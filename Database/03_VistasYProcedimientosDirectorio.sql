/* =====================================================================
   03_VistasYProcedimientosDirectorio.sql
   BD global: EP360_Logistics   (servidor 60, maestro)
   REQUIERE haber corrido antes: 01_CrearBaseYEsquemas.sql y 02_TablasNucleoDirectorio.sql

   Contrato de lectura y escritura del directorio:

   LECTURA (los portales leen la copia local de su servidor):
     Vistas dir.vw_*  -> solo filas ACTIVAS.
        vw_Departamento, vw_Sucursal, vw_Ciudad, vw_GrupoCuenta, vw_Cuenta,
        vw_Persona, vw_PersonaCuenta

   ADMINISTRACION (la app de administracion; solo en el maestro 60):
     Catalogos     : sp_ObtenerDepartamentos / Sucursales / Ciudades, sp_ObtenerOCrearDepartamento / Sucursal
     Grupos        : sp_ObtenerGruposCuenta, sp_ObtenerGrupoCuentaPorId, sp_InsertarGrupoCuenta,
                     sp_ActualizarGrupoCuenta, sp_DesactivarGrupoCuenta, sp_ReactivarGrupoCuenta
     Cuentas       : sp_ObtenerCuentas, sp_ObtenerCuentaPorId, sp_InsertarCuenta, sp_ActualizarCuenta,
                     sp_DesactivarCuenta, sp_ReactivarCuenta, sp_AsignarGrupoCuenta
     Personas      : sp_ObtenerPersonas, sp_ObtenerPersonaPorId, sp_InsertarPersona, sp_ActualizarPersona,
                     sp_DesactivarPersona, sp_ReactivarPersona
     Persona-cuenta: sp_ObtenerCuentasDePersona, sp_ObtenerPersonasDeCuenta,
                     sp_VincularPersonaCuenta, sp_DesvincularPersonaCuenta
     Sincronizacion: sync.sp_SincronizarUsuarioAD

   Reglas: solo stored procedures, borrado logico, folios CGRP-yyMMdd-n / CCLI-yyMMdd-n,
   errores con THROW y mensaje en espanol (50001..50099).

   Es IDEMPOTENTE (CREATE OR ALTER). No inserta datos ni toca otras bases.
   Quien lo corre: el dueno del proyecto, en SSMS, conectado al 60.
   ===================================================================== */

USE EP360_Logistics;
GO

/* =====================================================================
   VISTAS (solo activos)
   ===================================================================== */
CREATE OR ALTER VIEW dir.vw_Departamento AS
SELECT idDepartamento, nombre FROM dir.Departamento WHERE activo = 1;
GO

CREATE OR ALTER VIEW dir.vw_Sucursal AS
SELECT idSucursal, nombre FROM dir.Sucursal WHERE activo = 1;
GO

CREATE OR ALTER VIEW dir.vw_Ciudad AS
SELECT idCiudad, nombre, pais FROM dir.Ciudad WHERE activo = 1;
GO

CREATE OR ALTER VIEW dir.vw_GrupoCuenta AS
SELECT g.idGrupoCuenta, g.folio, g.nombre,
       (SELECT COUNT(*) FROM dir.Cuenta c WHERE c.idGrupoCuenta = g.idGrupoCuenta AND c.activo = 1) AS totalCuentas
FROM dir.GrupoCuenta g
WHERE g.activo = 1;
GO

CREATE OR ALTER VIEW dir.vw_Cuenta AS
SELECT c.idCuenta, c.folio, c.codigoCuenta, c.nombreComercial, c.razonSocial, c.rfc,
       c.idGrupoCuenta, g.nombre AS grupoCuenta,
       c.idCiudad, ci.nombre AS ciudad, ci.pais,
       c.idSucursal, s.nombre AS sucursal
FROM dir.Cuenta c
LEFT JOIN dir.GrupoCuenta g ON g.idGrupoCuenta = c.idGrupoCuenta
LEFT JOIN dir.Ciudad ci     ON ci.idCiudad     = c.idCiudad
LEFT JOIN dir.Sucursal s    ON s.idSucursal    = c.idSucursal
WHERE c.activo = 1;
GO

