/* =====================================================================
   11_AccesosAPortales.sql
   BD global: EP360_Logistics   -> correr en el 60 Y en el 11 (cada uno por separado), DESPUES del 10c.
   REQUIERE haber corrido antes: 01 a 10c.

   El ACCESO a cada portal (EP360, Balance, Help Desk, Intranet) se otorga desde la global; cada portal solo decide
   QUE PUEDE HACER (permisos por modulo) quien ya tiene acceso a ese portal.

   Crea:
     dir.Portal.permiteExternos       los usuarios EXTERNOS (clientes) solo pueden entrar a los portales publicos (hoy EP360 y Balance)
     dir.PersonaPortal                 que personas tienen acceso a que portal (borrado logico, una fila activa por persona y portal)
     dir.sp_ObtenerPortales            catalogo de portales activos
     dir.sp_ObtenerAccesosPortales     personas AD y externas activas con los ids de los portales a los que tienen acceso
     dir.sp_ObtenerAccesosDePersona    accesos de una persona, uno por portal
     dir.sp_OtorgarAccesoPortal        da (o reactiva) el acceso de una persona a un portal
     dir.sp_RevocarAccesoPortal        quita el acceso

   Reglas (errores 50051-50056, con mensaje para el usuario):
     * solo una persona ACTIVA de tipo AD o Externo puede tener acceso (un contacto debe volverse usuario externo primero);
     * un usuario externo solo entra a portales con permiteExternos = 1;
     * quien otorga o revoca queda en la bitacora (sync.BitacoraCambio).

   No da acceso a nadie por si solo: los accesos actuales de EP360 se cargan con 11b_MigrarAccesosEP360.sql.
   Es IDEMPOTENTE. Quien lo corre: el dueno del proyecto, en SSMS.
   ===================================================================== */

USE EP360_Logistics;
GO

/* ---- Portales publicos ---- */
IF COL_LENGTH(N'dir.Portal', N'permiteExternos') IS NULL
    ALTER TABLE dir.Portal ADD permiteExternos BIT NOT NULL CONSTRAINT DF_Portal_permiteExternos DEFAULT (0);
GO
UPDATE dir.Portal SET permiteExternos = 1 WHERE clave IN (N'EP360', N'BALANCE') AND permiteExternos = 0;
GO

/* ---- Accesos ---- */
IF OBJECT_ID(N'dir.PersonaPortal', N'U') IS NULL
BEGIN
    CREATE TABLE dir.PersonaPortal
    (
        idPersonaPortal   INT IDENTITY(1,1) NOT NULL,
        idPersona         INT               NOT NULL,
        idPortal          INT               NOT NULL,
        activo            BIT               NOT NULL CONSTRAINT DF_PersonaPortal_activo DEFAULT (1),
        otorgadoPor       NVARCHAR(150)     NULL,
        revocadoPor       NVARCHAR(150)     NULL,
        fechaCreacion     DATETIME          NOT NULL CONSTRAINT DF_PersonaPortal_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion DATETIME          NULL,
        CONSTRAINT PK_PersonaPortal PRIMARY KEY CLUSTERED (idPersonaPortal),
        CONSTRAINT FK_PersonaPortal_Persona FOREIGN KEY (idPersona) REFERENCES dir.Persona (idPersona),
        CONSTRAINT FK_PersonaPortal_Portal  FOREIGN KEY (idPortal)  REFERENCES dir.Portal (idPortal)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_PersonaPortal_activo' AND object_id = OBJECT_ID(N'dir.PersonaPortal'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_PersonaPortal_activo ON dir.PersonaPortal (idPersona, idPortal) WHERE activo = 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PersonaPortal_portal' AND object_id = OBJECT_ID(N'dir.PersonaPortal'))
    CREATE NONCLUSTERED INDEX IX_PersonaPortal_portal ON dir.PersonaPortal (idPortal, activo);
GO

/* ---- Consultas ---- */
CREATE OR ALTER PROCEDURE dir.sp_ObtenerPortales
AS
BEGIN
    SET NOCOUNT ON;
    SELECT idPortal, clave, nombre, permiteExternos
    FROM dir.Portal
    WHERE activo = 1
    ORDER BY idPortal;
END
GO

