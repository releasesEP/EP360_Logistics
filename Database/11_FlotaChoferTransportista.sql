/* =====================================================================
   11_FlotaChoferTransportista.sql
   BD global: EP360_Logistics   (servidor 60, maestro)
   REQUIERE haber corrido antes: 01 a 10.

   El chofer puede trabajar con varios transportistas: crea
   flota.ChoferTransportista (una fila por par, con historial
   fechaInicio / fechaFin / activo, igual que dir.PersonaCuenta), pasa ahi
   el transportista que tenia cada chofer y quita flota.Chofer.idTransportista.
   Ajusta vistas y SPs de choferes/transportistas y agrega
   sp_VincularChoferTransportista / sp_DesvincularChoferTransportista /
   sp_ObtenerTransportistasDeChofer.
   flota.Tractor se queda con UN transportista (el economico es su numero interno).

   Es IDEMPOTENTE. La carga de ligas historicas desde Transporte (50.11) se
   hizo aparte el 2026-10-05 y no se versiona (trae datos personales).
   Quien lo corre: el dueno del proyecto, en SSMS, conectado al 60.
   ===================================================================== */

USE EP360_Logistics;
GO
SET XACT_ABORT ON;
BEGIN TRAN;

IF OBJECT_ID(N'flota.ChoferTransportista', N'U') IS NULL
BEGIN
    CREATE TABLE flota.ChoferTransportista (
        idChoferTransportista INT IDENTITY(1,1) NOT NULL,
        idChofer              INT      NOT NULL,
        idTransportista       INT      NOT NULL,
        fechaInicio           DATE     NULL,           -- primera vez que trabajo con el transportista
        fechaFin              DATE     NULL,           -- se llena al desvincular
        activo                BIT      NOT NULL CONSTRAINT DF_ChoferTransportista_activo DEFAULT (1),
        fechaCreacion         DATETIME NOT NULL CONSTRAINT DF_ChoferTransportista_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion     DATETIME NULL,
        CONSTRAINT PK_ChoferTransportista PRIMARY KEY CLUSTERED (idChoferTransportista),
        CONSTRAINT FK_ChoferTransportista_Chofer        FOREIGN KEY (idChofer)        REFERENCES flota.Chofer (idChofer),
        CONSTRAINT FK_ChoferTransportista_Transportista FOREIGN KEY (idTransportista) REFERENCES flota.Transportista (idTransportista),
        CONSTRAINT CK_ChoferTransportista_fechas CHECK (fechaFin IS NULL OR fechaInicio IS NULL OR fechaFin >= fechaInicio)
    );
    CREATE UNIQUE INDEX UQ_ChoferTransportista ON flota.ChoferTransportista (idChofer, idTransportista);
    CREATE INDEX IX_ChoferTransportista_idTransportista ON flota.ChoferTransportista (idTransportista, activo);
END

IF COL_LENGTH(N'flota.Chofer', N'idTransportista') IS NOT NULL
BEGIN
    -- cada chofer conserva su transportista actual como primera liga
    EXEC (N'INSERT INTO flota.ChoferTransportista (idChofer, idTransportista)
            SELECT h.idChofer, h.idTransportista FROM flota.Chofer h
            WHERE h.idTransportista IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM flota.ChoferTransportista x WHERE x.idChofer = h.idChofer AND x.idTransportista = h.idTransportista);');

    DROP INDEX IX_Chofer_idTransportista ON flota.Chofer;
    ALTER TABLE flota.Chofer DROP CONSTRAINT FK_Chofer_Transportista;
    ALTER TABLE flota.Chofer DROP COLUMN idTransportista;
END
COMMIT;
GO

/* =====================================================================
   VISTAS
   ===================================================================== */
CREATE OR ALTER VIEW flota.vw_Chofer AS
SELECT h.idChofer, h.nombreCompleto, h.licencia, h.telefono,
       (SELECT STRING_AGG(t.nombre, N', ') WITHIN GROUP (ORDER BY t.nombre)
        FROM flota.ChoferTransportista ct JOIN flota.Transportista t ON t.idTransportista = ct.idTransportista AND t.activo = 1
        WHERE ct.idChofer = h.idChofer AND ct.activo = 1) AS transportistas
FROM flota.Chofer h
WHERE h.activo = 1;
GO