CREATE OR ALTER VIEW dir.vw_Persona AS
SELECT p.idPersona, p.tipoPersona, p.nombreCompleto, p.correo, p.telefono, p.extension, p.puesto,
       p.idDepartamento, d.nombre AS departamento,
       p.idSucursal, s.nombre AS sucursal,
       u.objectSid, u.samAccountName, u.userPrincipalName, u.habilitadoAD
FROM dir.Persona p
LEFT JOIN dir.UsuarioAD u    ON u.idPersona      = p.idPersona
LEFT JOIN dir.Departamento d ON d.idDepartamento = p.idDepartamento
LEFT JOIN dir.Sucursal s     ON s.idSucursal     = p.idSucursal
WHERE p.activo = 1;
GO

CREATE OR ALTER VIEW dir.vw_PersonaCuenta AS
SELECT pc.idPersonaCuenta, pc.idPersona, p.nombreCompleto, p.correo, p.tipoPersona,
       pc.idCuenta, c.nombreComercial AS cuenta, c.idGrupoCuenta,
       pc.categoria, pc.puestoEnCuenta
FROM dir.PersonaCuenta pc
JOIN dir.Persona p ON p.idPersona = pc.idPersona AND p.activo = 1
JOIN dir.Cuenta  c ON c.idCuenta  = pc.idCuenta  AND c.activo = 1
WHERE pc.activo = 1;
GO

/* =====================================================================
   CATALOGOS
   ===================================================================== */
CREATE OR ALTER PROCEDURE dir.sp_ObtenerDepartamentos @incluirInactivos BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT idDepartamento, nombre, activo FROM dir.Departamento
    WHERE (@incluirInactivos = 1 OR activo = 1) ORDER BY nombre;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ObtenerSucursales @incluirInactivos BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT idSucursal, nombre, activo FROM dir.Sucursal
    WHERE (@incluirInactivos = 1 OR activo = 1) ORDER BY nombre;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ObtenerCiudades @incluirInactivos BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT idCiudad, nombre, pais, activo FROM dir.Ciudad
    WHERE (@incluirInactivos = 1 OR activo = 1) ORDER BY pais, nombre;
END
GO

/* Devuelve el id del departamento activo con ese nombre; si no existe lo crea
   (la sincronizacion con AD crea departamentos nuevos igual que EP360). */
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
        INSERT INTO dir.Departamento (nombre) VALUES (@nombre);
        SET @id = SCOPE_IDENTITY();
    END
    COMMIT;
    SELECT @id AS idDepartamento;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ObtenerOCrearSucursal @nombre NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @nombre = LTRIM(RTRIM(@nombre));
    IF @nombre IS NULL OR @nombre = N'' THROW 50001, N'El nombre de la sucursal es obligatorio.', 1;

    DECLARE @id INT;
    BEGIN TRAN;
    SELECT @id = idSucursal FROM dir.Sucursal WITH (UPDLOCK, HOLDLOCK) WHERE nombre = @nombre AND activo = 1;
    IF @id IS NULL
    BEGIN
        INSERT INTO dir.Sucursal (nombre) VALUES (@nombre);
        SET @id = SCOPE_IDENTITY();
    END
    COMMIT;
    SELECT @id AS idSucursal;
END
GO

/* =====================================================================
   GRUPOS DE CUENTAS
   ===================================================================== */
