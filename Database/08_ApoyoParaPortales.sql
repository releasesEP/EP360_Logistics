/* =====================================================================
   08_ApoyoParaPortales.sql
   BD global: EP360_Logistics   (servidor 60, maestro)
   REQUIERE haber corrido antes: 01 a 07.

   Ajustes para que un PORTAL (primero EP360) pueda llamar a la global desde sus propios
   stored procedures y recuperar el id que se genero:

     1. dir.sp_InsertarGrupoCuenta, dir.sp_InsertarCuenta y dir.sp_InsertarPersona:
        nuevos parametros opcionales
           @devolverResultado BIT = 1    -> con 1 (default) se comporta IGUAL que antes: devuelve el
                                            id en un result set. Con 0 NO devuelve result set.
           @id... INT = NULL OUTPUT      -> devuelve el id nuevo por parametro de salida.
        Por que: SQL Server no permite capturar un result set con INSERT ... EXEC cuando el SP
        llamado hace ROLLBACK (y estos lo hacen al detectar duplicados); con OUTPUT el portal
        obtiene el id sin ese problema y el mensaje de error original llega intacto.
        La app de administracion (EP360_Logistics_Admin) no cambia: usa los defaults.

     2. seg.sp_FijarPasswordCredencial: un ADMINISTRADOR de un portal fija el hash de la
        contrasena de un usuario externo (EP360 hoy da de alta externos con contrasena inicial).
        Crea la credencial si no existe, la deja Activa y convierte un Contacto en Externo.
        La app calcula el hash (PBKDF2); la BD nunca ve la contrasena en claro.

   Es IDEMPOTENTE (CREATE OR ALTER). No inserta datos ni toca otras bases.
   Quien lo corre: el dueno del proyecto, en SSMS, conectado al 60.
   ===================================================================== */

USE EP360_Logistics;
GO

CREATE OR ALTER PROCEDURE dir.sp_InsertarGrupoCuenta
    @nombre            NVARCHAR(180),
    @devolverResultado BIT = 1,
    @idGrupoCuenta     INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @nombre = LTRIM(RTRIM(@nombre));
    IF @nombre IS NULL OR @nombre = N'' THROW 50001, N'El nombre del grupo es obligatorio.', 1;

    BEGIN TRAN;
    IF EXISTS (SELECT 1 FROM dir.GrupoCuenta WITH (UPDLOCK, HOLDLOCK) WHERE nombre = @nombre AND activo = 1)
    BEGIN
        ROLLBACK;
        THROW 50002, N'Ya existe un grupo activo con ese nombre.', 1;
    END

    DECLARE @prefijo NVARCHAR(20) = N'CGRP-' + FORMAT(GETDATE(), 'yyMMdd') + N'-';
    DECLARE @n INT = (SELECT COUNT(*) FROM dir.GrupoCuenta WITH (UPDLOCK, HOLDLOCK) WHERE folio LIKE @prefijo + N'%') + 1;

    INSERT INTO dir.GrupoCuenta (folio, nombre) VALUES (@prefijo + CAST(@n AS NVARCHAR(10)), @nombre);
    SET @idGrupoCuenta = SCOPE_IDENTITY();
    COMMIT;

    IF @devolverResultado = 1 SELECT @idGrupoCuenta AS idGrupoCuenta;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_InsertarCuenta
    @nombreComercial   NVARCHAR(180),
    @codigoCuenta      NVARCHAR(60)  = NULL,
    @razonSocial       NVARCHAR(180) = NULL,
    @rfc               NVARCHAR(20)  = NULL,
    @idGrupoCuenta     INT = NULL,
    @idCiudad          INT = NULL,
    @idSucursal        INT = NULL,
    @devolverResultado BIT = 1,
    @idCuenta          INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @nombreComercial = LTRIM(RTRIM(@nombreComercial));
    IF @nombreComercial IS NULL OR @nombreComercial = N'' THROW 50001, N'El nombre de la cuenta es obligatorio.', 1;
    IF @idGrupoCuenta IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dir.GrupoCuenta WHERE idGrupoCuenta = @idGrupoCuenta AND activo = 1)
        THROW 50004, N'El grupo indicado no existe o esta inactivo.', 1;

    BEGIN TRAN;
    DECLARE @prefijo NVARCHAR(20) = N'CCLI-' + FORMAT(GETDATE(), 'yyMMdd') + N'-';
    DECLARE @n INT = (SELECT COUNT(*) FROM dir.Cuenta WITH (UPDLOCK, HOLDLOCK) WHERE folio LIKE @prefijo + N'%') + 1;

    INSERT INTO dir.Cuenta (folio, codigoCuenta, nombreComercial, razonSocial, rfc, idGrupoCuenta, idCiudad, idSucursal)
    VALUES (@prefijo + CAST(@n AS NVARCHAR(10)), @codigoCuenta, @nombreComercial, @razonSocial, @rfc, @idGrupoCuenta, @idCiudad, @idSucursal);
    SET @idCuenta = SCOPE_IDENTITY();
    COMMIT;

    IF @devolverResultado = 1 SELECT @idCuenta AS idCuenta;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_InsertarPersona
    @tipoPersona       NVARCHAR(20),
    @nombreCompleto    NVARCHAR(150),
    @correo            NVARCHAR(200) = NULL,
    @telefono          NVARCHAR(40)  = NULL,
    @extension         NVARCHAR(20)  = NULL,
    @puesto            NVARCHAR(120) = NULL,
    @idDepartamento    INT = NULL,
    @idSucursal        INT = NULL,
    @idPersonaModifico INT = NULL,
    @devolverResultado BIT = 1,
    @idPersona         INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @nombreCompleto = LTRIM(RTRIM(@nombreCompleto));
    SET @correo = NULLIF(LTRIM(RTRIM(@correo)), N'');
    IF @tipoPersona NOT IN (N'Contacto', N'Externo') THROW 50005, N'Solo se pueden crear personas de tipo Contacto o Externo; los usuarios de AD se sincronizan.', 1;
    IF @nombreCompleto IS NULL OR @nombreCompleto = N'' THROW 50001, N'El nombre es obligatorio.', 1;
    IF @tipoPersona = N'Externo' AND @correo IS NULL THROW 50006, N'Un usuario externo necesita correo.', 1;
    IF @correo IS NOT NULL AND EXISTS (SELECT 1 FROM dir.Persona WHERE correo = @correo AND activo = 1)
        THROW 50007, N'Ya existe una persona activa con ese correo.', 1;

    INSERT INTO dir.Persona (tipoPersona, nombreCompleto, correo, telefono, extension, puesto, idDepartamento, idSucursal, idPersonaModifico)
    VALUES (@tipoPersona, @nombreCompleto, @correo, @telefono, @extension, @puesto, @idDepartamento, @idSucursal, @idPersonaModifico);
    SET @idPersona = CAST(SCOPE_IDENTITY() AS INT);

    IF @devolverResultado = 1 SELECT @idPersona AS idPersona;