CREATE OR ALTER VIEW flota.vw_ChoferTransportista AS
SELECT ct.idChoferTransportista, ct.idChofer, h.nombreCompleto, h.licencia,
       ct.idTransportista, t.nombre AS transportista, ct.fechaInicio
FROM flota.ChoferTransportista ct
JOIN flota.Chofer h        ON h.idChofer        = ct.idChofer        AND h.activo = 1
JOIN flota.Transportista t ON t.idTransportista = ct.idTransportista AND t.activo = 1
WHERE ct.activo = 1;
GO

/* =====================================================================
   CHOFERES (ajustados) + CHOFER <-> TRANSPORTISTA
   ===================================================================== */
CREATE OR ALTER PROCEDURE flota.sp_ObtenerChoferes
    @incluirInactivos BIT = 0,
    @idTransportista  INT = NULL,          -- choferes con liga activa a ese transportista
    @buscar           NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT h.idChofer, h.nombreCompleto, h.licencia, h.telefono,
           (SELECT STRING_AGG(t.nombre, N', ') WITHIN GROUP (ORDER BY t.nombre)
            FROM flota.ChoferTransportista ct JOIN flota.Transportista t ON t.idTransportista = ct.idTransportista AND t.activo = 1
            WHERE ct.idChofer = h.idChofer AND ct.activo = 1) AS transportistas,
           h.activo
    FROM flota.Chofer h
    WHERE (@incluirInactivos = 1 OR h.activo = 1)
      AND (@idTransportista IS NULL OR EXISTS (SELECT 1 FROM flota.ChoferTransportista ct
                                               WHERE ct.idChofer = h.idChofer AND ct.idTransportista = @idTransportista AND ct.activo = 1))
      AND (@buscar IS NULL OR h.nombreCompleto LIKE N'%' + @buscar + N'%' OR h.licencia LIKE N'%' + @buscar + N'%')
    ORDER BY h.nombreCompleto;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_ObtenerChoferPorId @idChofer INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT h.idChofer, h.nombreCompleto, h.licencia, h.telefono,
           h.activo, h.fechaCreacion, h.fechaModificacion
    FROM flota.Chofer h
    WHERE h.idChofer = @idChofer;
END
GO

/* @idTransportista es opcional: si viene, se crea la primera liga del chofer. */
CREATE OR ALTER PROCEDURE flota.sp_InsertarChofer
    @nombreCompleto    NVARCHAR(150),
    @idTransportista   INT = NULL,
    @licencia          NVARCHAR(30) = NULL,
    @telefono          NVARCHAR(40) = NULL,
    @devolverResultado BIT = 1,
    @idChofer          INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @nombreCompleto = UPPER(LTRIM(RTRIM(@nombreCompleto)));
    SET @licencia = NULLIF(UPPER(REPLACE(LTRIM(RTRIM(@licencia)), N' ', N'')), N'');
    IF @nombreCompleto IS NULL OR @nombreCompleto = N'' THROW 50001, N'El nombre del chofer es obligatorio.', 1;
    IF @idTransportista IS NOT NULL AND NOT EXISTS (SELECT 1 FROM flota.Transportista WHERE idTransportista = @idTransportista AND activo = 1)
        THROW 50004, N'El transportista indicado no existe o esta inactivo.', 1;

    BEGIN TRAN;
    IF @licencia IS NOT NULL AND EXISTS (SELECT 1 FROM flota.Chofer WITH (UPDLOCK, HOLDLOCK) WHERE licencia = @licencia AND activo = 1)
    BEGIN
        ROLLBACK;
        THROW 50002, N'Ya existe un chofer activo con esa licencia.', 1;
    END

    INSERT INTO flota.Chofer (nombreCompleto, licencia, telefono) VALUES (@nombreCompleto, @licencia, @telefono);
    SET @idChofer = SCOPE_IDENTITY();
    IF @idTransportista IS NOT NULL
        INSERT INTO flota.ChoferTransportista (idChofer, idTransportista, fechaInicio) VALUES (@idChofer, @idTransportista, CAST(GETDATE() AS DATE));
    COMMIT;

    IF @devolverResultado = 1 SELECT @idChofer AS idChofer;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_ActualizarChofer
    @idChofer       INT,
    @nombreCompleto NVARCHAR(150),
    @licencia       NVARCHAR(30) = NULL,
    @telefono       NVARCHAR(40) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @nombreCompleto = UPPER(LTRIM(RTRIM(@nombreCompleto)));
    SET @licencia = NULLIF(UPPER(REPLACE(LTRIM(RTRIM(@licencia)), N' ', N'')), N'');
    IF @nombreCompleto IS NULL OR @nombreCompleto = N'' THROW 50001, N'El nombre del chofer es obligatorio.', 1;
    IF NOT EXISTS (SELECT 1 FROM flota.Chofer WHERE idChofer = @idChofer) THROW 50003, N'El chofer no existe.', 1;
    IF @licencia IS NOT NULL AND EXISTS (SELECT 1 FROM flota.Chofer WHERE licencia = @licencia AND activo = 1 AND idChofer <> @idChofer)
        THROW 50002, N'Ya existe un chofer activo con esa licencia.', 1;

    UPDATE flota.Chofer
    SET nombreCompleto = @nombreCompleto, licencia = @licencia, telefono = @telefono, fechaModificacion = GETDATE()
    WHERE idChofer = @idChofer;