CREATE OR ALTER PROCEDURE dir.sp_ObtenerGruposCuenta @incluirInactivos BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT g.idGrupoCuenta, g.folio, g.nombre, g.activo,
           (SELECT COUNT(*) FROM dir.Cuenta c WHERE c.idGrupoCuenta = g.idGrupoCuenta AND c.activo = 1) AS totalCuentas
    FROM dir.GrupoCuenta g
    WHERE (@incluirInactivos = 1 OR g.activo = 1)
    ORDER BY g.nombre;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ObtenerGrupoCuentaPorId @idGrupoCuenta INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT g.idGrupoCuenta, g.folio, g.nombre, g.activo, g.fechaCreacion, g.fechaModificacion,
           (SELECT COUNT(*) FROM dir.Cuenta c WHERE c.idGrupoCuenta = g.idGrupoCuenta AND c.activo = 1) AS totalCuentas
    FROM dir.GrupoCuenta g
    WHERE g.idGrupoCuenta = @idGrupoCuenta;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_InsertarGrupoCuenta @nombre NVARCHAR(180)
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
    DECLARE @id INT = SCOPE_IDENTITY();
    COMMIT;
    SELECT @id AS idGrupoCuenta;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ActualizarGrupoCuenta @idGrupoCuenta INT, @nombre NVARCHAR(180)
AS
BEGIN
    SET NOCOUNT ON;
    SET @nombre = LTRIM(RTRIM(@nombre));
    IF @nombre IS NULL OR @nombre = N'' THROW 50001, N'El nombre del grupo es obligatorio.', 1;
    IF NOT EXISTS (SELECT 1 FROM dir.GrupoCuenta WHERE idGrupoCuenta = @idGrupoCuenta) THROW 50003, N'El grupo no existe.', 1;
    IF EXISTS (SELECT 1 FROM dir.GrupoCuenta WHERE nombre = @nombre AND activo = 1 AND idGrupoCuenta <> @idGrupoCuenta)
        THROW 50002, N'Ya existe un grupo activo con ese nombre.', 1;

    UPDATE dir.GrupoCuenta SET nombre = @nombre, fechaModificacion = GETDATE() WHERE idGrupoCuenta = @idGrupoCuenta;
END
GO

/* Desactivar un grupo deja sus cuentas sin grupo (igual que Balance) y luego lo da de baja. */
CREATE OR ALTER PROCEDURE dir.sp_DesactivarGrupoCuenta @idGrupoCuenta INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dir.GrupoCuenta WHERE idGrupoCuenta = @idGrupoCuenta) THROW 50003, N'El grupo no existe.', 1;

    BEGIN TRAN;
    UPDATE dir.Cuenta SET idGrupoCuenta = NULL, fechaModificacion = GETDATE() WHERE idGrupoCuenta = @idGrupoCuenta;
    UPDATE dir.GrupoCuenta SET activo = 0, fechaModificacion = GETDATE() WHERE idGrupoCuenta = @idGrupoCuenta;
    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ReactivarGrupoCuenta @idGrupoCuenta INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @nombre NVARCHAR(180) = (SELECT nombre FROM dir.GrupoCuenta WHERE idGrupoCuenta = @idGrupoCuenta);
    IF @nombre IS NULL THROW 50003, N'El grupo no existe.', 1;
    IF EXISTS (SELECT 1 FROM dir.GrupoCuenta WHERE nombre = @nombre AND activo = 1 AND idGrupoCuenta <> @idGrupoCuenta)
        THROW 50002, N'Ya existe un grupo activo con ese nombre.', 1;

    UPDATE dir.GrupoCuenta SET activo = 1, fechaModificacion = GETDATE() WHERE idGrupoCuenta = @idGrupoCuenta;
END
GO

/* =====================================================================
   CUENTAS
   ===================================================================== */
