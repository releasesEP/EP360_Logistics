/* =====================================================================
   05_SincronizacionAD.sql
   BD global: EP360_Logistics   (servidor 60, maestro)
   REQUIERE haber corrido antes: 01, 02, 03 y 04.

   Esquema sync: sincronizacion de usuarios desde Active Directory y apoyo a la migracion.

   Crea:
     Tablas : sync.SincronizacionAD     (bitacora de cada corrida de la sincronizacion)
              sync.EquivalenciaEntidad  (id local de cada portal <-> id global; para la migracion)
              sync.BitacoraCambio       (quien cambio que; la usaran las pantallas de administracion)
     SPs    : sync.sp_IniciarSincronizacionAD, sync.sp_FinalizarSincronizacionAD,
              sync.sp_ObtenerSincronizacionesAD
   Reemplaza: sync.sp_SincronizarUsuarioAD (del script 03): un usuario NUEVO que ya viene
              deshabilitado en AD se OMITE (no se crea como persona inactiva); si ya existia, se
              actualiza y se da de baja logica como antes.

   Es IDEMPOTENTE. No inserta datos ni toca otras bases.
   Quien lo corre: el dueno del proyecto, en SSMS, conectado al 60.
   ===================================================================== */

USE EP360_Logistics;
GO

/* ---------------------------------------------------------------------
   sync.SincronizacionAD : una fila por corrida.
   estado: EnCurso, Terminada, ConErrores (termino pero algun usuario fallo), Fallida (no pudo leer AD).
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'sync.SincronizacionAD', N'U') IS NULL
BEGIN
    CREATE TABLE sync.SincronizacionAD
    (
        idSincronizacion INT IDENTITY(1,1) NOT NULL,
        fechaInicio      DATETIME          NOT NULL CONSTRAINT DF_SincronizacionAD_fechaInicio DEFAULT (GETDATE()),
        fechaFin         DATETIME          NULL,
        ejecutadoPor     NVARCHAR(100)     NULL,
        estado           NVARCHAR(20)      NOT NULL CONSTRAINT DF_SincronizacionAD_estado DEFAULT (N'EnCurso'),
        leidos           INT               NOT NULL CONSTRAINT DF_SincronizacionAD_leidos DEFAULT (0),
        altas            INT               NOT NULL CONSTRAINT DF_SincronizacionAD_altas DEFAULT (0),
        actualizaciones  INT               NOT NULL CONSTRAINT DF_SincronizacionAD_actualizaciones DEFAULT (0),
        omitidos         INT               NOT NULL CONSTRAINT DF_SincronizacionAD_omitidos DEFAULT (0),
        errores          INT               NOT NULL CONSTRAINT DF_SincronizacionAD_errores DEFAULT (0),
        detalleErrores   NVARCHAR(MAX)     NULL,
        CONSTRAINT PK_SincronizacionAD PRIMARY KEY CLUSTERED (idSincronizacion),
        CONSTRAINT CK_SincronizacionAD_estado CHECK (estado IN (N'EnCurso', N'Terminada', N'ConErrores', N'Fallida'))
    );
END
GO

/* ---------------------------------------------------------------------
   sync.EquivalenciaEntidad : tabla de equivalencias de la migracion.
   entidad: Persona, Cuenta, Grupo.  metodo: Automatica, Manual.
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'sync.EquivalenciaEntidad', N'U') IS NULL
BEGIN
    CREATE TABLE sync.EquivalenciaEntidad
    (
        idEquivalencia INT IDENTITY(1,1) NOT NULL,
        idPortal       INT               NOT NULL,
        entidad        NVARCHAR(20)      NOT NULL,
        idLocal        INT               NOT NULL,
        idGlobal       INT               NOT NULL,
        nombreOrigen   NVARCHAR(200)     NULL,
        metodo         NVARCHAR(20)      NOT NULL CONSTRAINT DF_EquivalenciaEntidad_metodo DEFAULT (N'Automatica'),
        revisado       BIT               NOT NULL CONSTRAINT DF_EquivalenciaEntidad_revisado DEFAULT (0),
        fechaCreacion  DATETIME          NOT NULL CONSTRAINT DF_EquivalenciaEntidad_fechaCreacion DEFAULT (GETDATE()),
        CONSTRAINT PK_EquivalenciaEntidad PRIMARY KEY CLUSTERED (idEquivalencia),
        CONSTRAINT FK_EquivalenciaEntidad_Portal FOREIGN KEY (idPortal) REFERENCES dir.Portal (idPortal),
        CONSTRAINT CK_EquivalenciaEntidad_entidad CHECK (entidad IN (N'Persona', N'Cuenta', N'Grupo')),
        CONSTRAINT CK_EquivalenciaEntidad_metodo  CHECK (metodo IN (N'Automatica', N'Manual'))
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_EquivalenciaEntidad_local' AND object_id = OBJECT_ID(N'sync.EquivalenciaEntidad'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_EquivalenciaEntidad_local ON sync.EquivalenciaEntidad (idPortal, entidad, idLocal);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EquivalenciaEntidad_global' AND object_id = OBJECT_ID(N'sync.EquivalenciaEntidad'))
    CREATE NONCLUSTERED INDEX IX_EquivalenciaEntidad_global ON sync.EquivalenciaEntidad (entidad, idGlobal);
GO

/* ---------------------------------------------------------------------
   sync.BitacoraCambio : auditoria de cambios del directorio (hoy ninguna tabla de directorio
   guarda quien la cambio).
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'sync.BitacoraCambio', N'U') IS NULL
BEGIN
    CREATE TABLE sync.BitacoraCambio
    (
        idBitacoraCambio BIGINT IDENTITY(1,1) NOT NULL,
        entidad          NVARCHAR(40)  NOT NULL,
        idEntidad        INT           NOT NULL,
        accion           NVARCHAR(30)  NOT NULL,
        detalle          NVARCHAR(500) NULL,
        idPersonaModifico INT          NULL,
        fecha            DATETIME      NOT NULL CONSTRAINT DF_BitacoraCambio_fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_BitacoraCambio PRIMARY KEY CLUSTERED (idBitacoraCambio),
        CONSTRAINT FK_BitacoraCambio_Persona FOREIGN KEY (idPersonaModifico) REFERENCES dir.Persona (idPersona)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BitacoraCambio_entidad' AND object_id = OBJECT_ID(N'sync.BitacoraCambio'))
    CREATE NONCLUSTERED INDEX IX_BitacoraCambio_entidad ON sync.BitacoraCambio (entidad, idEntidad, fecha DESC);
GO

/* =====================================================================
   CORRIDAS DE SINCRONIZACION
   ===================================================================== */