END
GO

/* Desactivar un chofer cierra sus ligas con transportistas (igual que Cuenta con PersonaCuenta). */
CREATE OR ALTER PROCEDURE flota.sp_DesactivarChofer @idChofer INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM flota.Chofer WHERE idChofer = @idChofer) THROW 50003, N'El chofer no existe.', 1;

    BEGIN TRAN;
    UPDATE flota.ChoferTransportista SET activo = 0, fechaFin = CAST(GETDATE() AS DATE), fechaModificacion = GETDATE()
    WHERE idChofer = @idChofer AND activo = 1;
    UPDATE flota.Chofer SET activo = 0, fechaModificacion = GETDATE() WHERE idChofer = @idChofer;
    COMMIT;
END
GO

/* Reactiva solo al chofer; sus ligas se vuelven a crear con sp_VincularChoferTransportista. */
CREATE OR ALTER PROCEDURE flota.sp_ReactivarChofer @idChofer INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @licencia NVARCHAR(30);
    IF NOT EXISTS (SELECT 1 FROM flota.Chofer WHERE idChofer = @idChofer) THROW 50003, N'El chofer no existe.', 1;
    SELECT @licencia = licencia FROM flota.Chofer WHERE idChofer = @idChofer;
    IF @licencia IS NOT NULL AND EXISTS (SELECT 1 FROM flota.Chofer WHERE licencia = @licencia AND activo = 1 AND idChofer <> @idChofer)
        THROW 50002, N'Ya existe otro chofer activo con esa licencia.', 1;
    UPDATE flota.Chofer SET activo = 1, fechaModificacion = GETDATE() WHERE idChofer = @idChofer;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_ObtenerTransportistasDeChofer
    @idChofer        INT,
    @incluirHistoria BIT = 0               -- 1 = tambien las ligas cerradas
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ct.idChoferTransportista, ct.idTransportista, t.nombre AS transportista,
           ct.fechaInicio, ct.fechaFin, ct.activo
    FROM flota.ChoferTransportista ct
    JOIN flota.Transportista t ON t.idTransportista = ct.idTransportista
    WHERE ct.idChofer = @idChofer AND (@incluirHistoria = 1 OR (ct.activo = 1 AND t.activo = 1))
    ORDER BY ct.activo DESC, t.nombre;
END
GO

/* Vincula (o reactiva) al chofer con el transportista. */
CREATE OR ALTER PROCEDURE flota.sp_VincularChoferTransportista
    @idChofer        INT,
    @idTransportista INT,
    @fechaInicio     DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM flota.Chofer WHERE idChofer = @idChofer AND activo = 1) THROW 50003, N'El chofer no existe o esta inactivo.', 1;
    IF NOT EXISTS (SELECT 1 FROM flota.Transportista WHERE idTransportista = @idTransportista AND activo = 1)
        THROW 50004, N'El transportista indicado no existe o esta inactivo.', 1;

    DECLARE @id INT;
    BEGIN TRAN;
    SELECT @id = idChoferTransportista FROM flota.ChoferTransportista WITH (UPDLOCK, HOLDLOCK)
    WHERE idChofer = @idChofer AND idTransportista = @idTransportista;

    IF @id IS NULL
    BEGIN
        INSERT INTO flota.ChoferTransportista (idChofer, idTransportista, fechaInicio)
        VALUES (@idChofer, @idTransportista, ISNULL(@fechaInicio, CAST(GETDATE() AS DATE)));
        SET @id = SCOPE_IDENTITY();
    END
    ELSE
        UPDATE flota.ChoferTransportista
        SET activo = 1, fechaFin = NULL, fechaInicio = COALESCE(fechaInicio, @fechaInicio, CAST(GETDATE() AS DATE)), fechaModificacion = GETDATE()
        WHERE idChoferTransportista = @id;
    COMMIT;
    SELECT @id AS idChoferTransportista;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_DesvincularChoferTransportista @idChofer INT, @idTransportista INT, @fechaFin DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE flota.ChoferTransportista
    SET activo = 0, fechaFin = ISNULL(@fechaFin, CAST(GETDATE() AS DATE)), fechaModificacion = GETDATE()
    WHERE idChofer = @idChofer AND idTransportista = @idTransportista AND activo = 1;