-- Personas ACTIVAS que pueden tener acceso (AD y externas) con los ids de sus portales ("1,3"). El filtrado fino lo hace la app.
CREATE OR ALTER PROCEDURE dir.sp_ObtenerAccesosPortales
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.idPersona, p.tipoPersona, p.nombreCompleto, p.correo, p.puesto,
           d.nombre AS departamento, u.samAccountName,
           (SELECT STRING_AGG(CAST(pp.idPortal AS NVARCHAR(10)), N',')
            FROM dir.PersonaPortal pp
            JOIN dir.Portal po ON po.idPortal = pp.idPortal AND po.activo = 1
            WHERE pp.idPersona = p.idPersona AND pp.activo = 1) AS idsPortales,
           (SELECT TOP (1) c.nombreComercial
            FROM dir.PersonaCuenta pc
            JOIN dir.Cuenta c ON c.idCuenta = pc.idCuenta
            WHERE pc.idPersona = p.idPersona AND pc.activo = 1
            ORDER BY c.nombreComercial) AS cuenta
    FROM dir.Persona p
    LEFT JOIN dir.UsuarioAD u    ON u.idPersona      = p.idPersona
    LEFT JOIN dir.Departamento d ON d.idDepartamento = p.idDepartamento
    WHERE p.activo = 1 AND p.tipoPersona IN (N'AD', N'Externo')
    ORDER BY p.nombreCompleto;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ObtenerAccesosDePersona @idPersona INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT po.idPortal, po.clave, po.nombre, po.permiteExternos,
           CAST(CASE WHEN pp.idPersonaPortal IS NULL THEN 0 ELSE 1 END AS BIT) AS tieneAcceso,
           pp.fechaCreacion AS fechaOtorgado, pp.otorgadoPor
    FROM dir.Portal po
    LEFT JOIN dir.PersonaPortal pp ON pp.idPortal = po.idPortal AND pp.idPersona = @idPersona AND pp.activo = 1
    WHERE po.activo = 1
    ORDER BY po.idPortal;
END
GO

/* ---- Cambios ---- */
CREATE OR ALTER PROCEDURE dir.sp_OtorgarAccesoPortal
    @idPersona INT,
    @idPortal  INT,
    @usuario   NVARCHAR(150) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @tipo NVARCHAR(20), @activa BIT, @nombre NVARCHAR(200);
    SELECT @tipo = tipoPersona, @activa = activo, @nombre = nombreCompleto FROM dir.Persona WHERE idPersona = @idPersona;
    IF @tipo IS NULL THROW 50051, N'La persona no existe.', 1;
    IF @activa = 0   THROW 50052, N'La persona esta inactiva.', 1;
    IF @tipo NOT IN (N'AD', N'Externo')
        THROW 50053, N'Solo un usuario de AD o un usuario externo puede tener acceso a un portal. Un contacto debe convertirse primero en usuario externo.', 1;

    DECLARE @clave NVARCHAR(60), @permiteExternos BIT;
    SELECT @clave = clave, @permiteExternos = permiteExternos FROM dir.Portal WHERE idPortal = @idPortal AND activo = 1;
    IF @clave IS NULL THROW 50054, N'El portal no existe o esta inactivo.', 1;
    IF @tipo = N'Externo' AND @permiteExternos = 0
        THROW 50055, N'Ese portal es interno: un usuario externo (cliente) no puede tener acceso.', 1;

    DECLARE @id INT;
    BEGIN TRAN;
    SELECT TOP (1) @id = idPersonaPortal FROM dir.PersonaPortal WITH (UPDLOCK, HOLDLOCK)
    WHERE idPersona = @idPersona AND idPortal = @idPortal ORDER BY activo DESC, idPersonaPortal DESC;

    IF @id IS NULL
    BEGIN
        INSERT INTO dir.PersonaPortal (idPersona, idPortal, otorgadoPor) VALUES (@idPersona, @idPortal, LEFT(@usuario, 150));
        SET @id = SCOPE_IDENTITY();
    END
    ELSE
        UPDATE dir.PersonaPortal
           SET activo = 1, otorgadoPor = LEFT(@usuario, 150), revocadoPor = NULL, fechaModificacion = GETDATE()
         WHERE idPersonaPortal = @id AND activo = 0;

    INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle)
    VALUES (N'PersonaPortal', @idPersona, N'OtorgarAcceso', LEFT(N'Portal ' + @clave + N' a ' + @nombre + N' (por ' + ISNULL(@usuario, N'?') + N')', 500));
    COMMIT;

    SELECT @id AS idPersonaPortal;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_RevocarAccesoPortal
    @idPersona INT,
    @idPortal  INT,
    @usuario   NVARCHAR(150) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @clave NVARCHAR(60) = (SELECT clave FROM dir.Portal WHERE idPortal = @idPortal);
    IF @clave IS NULL THROW 50054, N'El portal no existe o esta inactivo.', 1;

    BEGIN TRAN;
    UPDATE dir.PersonaPortal
       SET activo = 0, revocadoPor = LEFT(@usuario, 150), fechaModificacion = GETDATE()
     WHERE idPersona = @idPersona AND idPortal = @idPortal AND activo = 1;

    IF @@ROWCOUNT > 0
        INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle)
        VALUES (N'PersonaPortal', @idPersona, N'RevocarAcceso',
                LEFT(N'Portal ' + @clave + N' a la persona ' + CONVERT(NVARCHAR(20), @idPersona) + N' (por ' + ISNULL(@usuario, N'?') + N')', 500));
    COMMIT;
END
GO

PRINT N'11_AccesosAPortales.sql terminado.';
GO