CREATE OR ALTER PROCEDURE dir.sp_ObtenerCuentas
    @incluirInactivos BIT = 0,
    @idGrupoCuenta    INT = NULL,
    @buscar           NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT c.idCuenta, c.folio, c.codigoCuenta, c.nombreComercial, c.razonSocial, c.rfc,
           c.idGrupoCuenta, g.nombre AS grupoCuenta,
           c.idCiudad, ci.nombre AS ciudad, ci.pais,
           c.idSucursal, s.nombre AS sucursal, c.activo
    FROM dir.Cuenta c
    LEFT JOIN dir.GrupoCuenta g ON g.idGrupoCuenta = c.idGrupoCuenta
    LEFT JOIN dir.Ciudad ci     ON ci.idCiudad     = c.idCiudad
    LEFT JOIN dir.Sucursal s    ON s.idSucursal    = c.idSucursal
    WHERE (@incluirInactivos = 1 OR c.activo = 1)
      AND (@idGrupoCuenta IS NULL OR c.idGrupoCuenta = @idGrupoCuenta)
      AND (@buscar IS NULL OR c.nombreComercial LIKE N'%' + @buscar + N'%' OR c.codigoCuenta LIKE N'%' + @buscar + N'%')
    ORDER BY c.nombreComercial;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ObtenerCuentaPorId @idCuenta INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT c.idCuenta, c.folio, c.codigoCuenta, c.nombreComercial, c.razonSocial, c.rfc,
           c.idGrupoCuenta, g.nombre AS grupoCuenta,
           c.idCiudad, ci.nombre AS ciudad, ci.pais,
           c.idSucursal, s.nombre AS sucursal,
           c.activo, c.fechaCreacion, c.fechaModificacion
    FROM dir.Cuenta c
    LEFT JOIN dir.GrupoCuenta g ON g.idGrupoCuenta = c.idGrupoCuenta
    LEFT JOIN dir.Ciudad ci     ON ci.idCiudad     = c.idCiudad
    LEFT JOIN dir.Sucursal s    ON s.idSucursal    = c.idSucursal
    WHERE c.idCuenta = @idCuenta;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_InsertarCuenta
    @nombreComercial NVARCHAR(180),
    @codigoCuenta    NVARCHAR(60)  = NULL,
    @razonSocial     NVARCHAR(180) = NULL,
    @rfc             NVARCHAR(20)  = NULL,
    @idGrupoCuenta   INT = NULL,
    @idCiudad        INT = NULL,
    @idSucursal      INT = NULL
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
    DECLARE @id INT = SCOPE_IDENTITY();
    COMMIT;
    SELECT @id AS idCuenta;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ActualizarCuenta
    @idCuenta        INT,
    @nombreComercial NVARCHAR(180),
    @codigoCuenta    NVARCHAR(60)  = NULL,
    @razonSocial     NVARCHAR(180) = NULL,
    @rfc             NVARCHAR(20)  = NULL,
    @idGrupoCuenta   INT = NULL,
    @idCiudad        INT = NULL,
    @idSucursal      INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @nombreComercial = LTRIM(RTRIM(@nombreComercial));
    IF @nombreComercial IS NULL OR @nombreComercial = N'' THROW 50001, N'El nombre de la cuenta es obligatorio.', 1;
    IF NOT EXISTS (SELECT 1 FROM dir.Cuenta WHERE idCuenta = @idCuenta) THROW 50003, N'La cuenta no existe.', 1;
    IF @idGrupoCuenta IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dir.GrupoCuenta WHERE idGrupoCuenta = @idGrupoCuenta AND activo = 1)
        THROW 50004, N'El grupo indicado no existe o esta inactivo.', 1;

    UPDATE dir.Cuenta
    SET nombreComercial = @nombreComercial, codigoCuenta = @codigoCuenta, razonSocial = @razonSocial, rfc = @rfc,
        idGrupoCuenta = @idGrupoCuenta, idCiudad = @idCiudad, idSucursal = @idSucursal,
        fechaModificacion = GETDATE()
    WHERE idCuenta = @idCuenta;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_AsignarGrupoCuenta @idCuenta INT, @idGrupoCuenta INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dir.Cuenta WHERE idCuenta = @idCuenta) THROW 50003, N'La cuenta no existe.', 1;
    IF @idGrupoCuenta IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dir.GrupoCuenta WHERE idGrupoCuenta = @idGrupoCuenta AND activo = 1)
        THROW 50004, N'El grupo indicado no existe o esta inactivo.', 1;

    UPDATE dir.Cuenta SET idGrupoCuenta = @idGrupoCuenta, fechaModificacion = GETDATE() WHERE idCuenta = @idCuenta;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_DesactivarCuenta @idCuenta INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dir.Cuenta WHERE idCuenta = @idCuenta) THROW 50003, N'La cuenta no existe.', 1;

    BEGIN TRAN;
    UPDATE dir.PersonaCuenta SET activo = 0, fechaModificacion = GETDATE() WHERE idCuenta = @idCuenta AND activo = 1;
    UPDATE dir.Cuenta SET activo = 0, fechaModificacion = GETDATE() WHERE idCuenta = @idCuenta;
    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ReactivarCuenta @idCuenta INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dir.Cuenta WHERE idCuenta = @idCuenta) THROW 50003, N'La cuenta no existe.', 1;
    -- Si su grupo ya esta inactivo, la cuenta vuelve sin grupo.
    UPDATE c SET c.activo = 1, c.fechaModificacion = GETDATE(),
                 c.idGrupoCuenta = CASE WHEN g.activo = 1 THEN c.idGrupoCuenta ELSE NULL END
    FROM dir.Cuenta c
    LEFT JOIN dir.GrupoCuenta g ON g.idGrupoCuenta = c.idGrupoCuenta
    WHERE c.idCuenta = @idCuenta;
