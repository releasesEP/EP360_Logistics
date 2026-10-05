/* =====================================================================
   06_ReglasSincronizacionAD.sql
   BD global: EP360_Logistics   (servidor 60, maestro)
   REQUIERE haber corrido antes: 01 a 05.

   Reglas del directorio (para no traer usuarios basura ni cuentas de servicio): un usuario de AD
   aparece en el directorio SOLO si cumple TODO lo siguiente en Active Directory:
     activo (cuenta no deshabilitada), puesto, departamento, oficina, compania y jefe (manager).
   La aplicacion evalua las reglas y le avisa a la BD con @cumpleReglas.

   Cambios:
     1. sync.sp_SincronizarUsuarioAD (reemplaza la del 05)
        - nuevos parametros: @puesto (atributo title de AD) y @cumpleReglas
        - Persona.activo = (habilitado en AD) Y (cumple reglas)
        - dir.UsuarioAD.habilitadoAD sigue reflejando SOLO el estado real de la cuenta en AD
        - un usuario NUEVO que no queda activo se OMITE; uno existente que deja de cumplir se da de
          baja logica (y se reactiva si vuelve a cumplir)
        - el puesto de un usuario de AD ahora viene de AD
     2. dir.sp_ActualizarPersona (reemplaza la del 03): en un usuario de AD solo se editan telefono y
        extension (el puesto ya lo administra la sincronizacion).

   Es IDEMPOTENTE. No inserta datos ni toca otras bases.
   Quien lo corre: el dueno del proyecto, en SSMS, conectado al 60.
   ===================================================================== */

USE EP360_Logistics;
GO

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
    @cumpleReglas      BIT = 1
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

    IF @objectSid IS NULL THROW 50009, N'El objectSid es obligatorio.', 1;
    IF @samAccountName IS NULL OR @samAccountName = N'' THROW 50001, N'El samAccountName es obligatorio.', 1;

    -- Aparece en el directorio solo si esta habilitado en AD y cumple las reglas.
    DECLARE @activa BIT = CASE WHEN @habilitadoAD = 1 AND @cumpleReglas = 1 THEN 1 ELSE 0 END;

    -- Nuevo y que no queda activo: no se crea.
    IF @activa = 0 AND NOT EXISTS (SELECT 1 FROM dir.UsuarioAD WHERE objectSid = @objectSid)
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

        INSERT INTO dir.Persona (tipoPersona, nombreCompleto, correo, puesto, idDepartamento, idSucursal, activo)
        VALUES (N'AD', @nombreCompleto, @correoAD, @puesto, @idDepartamento, @idSucursal, @activa);
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
        IF @correoAD IS NOT NULL AND @activa = 1 AND EXISTS (SELECT 1 FROM dir.Persona WHERE correo = @correoAD AND activo = 1 AND idPersona <> @idPersona)
        BEGIN
            ROLLBACK;
            THROW 50007, N'El correo de AD ya lo usa otra persona activa. Revisar a mano.', 1;
        END

        UPDATE dir.Persona
        SET nombreCompleto = @nombreCompleto, correo = @correoAD, puesto = @puesto,
            idDepartamento = @idDepartamento, idSucursal = @idSucursal,
            activo = @activa, fechaModificacion = GETDATE()
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

/* En un usuario de AD solo se editan telefono y extension: nombre, correo, puesto, departamento y
   sucursal los administra la sincronizacion con AD. */
CREATE OR ALTER PROCEDURE dir.sp_ActualizarPersona
    @idPersona         INT,
    @nombreCompleto    NVARCHAR(150),
    @correo            NVARCHAR(200) = NULL,
    @telefono          NVARCHAR(40)  = NULL,
    @extension         NVARCHAR(20)  = NULL,
    @puesto            NVARCHAR(120) = NULL,
    @idDepartamento    INT = NULL,
    @idSucursal        INT = NULL,
    @idPersonaModifico INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @nombreCompleto = LTRIM(RTRIM(@nombreCompleto));
    SET @correo = NULLIF(LTRIM(RTRIM(@correo)), N'');

    DECLARE @tipo NVARCHAR(20) = (SELECT tipoPersona FROM dir.Persona WHERE idPersona = @idPersona);
    IF @tipo IS NULL THROW 50003, N'La persona no existe.', 1;

    IF @tipo = N'AD'
    BEGIN
        UPDATE dir.Persona
        SET telefono = @telefono, extension = @extension,
            fechaModificacion = GETDATE(), idPersonaModifico = @idPersonaModifico
        WHERE idPersona = @idPersona;
        RETURN;
    END

    IF @nombreCompleto IS NULL OR @nombreCompleto = N'' THROW 50001, N'El nombre es obligatorio.', 1;
    IF @tipo = N'Externo' AND @correo IS NULL THROW 50006, N'Un usuario externo necesita correo.', 1;
    IF @correo IS NOT NULL AND EXISTS (SELECT 1 FROM dir.Persona WHERE correo = @correo AND activo = 1 AND idPersona <> @idPersona)
        THROW 50007, N'Ya existe una persona activa con ese correo.', 1;

    UPDATE dir.Persona
    SET nombreCompleto = @nombreCompleto, correo = @correo, telefono = @telefono, extension = @extension,
        puesto = @puesto, idDepartamento = @idDepartamento, idSucursal = @idSucursal,
        fechaModificacion = GETDATE(), idPersonaModifico = @idPersonaModifico
    WHERE idPersona = @idPersona;
END
GO

PRINT '06_ReglasSincronizacionAD.sql terminado.';
GO