/* Abre una corrida. Solo una a la vez: si hay otra EnCurso de hace menos de 30 minutos, se rechaza
   (una anterior colgada mas de 30 min se marca Fallida y se permite empezar). */
CREATE OR ALTER PROCEDURE sync.sp_IniciarSincronizacionAD @ejecutadoPor NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;
    IF EXISTS (SELECT 1 FROM sync.SincronizacionAD WITH (UPDLOCK, HOLDLOCK)
               WHERE estado = N'EnCurso' AND fechaInicio > DATEADD(MINUTE, -30, GETDATE()))
    BEGIN
        ROLLBACK;
        THROW 50031, N'Ya hay una sincronizacion con AD en curso. Espera a que termine.', 1;
    END

    UPDATE sync.SincronizacionAD
    SET estado = N'Fallida', fechaFin = GETDATE(), detalleErrores = N'Se quedo en curso mas de 30 minutos (proceso interrumpido).'
    WHERE estado = N'EnCurso';

    INSERT INTO sync.SincronizacionAD (ejecutadoPor) VALUES (@ejecutadoPor);
    DECLARE @id INT = SCOPE_IDENTITY();
    COMMIT;
    SELECT @id AS idSincronizacion;
END
GO

CREATE OR ALTER PROCEDURE sync.sp_FinalizarSincronizacionAD
    @idSincronizacion INT,
    @estado           NVARCHAR(20),
    @leidos           INT,
    @altas            INT,
    @actualizaciones  INT,
    @omitidos         INT,
    @errores          INT,
    @detalleErrores   NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @estado NOT IN (N'Terminada', N'ConErrores', N'Fallida') THROW 50032, N'Estado de sincronizacion no valido.', 1;
    IF NOT EXISTS (SELECT 1 FROM sync.SincronizacionAD WHERE idSincronizacion = @idSincronizacion) THROW 50003, N'La sincronizacion no existe.', 1;

    UPDATE sync.SincronizacionAD
    SET estado = @estado, fechaFin = GETDATE(), leidos = @leidos, altas = @altas, actualizaciones = @actualizaciones,
        omitidos = @omitidos, errores = @errores, detalleErrores = @detalleErrores
    WHERE idSincronizacion = @idSincronizacion;
END
GO

CREATE OR ALTER PROCEDURE sync.sp_ObtenerSincronizacionesAD @maximo INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@maximo) idSincronizacion, fechaInicio, fechaFin, ejecutadoPor, estado,
           leidos, altas, actualizaciones, omitidos, errores, detalleErrores
    FROM sync.SincronizacionAD
    ORDER BY idSincronizacion DESC;
END
GO

/* =====================================================================
   sync.sp_SincronizarUsuarioAD (reemplaza la del script 03)
   Cambio: un usuario NUEVO que ya viene deshabilitado en AD se omite.
   Devuelve idPersona (NULL si se omitio) y accion: Alta / Actualizacion / Omitido.
   ===================================================================== */