END
GO

/* =====================================================================
   PERSONAS
   ===================================================================== */
CREATE OR ALTER PROCEDURE dir.sp_ObtenerPersonas
    @tipoPersona      NVARCHAR(20)  = NULL,
    @incluirInactivos BIT = 0,
    @buscar           NVARCHAR(100) = NULL,
    @maximo           INT = 500
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@maximo)
           p.idPersona, p.tipoPersona, p.nombreCompleto, p.correo, p.telefono, p.extension, p.puesto,
           p.idDepartamento, d.nombre AS departamento, p.idSucursal, s.nombre AS sucursal,
           u.samAccountName, u.habilitadoAD, p.activo
    FROM dir.Persona p
    LEFT JOIN dir.UsuarioAD u    ON u.idPersona      = p.idPersona
    LEFT JOIN dir.Departamento d ON d.idDepartamento = p.idDepartamento
    LEFT JOIN dir.Sucursal s     ON s.idSucursal     = p.idSucursal
    WHERE (@incluirInactivos = 1 OR p.activo = 1)
      AND (@tipoPersona IS NULL OR p.tipoPersona = @tipoPersona)
      AND (@buscar IS NULL
           OR p.nombreCompleto LIKE N'%' + @buscar + N'%'
           OR p.correo LIKE N'%' + @buscar + N'%'
           OR u.samAccountName LIKE N'%' + @buscar + N'%')
    ORDER BY p.nombreCompleto;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ObtenerPersonaPorId @idPersona INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.idPersona, p.tipoPersona, p.nombreCompleto, p.correo, p.telefono, p.extension, p.puesto,
           p.idDepartamento, d.nombre AS departamento, p.idSucursal, s.nombre AS sucursal,
           u.objectSid, u.samAccountName, u.userPrincipalName, u.correoAD, u.habilitadoAD, u.fechaUltimaSincronizacion,
           p.activo, p.fechaCreacion, p.fechaModificacion, p.idPersonaModifico
    FROM dir.Persona p
    LEFT JOIN dir.UsuarioAD u    ON u.idPersona      = p.idPersona
    LEFT JOIN dir.Departamento d ON d.idDepartamento = p.idDepartamento
    LEFT JOIN dir.Sucursal s     ON s.idSucursal     = p.idSucursal
    WHERE p.idPersona = @idPersona;
END
GO

/* Solo da de alta Contacto o Externo. Los usuarios de AD se crean unicamente por sync.sp_SincronizarUsuarioAD.
   Un Externo todavia no tiene credencial: se la da seg.sp_InvitarUsuarioExterno (script posterior). */
