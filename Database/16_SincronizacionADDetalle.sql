/* =====================================================================
   16_SincronizacionADDetalle.sql
   BD global: EP360_Logistics   -> correr en el 60 Y en el 11 (cada uno por separado), DESPUES del 15.
   Correr en LOS DOS ANTES de desplegar el codigo que lo usa: la sincronizacion llama a estos procedimientos con
   parametros nuevos y el Replicador los repite en el 11; si alli no existen, la replicacion se detiene (Divergente).

   Pedido (2026-11-05):
     1. Ver que se hizo EXACTAMENTE en cada sincronizacion: cada valor nuevo queda registrado con la fecha de la corrida.
     2. Todo lo que ya no esta en AD se inhabilita: personas, sucursales y departamentos.

   Crea / cambia:
     sync.SincronizacionADDetalle        una fila por cambio (alta, campo cambiado con antes/despues, baja, reactivacion,
                                         omitido, error, sucursal o departamento desactivado/reactivado).
     dir.UsuarioAD.idUltimaSincronizacion  en que corrida se vio por ultima vez a ese usuario en AD.
     sync.SincronizacionAD                 + bajas, reactivaciones, sinCambios, ausentes (antes "actualizaciones" contaba
                                           a todos los que ya existian aunque no les cambiara nada).
     sync.sp_SincronizarUsuarioAD          + @idSincronizacion, @motivo. Compara antes/despues, registra cada campo que cambio,
                                           solo toca fechaModificacion si algo cambio. Devuelve (idPersona, accion) con accion =
                                           Alta | Actualizacion | Baja | Reactivacion | SinCambios | Omitido. La PRIMERA columna sigue
                                           siendo idPersona (es lo que compara el Replicador entre el 60 y el 11).
     sync.sp_DesactivarAusentesAD          da de baja a las personas de AD activas que NO se leyeron en esta corrida (ya no existen en AD).
                                           Se niega si se leyeron muy pocos usuarios (lectura de AD incompleta).
     sync.sp_RegistrarDetalleSincronizacionAD  para lo que registra la aplicacion (errores). Tambien marca al usuario como visto,
                                           para que un error suyo no lo haga parecer ausente.
     dir.sp_ReconciliarSucursales          + @idSincronizacion (registra en el detalle lo que desactiva/reactiva).
     dir.sp_ReconciliarDepartamentos       NUEVO: igual que sucursales, para departamentos sin personas activas.
     dir.sp_ObtenerOCrearDepartamento      si el departamento existe inactivo lo REACTIVA en vez de duplicarlo.
     sync.sp_FinalizarSincronizacionAD     + conteos nuevos (opcionales).
     sync.sp_ObtenerSincronizacionesAD     + columnas nuevas.
     sync.sp_ObtenerSincronizacionAD       NUEVO: una corrida.
     sync.sp_ObtenerDetalleSincronizacionAD NUEVO: el detalle de una corrida.

   Es IDEMPOTENTE. No borra nada. Los procedimientos que escriben no devuelven conteos (el Replicador los compararia).
   ===================================================================== */

USE EP360_Logistics;
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* ---------- Tablas y columnas ---------- */
IF OBJECT_ID(N'sync.SincronizacionADDetalle', N'U') IS NULL
BEGIN
    CREATE TABLE sync.SincronizacionADDetalle
    (
        idDetalle        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SincronizacionADDetalle PRIMARY KEY,
        idSincronizacion INT            NOT NULL CONSTRAINT FK_SincronizacionADDetalle_Sincronizacion REFERENCES sync.SincronizacionAD (idSincronizacion),
        fecha            DATETIME       NOT NULL CONSTRAINT DF_SincronizacionADDetalle_fecha DEFAULT (GETDATE()),
        entidad          NVARCHAR(20)   NOT NULL,   -- Persona | Sucursal | Departamento
        idEntidad        INT            NULL,
        nombre           NVARCHAR(150)  NULL,
        usuario          NVARCHAR(100)  NULL,       -- samAccountName
        accion           NVARCHAR(20)   NOT NULL,   -- Alta | Cambio | Baja | Reactivacion | Omitido | Error | Desactivar | Reactivar
        campo            NVARCHAR(40)   NULL,
        valorAnterior    NVARCHAR(300)  NULL,
        valorNuevo       NVARCHAR(300)  NULL,
        motivo           NVARCHAR(400)  NULL
    );
    CREATE INDEX IX_SincronizacionADDetalle_corrida ON sync.SincronizacionADDetalle (idSincronizacion, idDetalle);