CREATE OR ALTER PROCEDURE sync.sp_SincronizarUsuarioAD
    @objectSid         VARBINARY(85),
    @samAccountName    NVARCHAR(100),
    @nombreCompleto    NVARCHAR(150),
    @userPrincipalName NVARCHAR(200) = NULL,
    @correoAD          NVARCHAR(200) = NULL,
    @departamento      NVARCHAR(100) = NULL,
    @sucursal          NVARCHAR(100) = NULL,
    @habilitadoAD      BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @samAccountName = LTRIM(RTRIM(@samAccountName));
    SET @nombreCompleto = ISNULL(NULLIF(LTRIM(RTRIM(@nombreCompleto)), N''), @samAccountName);
    SET @correoAD       = NULLIF(LTRIM(RTRIM(@correoAD)), N'');
    SET @departamento   = NULLIF(LTRIM(RTRIM(@departamento)), N'');
    SET @sucursal       = NULLIF(LTRIM(RTRIM(@sucursal)), N'');

    IF @objectSid IS NULL THROW 50009, N'El objectSid es obligatorio.', 1;
    IF @samAccountName IS NULL OR @samAccountName = N'' THROW 50001, N'El samAccountName es obligatorio.', 1;

    -- Nuevo y deshabilitado: no se crea.
    IF @habilitadoAD = 0 AND NOT EXISTS (SELECT 1 FROM dir.UsuarioAD WHERE objectSid = @objectSid)
    BEGIN
        SELECT CAST(NULL AS INT) AS idPersona, N'Omitido' AS accion;
        RETURN;
    END

    DECLARE @idDepartamento INT, @idSucursal INT, @idPersona INT, @accion NVARCHAR(20);
    DECLARE @t TABLE (id INT);

    IF @departamento IS NOT NULL BEGIN DELETE @t; INSERT @t EXEC dir.sp_ObtenerOCrearDepartamento @departamento; SELECT @idDepartamento = id FROM @t; END
    IF @sucursal     IS NOT NULL BEGIN DELETE @t; INSERT @t EXEC dir.sp_ObtenerOCrearSucursal     @sucursal;     SELECT @idSucursal     = id FROM @t; END

    BEGIN TRAN;

    SELECT @idPersona = idPersona FROM dir.UsuarioAD WITH (UPDLOCK, HOLDLOCK) WHERE objectSid = @objectSid;

    IF @idPersona IS NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dir.UsuarioAD WHERE samAccountName = @samAccountName)
        BEGIN
            ROLLBACK;
            THROW 50010, N'Ya existe un usuario de AD con ese samAccountName pero con otro objectSid. Revisar a mano.', 1;
        END
        IF @correoAD IS NOT NULL AND EXISTS (SELECT 1 FROM dir.Persona WHERE correo = @correoAD AND activo = 1)
        BEGIN
            ROLLBACK;
            THROW 50007, N'Ya existe una persona activa con el correo de este usuario de AD. Revisar a mano.', 1;
        END

        INSERT INTO dir.Persona (tipoPersona, nombreCompleto, correo, idDepartamento, idSucursal, activo)
        VALUES (N'AD', @nombreCompleto, @correoAD, @idDepartamento, @idSucursal, @habilitadoAD);
        SET @idPersona = SCOPE_IDENTITY();

        INSERT INTO dir.UsuarioAD (idPersona, objectSid, samAccountName, userPrincipalName, correoAD, habilitadoAD, fechaUltimaSincronizacion)
        VALUES (@idPersona, @objectSid, @samAccountName, @userPrincipalName, @correoAD, @habilitadoAD, GETDATE());
        SET @accion = N'Alta';
    END
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM dir.UsuarioAD WHERE samAccountName = @samAccountName AND idPersona <> @idPersona)
        BEGIN
            ROLLBACK;
            THROW 50010, N'El samAccountName ya pertenece a otro usuario de AD. Revisar a mano.', 1;
        END
        IF @correoAD IS NOT NULL AND @habilitadoAD = 1 AND EXISTS (SELECT 1 FROM dir.Persona WHERE correo = @correoAD AND activo = 1 AND idPersona <> @idPersona)
        BEGIN
            ROLLBACK;
            THROW 50007, N'El correo de AD ya lo usa otra persona activa. Revisar a mano.', 1;
        END

        UPDATE dir.Persona
        SET nombreCompleto = @nombreCompleto, correo = @correoAD, idDepartamento = @idDepartamento, idSucursal = @idSucursal,
            -- Deshabilitado en AD = baja logica; si vuelve a habilitarse, se reactiva.
            activo = @habilitadoAD, fechaModificacion = GETDATE()
        WHERE idPersona = @idPersona;

        UPDATE dir.UsuarioAD
        SET samAccountName = @samAccountName, userPrincipalName = @userPrincipalName, correoAD = @correoAD,
            habilitadoAD = @habilitadoAD, fechaUltimaSincronizacion = GETDATE()
        WHERE idPersona = @idPersona;
        SET @accion = N'Actualizacion';
    END

    COMMIT;
    SELECT @idPersona AS idPersona, @accion AS accion;
END
GO

PRINT '05_SincronizacionAD.sql terminado.';
SELECT SCHEMA_NAME(schema_id) AS esquema, name, type_desc FROM sys.objects
WHERE SCHEMA_NAME(schema_id) = N'sync' ORDER BY type_desc, name;
GO