END
GO

/* =====================================================================
   seg.sp_FijarPasswordCredencial : un administrador fija la contrasena (hash) de un externo.
   ===================================================================== */
CREATE OR ALTER PROCEDURE seg.sp_FijarPasswordCredencial
    @idPersona         INT,
    @passwordHash      NVARCHAR(256),
    @idPersonaModifico INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @passwordHash IS NULL OR @passwordHash = N'' THROW 50027, N'El hash de la contrasena es obligatorio.', 1;

    DECLARE @tipo NVARCHAR(20), @activa BIT, @correo NVARCHAR(200);
    SELECT @tipo = tipoPersona, @activa = activo, @correo = correo FROM dir.Persona WHERE idPersona = @idPersona;

    IF @tipo IS NULL THROW 50003, N'La persona no existe.', 1;
    IF @activa = 0   THROW 50008, N'La persona esta inactiva.', 1;
    IF @tipo = N'AD' THROW 50023, N'Un usuario de AD no puede tener credencial externa.', 1;
    IF @correo IS NULL THROW 50006, N'Un usuario externo necesita correo.', 1;

    BEGIN TRAN;

    IF EXISTS (SELECT 1 FROM seg.CredencialExterna WITH (UPDLOCK, HOLDLOCK) WHERE idPersona = @idPersona)
        UPDATE seg.CredencialExterna
        SET passwordHash = @passwordHash, estado = N'Activo', tokenHash = NULL, fechaExpiraToken = NULL,
            debeCambiarPassword = 0, fechaCambioPassword = GETDATE(), intentosFallidos = 0, bloqueadoHasta = NULL,
            fechaModificacion = GETDATE()
        WHERE idPersona = @idPersona;
    ELSE
        INSERT INTO seg.CredencialExterna (idPersona, passwordHash, estado, fechaCambioPassword)
        VALUES (@idPersona, @passwordHash, N'Activo', GETDATE());

    IF @tipo = N'Contacto'
        UPDATE dir.Persona SET tipoPersona = N'Externo', fechaModificacion = GETDATE(), idPersonaModifico = @idPersonaModifico
        WHERE idPersona = @idPersona;

    COMMIT;
    SELECT @idPersona AS idPersona;
END
GO

PRINT '08_ApoyoParaPortales.sql terminado.';
GO