END
GO

IF COL_LENGTH(N'dir.UsuarioAD', N'idUltimaSincronizacion') IS NULL
    ALTER TABLE dir.UsuarioAD ADD idUltimaSincronizacion INT NULL;
GO
IF COL_LENGTH(N'sync.SincronizacionAD', N'bajas') IS NULL
    ALTER TABLE sync.SincronizacionAD ADD
        bajas          INT NOT NULL CONSTRAINT DF_SincronizacionAD_bajas DEFAULT (0),
        reactivaciones INT NOT NULL CONSTRAINT DF_SincronizacionAD_reactivaciones DEFAULT (0),
        sinCambios     INT NOT NULL CONSTRAINT DF_SincronizacionAD_sinCambios DEFAULT (0),
        ausentes       INT NOT NULL CONSTRAINT DF_SincronizacionAD_ausentes DEFAULT (0);
GO

/* ---------- Departamento: reactivar en vez de duplicar ---------- */
CREATE OR ALTER PROCEDURE dir.sp_ObtenerOCrearDepartamento @nombre NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @nombre = LTRIM(RTRIM(@nombre));
    IF @nombre IS NULL OR @nombre = N'' THROW 50001, N'El nombre del departamento es obligatorio.', 1;

    DECLARE @id INT;
    BEGIN TRAN;
    SELECT @id = idDepartamento FROM dir.Departamento WITH (UPDLOCK, HOLDLOCK) WHERE nombre = @nombre AND activo = 1;
    IF @id IS NULL
    BEGIN
        -- Existe pero se desactivo (por ejemplo, se quedo sin personas): se reactiva, no se duplica.
        SELECT TOP (1) @id = idDepartamento FROM dir.Departamento WITH (UPDLOCK, HOLDLOCK)
        WHERE nombre = @nombre AND activo = 0 ORDER BY idDepartamento DESC;
        IF @id IS NOT NULL
        BEGIN
            UPDATE dir.Departamento SET activo = 1, fechaModificacion = GETDATE() WHERE idDepartamento = @id;
            INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle)
            VALUES (N'Departamento', @id, N'Reactivar', N'Volvio a tener personas (sincronizacion con AD)');
        END
        ELSE
        BEGIN
            INSERT INTO dir.Departamento (nombre) VALUES (@nombre);
            SET @id = SCOPE_IDENTITY();
        END
    END
    COMMIT;
    SELECT @id AS idDepartamento;
END
GO