CREATE OR ALTER PROCEDURE dir.sp_InsertarPersona
    @tipoPersona       NVARCHAR(20),
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
    IF @tipoPersona NOT IN (N'Contacto', N'Externo') THROW 50005, N'Solo se pueden crear personas de tipo Contacto o Externo; los usuarios de AD se sincronizan.', 1;
    IF @nombreCompleto IS NULL OR @nombreCompleto = N'' THROW 50001, N'El nombre es obligatorio.', 1;
    IF @tipoPersona = N'Externo' AND @correo IS NULL THROW 50006, N'Un usuario externo necesita correo.', 1;
    IF @correo IS NOT NULL AND EXISTS (SELECT 1 FROM dir.Persona WHERE correo = @correo AND activo = 1)
        THROW 50007, N'Ya existe una persona activa con ese correo.', 1;

    INSERT INTO dir.Persona (tipoPersona, nombreCompleto, correo, telefono, extension, puesto, idDepartamento, idSucursal, idPersonaModifico)
    VALUES (@tipoPersona, @nombreCompleto, @correo, @telefono, @extension, @puesto, @idDepartamento, @idSucursal, @idPersonaModifico);

    SELECT CAST(SCOPE_IDENTITY() AS INT) AS idPersona;
END
GO

/* En un usuario de AD solo se editan telefono, extension y puesto: nombre, correo, departamento
   y sucursal los administra la sincronizacion con AD. */
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
        SET telefono = @telefono, extension = @extension, puesto = @puesto,
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

CREATE OR ALTER PROCEDURE dir.sp_DesactivarPersona @idPersona INT, @idPersonaModifico INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dir.Persona WHERE idPersona = @idPersona) THROW 50003, N'La persona no existe.', 1;

    BEGIN TRAN;
    UPDATE dir.PersonaCuenta SET activo = 0, fechaModificacion = GETDATE() WHERE idPersona = @idPersona AND activo = 1;
    UPDATE dir.PersonaMedioContacto SET activo = 0, fechaModificacion = GETDATE() WHERE idPersona = @idPersona AND activo = 1;
    UPDATE dir.Persona SET activo = 0, fechaModificacion = GETDATE(), idPersonaModifico = @idPersonaModifico WHERE idPersona = @idPersona;
    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ReactivarPersona @idPersona INT, @idPersonaModifico INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @correo NVARCHAR(200) = (SELECT correo FROM dir.Persona WHERE idPersona = @idPersona);
    IF NOT EXISTS (SELECT 1 FROM dir.Persona WHERE idPersona = @idPersona) THROW 50003, N'La persona no existe.', 1;
    IF @correo IS NOT NULL AND EXISTS (SELECT 1 FROM dir.Persona WHERE correo = @correo AND activo = 1 AND idPersona <> @idPersona)
        THROW 50007, N'Ya existe otra persona activa con ese correo.', 1;

    UPDATE dir.Persona SET activo = 1, fechaModificacion = GETDATE(), idPersonaModifico = @idPersonaModifico WHERE idPersona = @idPersona;
END
GO

/* =====================================================================
   PERSONA <-> CUENTA
   ===================================================================== */
CREATE OR ALTER PROCEDURE dir.sp_ObtenerCuentasDePersona @idPersona INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT pc.idPersonaCuenta, pc.idCuenta, c.nombreComercial AS cuenta, c.idGrupoCuenta, g.nombre AS grupoCuenta,
           pc.categoria, pc.puestoEnCuenta
    FROM dir.PersonaCuenta pc
    JOIN dir.Cuenta c ON c.idCuenta = pc.idCuenta AND c.activo = 1
    LEFT JOIN dir.GrupoCuenta g ON g.idGrupoCuenta = c.idGrupoCuenta
    WHERE pc.idPersona = @idPersona AND pc.activo = 1
    ORDER BY c.nombreComercial;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ObtenerPersonasDeCuenta @idCuenta INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT pc.idPersonaCuenta, p.idPersona, p.tipoPersona, p.nombreCompleto, p.correo, p.telefono, p.extension,
           pc.categoria, pc.puestoEnCuenta
    FROM dir.PersonaCuenta pc
    JOIN dir.Persona p ON p.idPersona = pc.idPersona AND p.activo = 1
    WHERE pc.idCuenta = @idCuenta AND pc.activo = 1
    ORDER BY p.nombreCompleto;
END
GO

