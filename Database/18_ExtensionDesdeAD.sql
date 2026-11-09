/* =====================================================================
   18_ExtensionDesdeAD.sql
   BD global: EP360_Logistics   -> correr en el 60 Y en el 11 (cada uno por separado), DESPUES del 17.
   REQUIERE haber corrido antes: 01 a 17.

   Prepara la global para que HelpDesk (y cualquier portal) deje de guardar su propia copia de los datos de la persona
   (ver HelpDesk\Database\11_conectar_global.sql). Es IDEMPOTENTE. No inserta ni borra datos.

   Cambios:
     1. sync.sp_SincronizarUsuarioAD   (reemplaza la del 16)
        - parametro nuevo @extension (atributo ipPhone de AD): la extension de un usuario de AD sale SOLO de AD y se
          reescribe en cada sincronizacion (de madrugada). Hoy la global tiene 0 extensiones y HelpDesk 150.
        - la app de la global debe mandarlo (EP360_Logistics_Admin: UsuarioADModel.Extension / SincronizacionDAL).
          Mientras la app no se actualice, el parametro llega NULL y la extension queda vacia, igual que hoy.
          OJO: el Replicador repite la llamada en el 11, asi que este script debe estar en AMBOS servidores antes de
          desplegar la app.
     2. dir.vw_PersonaEstado   (nueva): TODAS las personas de AD, activas o no, con el motivo por el que no estan activas:
          Activo          cumple las reglas y esta en AD
          NoCumpleReglas  existe y esta habilitada en AD pero le falta puesto, departamento, oficina, compania o jefe
          Deshabilitado   la cuenta esta deshabilitada en AD
          Ausente         ya no aparece en AD (no se leyo en la ultima corrida terminada)
        dir.vw_Persona solo trae activos; un portal que NO quiere perder a quien no cumple las reglas (HelpDesk: nadie debe
        quedarse sin poder mandar tickets) necesita saber cual de los tres casos es.
     3. Permiso de lectura para IIS APPPOOL\HelpDesk sobre las dos vistas (solo si ese login existe: solo en el 11).
   ===================================================================== */

USE EP360_Logistics;
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* ---------- 1. Sincronizacion de un usuario de AD, ahora con extension ---------- */
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
    @motivo            NVARCHAR(400) = NULL,    -- por que no cumple, ya redactado ("Le falta: puesto, jefe" / "Es cuenta de sistema")
    @extension         NVARCHAR(20)  = NULL     -- atributo ipPhone de AD (la extension sale SOLO de AD)
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
    SET @extension      = NULLIF(LTRIM(RTRIM(@extension)), N'');

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

        INSERT INTO dir.Persona (tipoPersona, nombreCompleto, correo, puesto, idDepartamento, idSucursal, extension, activo)
        VALUES (N'AD', @nombreCompleto, @correoAD, @puesto, @idDepartamento, @idSucursal, @extension, @activa);
        SET @idPersona = SCOPE_IDENTITY();

        INSERT INTO dir.UsuarioAD (idPersona, objectSid, samAccountName, userPrincipalName, correoAD, habilitadoAD, fechaUltimaSincronizacion, idUltimaSincronizacion)
        VALUES (@idPersona, @objectSid, @samAccountName, @userPrincipalName, @correoAD, @habilitadoAD, GETDATE(), @idSincronizacion);
        SET @accion = N'Alta';

        INSERT INTO @cambios (campo, antes, despues)
        SELECT v.campo, NULL, v.valor
        FROM (VALUES (N'Correo', @correoAD), (N'Puesto', @puesto), (N'Departamento', @departamento), (N'Oficina', @sucursal), (N'Extension', @extension)) v (campo, valor)
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
                @activaAntes BIT, @samAntes NVARCHAR(100), @upnAntes NVARCHAR(200), @extAntes NVARCHAR(20);
        SELECT @nombreAntes = p.nombreCompleto, @correoAntes = p.correo, @puestoAntes = p.puesto, @idDepAntes = p.idDepartamento,
               @idSucAntes = p.idSucursal, @activaAntes = p.activo, @samAntes = u.samAccountName, @upnAntes = u.userPrincipalName, @extAntes = p.extension
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
                (N'Extension',     @extAntes, @extension),
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
                idDepartamento = @idDepartamento, idSucursal = @idSucursal, extension = @extension,
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

/* ---------- 2. Estado de cada persona de AD ---------- */
CREATE OR ALTER VIEW dir.vw_PersonaEstado AS
SELECT p.idPersona, u.samAccountName, p.activo AS activa, u.habilitadoAD,
       CASE
            WHEN u.habilitadoAD = 0 THEN N'Deshabilitado'
            WHEN p.activo = 1 THEN N'Activo'
            WHEN ISNULL(u.idUltimaSincronizacion, -1) <>
                 ISNULL((SELECT MAX(idSincronizacion) FROM sync.SincronizacionAD WHERE estado = N'Terminada'), -1) THEN N'Ausente'
            ELSE N'NoCumpleReglas'
       END AS estadoAD
FROM dir.Persona p
JOIN dir.UsuarioAD u ON u.idPersona = p.idPersona;
GO

/* ---------- 3. Permiso de lectura para quien use HelpDesk (solo donde exista su login) ----------
   La vista dbo.Usuarios de HelpDesk lee estas dos vistas. Entre bases de datos NO hay encadenamiento de propiedad, asi que
   CUALQUIER cuenta que ejecute un SP de HelpDesk que toque Usuarios necesita CONNECT y SELECT tambien aqui. Son las cuentas
   que hoy tienen acceso a HelpDeskEP en el 11: el pool de HelpDesk, el de Intranet (KPIs), el servidor de EP360 (integracion
   de tickets), helpdeskep y el administrador local. Solo se da a las que existen como login en ESTE servidor. */
DECLARE @cuentas TABLE (nombre SYSNAME);
INSERT @cuentas VALUES (N'IIS APPPOOL\HelpDesk'), (N'IIS APPPOOL\Intranet'), (N'EPLOGISTICS\helpdeskep'),
                       (N'EPLOGISTICS\EPL1-EP360SERVE$'), (N'EPL1-APPSERVER0\Administrator');
DECLARE @n SYSNAME, @sql NVARCHAR(MAX);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT nombre FROM @cuentas;
OPEN c; FETCH NEXT FROM c INTO @n;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF SUSER_ID(@n) IS NOT NULL
    BEGIN
        SET @sql = N'';
        IF USER_ID(@n) IS NULL SET @sql = N'CREATE USER ' + QUOTENAME(@n) + N' FOR LOGIN ' + QUOTENAME(@n) + N'; ';
        SET @sql = @sql + N'GRANT SELECT ON dir.vw_Persona TO ' + QUOTENAME(@n) + N'; GRANT SELECT ON dir.vw_PersonaEstado TO ' + QUOTENAME(@n) + N';';
        EXEC sys.sp_executesql @sql;
        PRINT N'Permiso de lectura dado a ' + @n;
    END
    ELSE
        PRINT N'Sin login en este servidor (no se da permiso): ' + @n;
    FETCH NEXT FROM c INTO @n;
END
CLOSE c; DEALLOCATE c;
GO

PRINT N'18_ExtensionDesdeAD.sql terminado.';
GO