/* ---------- Usuario de AD con detalle por campo ---------- */
CREATE OR ALTER PROCEDURE sync.sp_SincronizarUsuarioAD
    @objectSid         VARBINARY(85),
    @samAccountName    NVARCHAR(100),
    @nombreCompleto    NVARCHAR(150),
    @userPrincipalName NVARCHAR(200) = NULL,
    @correoAD          NVARCHAR(200) = NULL,
    @puesto            NVARCHAR(120) = NULL,
    @departamento      NVARCHAR(100) = NULL,
    @sucursal          NVARCHAR(100) = NULL,
    @habilitadoAD      BIT = 1,
    @cumpleReglas      BIT = 1,
    @idSincronizacion  INT = NULL,
    @motivo            NVARCHAR(400) = NULL     -- por que no cumple, ya redactado ("Le falta: puesto, jefe" / "Es cuenta de sistema")
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @samAccountName = LTRIM(RTRIM(@samAccountName));
    SET @nombreCompleto = ISNULL(NULLIF(LTRIM(RTRIM(@nombreCompleto)), N''), @samAccountName);
    SET @correoAD       = NULLIF(LTRIM(RTRIM(@correoAD)), N'');
    SET @puesto         = NULLIF(LTRIM(RTRIM(@puesto)), N'');
    SET @departamento   = NULLIF(LTRIM(RTRIM(@departamento)), N'');
    SET @sucursal       = NULLIF(LTRIM(RTRIM(@sucursal)), N'');
    SET @motivo         = NULLIF(LTRIM(RTRIM(@motivo)), N'');

    IF @objectSid IS NULL THROW 50009, N'El objectSid es obligatorio.', 1;
    IF @samAccountName IS NULL OR @samAccountName = N'' THROW 50001, N'El samAccountName es obligatorio.', 1;

    DECLARE @activa BIT = CASE WHEN @habilitadoAD = 1 AND @cumpleReglas = 1 THEN 1 ELSE 0 END;
    IF @habilitadoAD = 0 SET @motivo = ISNULL(@motivo, N'Cuenta deshabilitada en AD');

    -- Nuevo y que no queda activo: no se crea (queda en el detalle como Omitido).
    IF @activa = 0 AND NOT EXISTS (SELECT 1 FROM dir.UsuarioAD WHERE objectSid = @objectSid)
    BEGIN
        IF @idSincronizacion IS NOT NULL
            INSERT INTO sync.SincronizacionADDetalle (idSincronizacion, entidad, nombre, usuario, accion, motivo)
            VALUES (@idSincronizacion, N'Persona', @nombreCompleto, @samAccountName, N'Omitido', ISNULL(@motivo, N'No cumple las reglas'));
        SELECT CAST(NULL AS INT) AS idPersona, N'Omitido' AS accion;
        RETURN;
    END

    DECLARE @idDepartamento INT, @idSucursal INT, @idPersona INT, @accion NVARCHAR(20);
    DECLARE @t TABLE (id INT);

    IF @departamento IS NOT NULL BEGIN DELETE @t; INSERT @t EXEC dir.sp_ObtenerOCrearDepartamento @departamento; SELECT @idDepartamento = id FROM @t; END
    IF @sucursal     IS NOT NULL BEGIN DELETE @t; INSERT @t EXEC dir.sp_ObtenerOCrearSucursal     @sucursal;     SELECT @idSucursal     = id FROM @t; END

    DECLARE @cambios TABLE (campo NVARCHAR(40), antes NVARCHAR(300), despues NVARCHAR(300));

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

        INSERT INTO dir.Persona (tipoPersona, nombreCompleto, correo, puesto, idDepartamento, idSucursal, activo)
        VALUES (N'AD', @nombreCompleto, @correoAD, @puesto, @idDepartamento, @idSucursal, @activa);
        SET @idPersona = SCOPE_IDENTITY();

        INSERT INTO dir.UsuarioAD (idPersona, objectSid, samAccountName, userPrincipalName, correoAD, habilitadoAD, fechaUltimaSincronizacion, idUltimaSincronizacion)
        VALUES (@idPersona, @objectSid, @samAccountName, @userPrincipalName, @correoAD, @habilitadoAD, GETDATE(), @idSincronizacion);
        SET @accion = N'Alta';

        INSERT INTO @cambios (campo, antes, despues)
        SELECT v.campo, NULL, v.valor
        FROM (VALUES (N'Correo', @correoAD), (N'Puesto', @puesto), (N'Departamento', @departamento), (N'Oficina', @sucursal)) v (campo, valor)
        WHERE v.valor IS NOT NULL;
    END
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM dir.UsuarioAD WHERE samAccountName = @samAccountName AND idPersona <> @idPersona)
        BEGIN
            ROLLBACK;
            THROW 50010, N'El samAccountName ya pertenece a otro usuario de AD. Revisar a mano.', 1;
        END
        IF @correoAD IS NOT NULL AND @activa = 1 AND EXISTS (SELECT 1 FROM dir.Persona WHERE correo = @correoAD AND activo = 1 AND idPersona <> @idPersona)
        BEGIN
            ROLLBACK;
            THROW 50007, N'El correo de AD ya lo usa otra persona activa. Revisar a mano.', 1;
        END

        -- Valores de antes, para registrar SOLO lo que cambia.
        DECLARE @nombreAntes NVARCHAR(150), @correoAntes NVARCHAR(200), @puestoAntes NVARCHAR(120), @idDepAntes INT, @idSucAntes INT,
                @activaAntes BIT, @samAntes NVARCHAR(100), @upnAntes NVARCHAR(200);
        SELECT @nombreAntes = p.nombreCompleto, @correoAntes = p.correo, @puestoAntes = p.puesto, @idDepAntes = p.idDepartamento,
               @idSucAntes = p.idSucursal, @activaAntes = p.activo, @samAntes = u.samAccountName, @upnAntes = u.userPrincipalName
        FROM dir.Persona p JOIN dir.UsuarioAD u ON u.idPersona = p.idPersona
        WHERE p.idPersona = @idPersona;

        INSERT INTO @cambios (campo, antes, despues)
        SELECT v.campo, v.antes, v.despues
        FROM (VALUES
                (N'Nombre',        @nombreAntes, @nombreCompleto),
                (N'Correo',        @correoAntes, @correoAD),
                (N'Puesto',        @puestoAntes, @puesto),
                (N'Departamento',  (SELECT nombre FROM dir.Departamento WHERE idDepartamento = @idDepAntes), (SELECT nombre FROM dir.Departamento WHERE idDepartamento = @idDepartamento)),
                (N'Oficina',       (SELECT nombre FROM dir.Sucursal WHERE idSucursal = @idSucAntes), (SELECT nombre FROM dir.Sucursal WHERE idSucursal = @idSucursal)),
                (N'Usuario',       @samAntes, @samAccountName),
                (N'Correo de AD (UPN)', @upnAntes, @userPrincipalName)
             ) v (campo, antes, despues)
        WHERE ISNULL(v.antes, N'') COLLATE Latin1_General_CS_AS <> ISNULL(v.despues, N'') COLLATE Latin1_General_CS_AS;

        SET @accion = CASE
            WHEN @activaAntes = 1 AND @activa = 0 THEN N'Baja'
            WHEN @activaAntes = 0 AND @activa = 1 THEN N'Reactivacion'
            WHEN EXISTS (SELECT 1 FROM @cambios) THEN N'Actualizacion'
            ELSE N'SinCambios' END;

        IF @accion <> N'SinCambios'
            UPDATE dir.Persona
            SET nombreCompleto = @nombreCompleto, correo = @correoAD, puesto = @puesto,
                idDepartamento = @idDepartamento, idSucursal = @idSucursal,
                activo = @activa, fechaModificacion = GETDATE()
            WHERE idPersona = @idPersona;

        UPDATE dir.UsuarioAD
        SET samAccountName = @samAccountName, userPrincipalName = @userPrincipalName, correoAD = @correoAD,
            habilitadoAD = @habilitadoAD, fechaUltimaSincronizacion = GETDATE(),
            idUltimaSincronizacion = ISNULL(@idSincronizacion, idUltimaSincronizacion)
        WHERE idPersona = @idPersona;
    END

    IF @idSincronizacion IS NOT NULL AND @accion <> N'SinCambios'
    BEGIN
        -- Renglon principal (alta, baja, reactivacion) y uno por cada campo que cambio.
        IF @accion IN (N'Alta', N'Baja', N'Reactivacion')
            INSERT INTO sync.SincronizacionADDetalle (idSincronizacion, entidad, idEntidad, nombre, usuario, accion, motivo)
            VALUES (@idSincronizacion, N'Persona', @idPersona, @nombreCompleto, @samAccountName, @accion,
                    CASE @accion WHEN N'Alta' THEN N'Nuevo en AD y cumple las reglas'
                                 WHEN N'Baja' THEN ISNULL(@motivo, N'Dejo de cumplir las reglas')
                                 ELSE N'Volvio a cumplir las reglas' END);

        INSERT INTO sync.SincronizacionADDetalle (idSincronizacion, entidad, idEntidad, nombre, usuario, accion, campo, valorAnterior, valorNuevo)
        SELECT @idSincronizacion, N'Persona', @idPersona, @nombreCompleto, @samAccountName,
               CASE WHEN @accion = N'Alta' THEN N'Alta' ELSE N'Cambio' END, campo, antes, despues
        FROM @cambios;
    END

    COMMIT;
    SELECT @idPersona AS idPersona, @accion AS accion;