/* Vincula (o reactiva y actualiza) a la persona con la cuenta. */
CREATE OR ALTER PROCEDURE dir.sp_VincularPersonaCuenta
    @idPersona      INT,
    @idCuenta       INT,
    @categoria      NVARCHAR(30)  = NULL,
    @puestoEnCuenta NVARCHAR(120) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dir.Persona WHERE idPersona = @idPersona AND activo = 1) THROW 50008, N'La persona no existe o esta inactiva.', 1;
    IF NOT EXISTS (SELECT 1 FROM dir.Cuenta  WHERE idCuenta  = @idCuenta  AND activo = 1) THROW 50004, N'La cuenta no existe o esta inactiva.', 1;

    DECLARE @id INT;
    BEGIN TRAN;
    SELECT TOP (1) @id = idPersonaCuenta FROM dir.PersonaCuenta WITH (UPDLOCK, HOLDLOCK)
    WHERE idPersona = @idPersona AND idCuenta = @idCuenta ORDER BY activo DESC, idPersonaCuenta DESC;

    IF @id IS NULL
    BEGIN
        INSERT INTO dir.PersonaCuenta (idPersona, idCuenta, categoria, puestoEnCuenta)
        VALUES (@idPersona, @idCuenta, @categoria, @puestoEnCuenta);
        SET @id = SCOPE_IDENTITY();
    END
    ELSE
        UPDATE dir.PersonaCuenta
        SET categoria = @categoria, puestoEnCuenta = @puestoEnCuenta, activo = 1, fechaModificacion = GETDATE()
        WHERE idPersonaCuenta = @id;
    COMMIT;
    SELECT @id AS idPersonaCuenta;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_DesvincularPersonaCuenta @idPersona INT, @idCuenta INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dir.PersonaCuenta SET activo = 0, fechaModificacion = GETDATE()
    WHERE idPersona = @idPersona AND idCuenta = @idCuenta AND activo = 1;
END
GO

/* =====================================================================
   SINCRONIZACION CON AD
   Upsert de un usuario de AD. La identidad es el objectSid.
   Crea el departamento y la sucursal en el catalogo si no existen.
   Devuelve idPersona y la accion realizada (Alta / Actualizacion).
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

    DECLARE @idDepartamento INT, @idSucursal INT, @idPersona INT, @accion NVARCHAR(20);
    DECLARE @t TABLE (id INT);

    IF @departamento IS NOT NULL BEGIN DELETE @t; INSERT @t EXEC dir.sp_ObtenerOCrearDepartamento @departamento; SELECT @idDepartamento = id FROM @t; END
    IF @sucursal     IS NOT NULL BEGIN DELETE @t; INSERT @t EXEC dir.sp_ObtenerOCrearSucursal     @sucursal;     SELECT @idSucursal     = id FROM @t; END

    BEGIN TRAN;

    SELECT @idPersona = idPersona FROM dir.UsuarioAD WITH (UPDLOCK, HOLDLOCK) WHERE objectSid = @objectSid;

    IF @idPersona IS NULL
    BEGIN
        -- Otro SID con el mismo samAccountName: cuenta recreada o conflicto. No se adivina.
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
        IF @correoAD IS NOT NULL AND EXISTS (SELECT 1 FROM dir.Persona WHERE correo = @correoAD AND activo = 1 AND idPersona <> @idPersona)
        BEGIN
            ROLLBACK;
            THROW 50007, N'El correo de AD ya lo usa otra persona activa. Revisar a mano.', 1;
        END

        UPDATE dir.Persona
        SET nombreCompleto = @nombreCompleto, correo = @correoAD, idDepartamento = @idDepartamento, idSucursal = @idSucursal,
            -- Un usuario deshabilitado en AD se da de baja logica; si vuelve a habilitarse, se reactiva.
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

PRINT '03_VistasYProcedimientosDirectorio.sql terminado.';
SELECT SCHEMA_NAME(schema_id) AS esquema, name, type_desc FROM sys.objects
WHERE SCHEMA_NAME(schema_id) IN (N'dir', N'sync') AND type IN ('V', 'P') ORDER BY type_desc, esquema, name;
GO