END
GO

/* =====================================================================
   TRANSPORTISTAS (ajustados a la nueva relacion)
   ===================================================================== */
CREATE OR ALTER PROCEDURE flota.sp_ObtenerTransportistas
    @incluirInactivos BIT = 0,
    @buscar           NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT t.idTransportista, t.folio, t.nombre, t.razonSocial, t.rfc,
           t.idCuenta, c.nombreComercial AS cuenta,
           (SELECT COUNT(*) FROM flota.ChoferTransportista ct JOIN flota.Chofer h ON h.idChofer = ct.idChofer AND h.activo = 1
            WHERE ct.idTransportista = t.idTransportista AND ct.activo = 1) AS choferes,
           (SELECT COUNT(*) FROM flota.Tractor r WHERE r.idTransportista = t.idTransportista AND r.activo = 1) AS tractores,
           (SELECT COUNT(*) FROM flota.Caja k    WHERE k.idTransportista = t.idTransportista AND k.activo = 1) AS cajas,
           t.activo
    FROM flota.Transportista t
    LEFT JOIN dir.Cuenta c ON c.idCuenta = t.idCuenta
    WHERE (@incluirInactivos = 1 OR t.activo = 1)
      AND (@buscar IS NULL OR t.nombre LIKE N'%' + @buscar + N'%' OR t.razonSocial LIKE N'%' + @buscar + N'%')
    ORDER BY t.nombre;
END
GO

/* Desactivar un transportista da de baja sus tractores y cajas propias, cierra sus ligas con
   choferes (los choferes siguen activos: pueden trabajar con otros) y lo quita como
   transportista habitual de las cajas de clientes / EP. */
CREATE OR ALTER PROCEDURE flota.sp_DesactivarTransportista @idTransportista INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM flota.Transportista WHERE idTransportista = @idTransportista) THROW 50003, N'El transportista no existe.', 1;

    BEGIN TRAN;
    UPDATE flota.ChoferTransportista SET activo = 0, fechaFin = CAST(GETDATE() AS DATE), fechaModificacion = GETDATE()
    WHERE idTransportista = @idTransportista AND activo = 1;
    UPDATE flota.Tractor SET activo = 0, fechaModificacion = GETDATE() WHERE idTransportista = @idTransportista AND activo = 1;
    UPDATE flota.Caja    SET activo = 0, fechaModificacion = GETDATE() WHERE idTransportista = @idTransportista AND tipoPropiedad = N'Transportista' AND activo = 1;
    UPDATE flota.Caja    SET idTransportista = NULL, fechaModificacion = GETDATE() WHERE idTransportista = @idTransportista AND tipoPropiedad <> N'Transportista';
    UPDATE flota.Transportista SET activo = 0, fechaModificacion = GETDATE() WHERE idTransportista = @idTransportista;
    COMMIT;
END
GO

/* ---------- Resumen ---------- */
SELECT (SELECT COUNT(*) FROM flota.ChoferTransportista) AS ligas,
       (SELECT COUNT(*) FROM flota.ChoferTransportista WHERE activo = 1) AS ligasActivas,
       (SELECT COUNT(*) FROM (SELECT idChofer FROM flota.ChoferTransportista GROUP BY idChofer HAVING COUNT(*) > 1) x) AS choferesConVarios,
       (SELECT COUNT(*) FROM flota.Chofer h WHERE NOT EXISTS (SELECT 1 FROM flota.ChoferTransportista ct WHERE ct.idChofer = h.idChofer)) AS choferesSinLiga,
       CASE WHEN COL_LENGTH(N'flota.Chofer', N'idTransportista') IS NULL THEN 'quitada' ELSE 'sigue' END AS columnaIdTransportista;