END
GO

/* ---------- Personas que ya no existen en AD ---------- */
CREATE OR ALTER PROCEDURE sync.sp_DesactivarAusentesAD
    @idSincronizacion INT,
    @leidos           INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Freno de seguridad: si AD devolvio menos usuarios que las personas de AD activas, la lectura salio incompleta
    -- y dar de baja a "los que faltan" vaciaria el directorio.
    DECLARE @activas INT = (SELECT COUNT(*) FROM dir.Persona p JOIN dir.UsuarioAD u ON u.idPersona = p.idPersona WHERE p.activo = 1);
    IF @leidos < @activas
        THROW 50033, N'Se leyeron menos usuarios de AD que personas activas: no se da de baja a nadie por ausencia.', 1;

    DECLARE @bajas TABLE (idPersona INT PRIMARY KEY);

    BEGIN TRAN;
    UPDATE p SET activo = 0, fechaModificacion = GETDATE()
    OUTPUT inserted.idPersona INTO @bajas
    FROM dir.Persona p
    JOIN dir.UsuarioAD u ON u.idPersona = p.idPersona
    WHERE p.activo = 1
      AND ISNULL(u.idUltimaSincronizacion, -1) <> @idSincronizacion;

    INSERT INTO sync.SincronizacionADDetalle (idSincronizacion, entidad, idEntidad, nombre, usuario, accion, campo, valorAnterior, valorNuevo, motivo)
    SELECT @idSincronizacion, N'Persona', p.idPersona, p.nombreCompleto, u.samAccountName, N'Baja', N'Estado', N'Activo', N'Inactivo', N'Ya no existe en AD'
    FROM @bajas b
    JOIN dir.Persona p ON p.idPersona = b.idPersona
    JOIN dir.UsuarioAD u ON u.idPersona = b.idPersona;

    INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle)
    SELECT N'Persona', idPersona, N'Desactivar', N'Ya no existe en AD (sincronizacion con AD)' FROM @bajas;

    UPDATE sync.SincronizacionAD SET ausentes = (SELECT COUNT(*) FROM @bajas) WHERE idSincronizacion = @idSincronizacion;
    COMMIT;
END
GO

/* ---------- Lo que registra la aplicacion (errores por usuario) ---------- */
CREATE OR ALTER PROCEDURE sync.sp_RegistrarDetalleSincronizacionAD
    @idSincronizacion INT,
    @objectSid        VARBINARY(85) = NULL,
    @usuario          NVARCHAR(100) = NULL,
    @nombre           NVARCHAR(150) = NULL,
    @accion           NVARCHAR(20),
    @motivo           NVARCHAR(400) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sync.SincronizacionADDetalle (idSincronizacion, entidad, idEntidad, nombre, usuario, accion, motivo)
    VALUES (@idSincronizacion, N'Persona', (SELECT idPersona FROM dir.UsuarioAD WHERE objectSid = @objectSid), @nombre, @usuario, @accion, LEFT(@motivo, 400));

    -- Un usuario que fallo SI esta en AD: se marca como visto para que no se le de de baja por "ausente".
    IF @objectSid IS NOT NULL
        UPDATE dir.UsuarioAD SET idUltimaSincronizacion = @idSincronizacion WHERE objectSid = @objectSid;
END
GO

/* ---------- Sucursales y departamentos sin uso ---------- */
CREATE OR ALTER PROCEDURE dir.sp_ReconciliarSucursales @idSincronizacion INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @marca NVARCHAR(100) = N'Sin uso (sincronizacion con AD)';
    DECLARE @cambiadas TABLE (idSucursal INT PRIMARY KEY);

    BEGIN TRAN;

    -- 1. Retira las que ya no tienen a nadie (ni personas ni cuentas activas).
    UPDATE s SET activo = 0, fechaModificacion = GETDATE()
    OUTPUT inserted.idSucursal INTO @cambiadas
    FROM dir.Sucursal s
    WHERE s.activo = 1
      AND NOT EXISTS (SELECT 1 FROM dir.Persona p WHERE p.idSucursal = s.idSucursal AND p.activo = 1)
      AND NOT EXISTS (SELECT 1 FROM dir.Cuenta  c WHERE c.idSucursal = s.idSucursal AND c.activo = 1);

    INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle)
    SELECT N'Sucursal', idSucursal, N'Desactivar', @marca FROM @cambiadas;
    IF @idSincronizacion IS NOT NULL
        INSERT INTO sync.SincronizacionADDetalle (idSincronizacion, entidad, idEntidad, nombre, accion, campo, valorAnterior, valorNuevo, motivo)
        SELECT @idSincronizacion, N'Sucursal', s.idSucursal, s.nombre, N'Desactivar', N'Estado', N'Activa', N'Inactiva', N'Ninguna persona ni cuenta activa la usa'
        FROM @cambiadas c JOIN dir.Sucursal s ON s.idSucursal = c.idSucursal;

    DELETE FROM @cambiadas;

    -- 2. Reactiva las que retiro este mecanismo y volvieron a tener personas activas.
    UPDATE s SET activo = 1, fechaModificacion = GETDATE()
    OUTPUT inserted.idSucursal INTO @cambiadas
    FROM dir.Sucursal s
    WHERE s.activo = 0
      AND EXISTS (SELECT 1 FROM dir.Persona p WHERE p.idSucursal = s.idSucursal AND p.activo = 1)
      AND ISNULL((SELECT TOP (1) b.detalle FROM sync.BitacoraCambio b
                  WHERE b.entidad = N'Sucursal' AND b.idEntidad = s.idSucursal AND b.accion IN (N'Desactivar', N'Reactivar')
                  ORDER BY b.idBitacoraCambio DESC), N'') = @marca;

    INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle)
    SELECT N'Sucursal', idSucursal, N'Reactivar', N'Volvio a tener personas (sincronizacion con AD)' FROM @cambiadas;
    IF @idSincronizacion IS NOT NULL
        INSERT INTO sync.SincronizacionADDetalle (idSincronizacion, entidad, idEntidad, nombre, accion, campo, valorAnterior, valorNuevo, motivo)
        SELECT @idSincronizacion, N'Sucursal', s.idSucursal, s.nombre, N'Reactivar', N'Estado', N'Inactiva', N'Activa', N'Volvio a tener personas'
        FROM @cambiadas c JOIN dir.Sucursal s ON s.idSucursal = c.idSucursal;

    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ReconciliarDepartamentos @idSincronizacion INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Los departamentos solo los usan las personas. Uno sin personas activas se desactiva (no se borra: el historial
    -- de otros portales puede apuntarle). Si alguien vuelve a traerlo desde AD, dir.sp_ObtenerOCrearDepartamento lo reactiva.
    DECLARE @cambiadas TABLE (idDepartamento INT PRIMARY KEY);

    BEGIN TRAN;
    UPDATE d SET activo = 0, fechaModificacion = GETDATE()
    OUTPUT inserted.idDepartamento INTO @cambiadas
    FROM dir.Departamento d
    WHERE d.activo = 1
      AND NOT EXISTS (SELECT 1 FROM dir.Persona p WHERE p.idDepartamento = d.idDepartamento AND p.activo = 1);

    INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle)
    SELECT N'Departamento', idDepartamento, N'Desactivar', N'Sin uso (sincronizacion con AD)' FROM @cambiadas;
    IF @idSincronizacion IS NOT NULL
        INSERT INTO sync.SincronizacionADDetalle (idSincronizacion, entidad, idEntidad, nombre, accion, campo, valorAnterior, valorNuevo, motivo)
        SELECT @idSincronizacion, N'Departamento', d.idDepartamento, d.nombre, N'Desactivar', N'Estado', N'Activo', N'Inactivo', N'Ninguna persona activa lo usa'
        FROM @cambiadas c JOIN dir.Departamento d ON d.idDepartamento = c.idDepartamento;
    COMMIT;
END
GO

/* ---------- Cierre y consultas ---------- */
CREATE OR ALTER PROCEDURE sync.sp_FinalizarSincronizacionAD
    @idSincronizacion INT,
    @estado           NVARCHAR(20),
    @leidos           INT,
    @altas            INT,
    @actualizaciones  INT,
    @omitidos         INT,
    @errores          INT,
    @detalleErrores   NVARCHAR(MAX) = NULL,
    @bajas            INT = 0,
    @reactivaciones   INT = 0,
    @sinCambios       INT = 0
AS
BEGIN
    SET NOCOUNT ON;
    IF @estado NOT IN (N'Terminada', N'ConErrores', N'Fallida') THROW 50032, N'Estado de sincronizacion no valido.', 1;
    IF NOT EXISTS (SELECT 1 FROM sync.SincronizacionAD WHERE idSincronizacion = @idSincronizacion) THROW 50003, N'La sincronizacion no existe.', 1;

    UPDATE sync.SincronizacionAD
    SET estado = @estado, fechaFin = GETDATE(), leidos = @leidos, altas = @altas, actualizaciones = @actualizaciones,
        omitidos = @omitidos, errores = @errores, detalleErrores = @detalleErrores,
        bajas = @bajas, reactivaciones = @reactivaciones, sinCambios = @sinCambios
    WHERE idSincronizacion = @idSincronizacion;
END
GO

CREATE OR ALTER PROCEDURE sync.sp_ObtenerSincronizacionesAD @maximo INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@maximo) s.idSincronizacion, s.fechaInicio, s.fechaFin, s.ejecutadoPor, s.estado,
           s.leidos, s.altas, s.actualizaciones, s.omitidos, s.errores, s.detalleErrores,
           s.bajas, s.reactivaciones, s.sinCambios, s.ausentes,
           (SELECT COUNT(*) FROM sync.SincronizacionADDetalle d WHERE d.idSincronizacion = s.idSincronizacion) AS renglonesDetalle
    FROM sync.SincronizacionAD s
    ORDER BY s.idSincronizacion DESC;
END
GO

CREATE OR ALTER PROCEDURE sync.sp_ObtenerSincronizacionAD @idSincronizacion INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.idSincronizacion, s.fechaInicio, s.fechaFin, s.ejecutadoPor, s.estado,
           s.leidos, s.altas, s.actualizaciones, s.omitidos, s.errores, s.detalleErrores,
           s.bajas, s.reactivaciones, s.sinCambios, s.ausentes,
           (SELECT COUNT(*) FROM sync.SincronizacionADDetalle d WHERE d.idSincronizacion = s.idSincronizacion) AS renglonesDetalle
    FROM sync.SincronizacionAD s
    WHERE s.idSincronizacion = @idSincronizacion;
END
GO

CREATE OR ALTER PROCEDURE sync.sp_ObtenerDetalleSincronizacionAD @idSincronizacion INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT idDetalle, fecha, entidad, idEntidad, nombre, usuario, accion, campo, valorAnterior, valorNuevo, motivo
    FROM sync.SincronizacionADDetalle
    WHERE idSincronizacion = @idSincronizacion
    ORDER BY CASE accion WHEN N'Error' THEN 0 WHEN N'Baja' THEN 1 WHEN N'Alta' THEN 2 WHEN N'Reactivacion' THEN 3
                         WHEN N'Cambio' THEN 4 WHEN N'Desactivar' THEN 5 WHEN N'Reactivar' THEN 6 ELSE 7 END,
             nombre, idDetalle;
END
GO

-- Permisos: la cuenta del servidor de aplicaciones ejecuta todo en la base (12b_PermisosAppGlobal.sql da EXECUTE a nivel base),
-- asi que los procedimientos nuevos no necesitan GRANT propio.
