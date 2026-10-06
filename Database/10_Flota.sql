/* =====================================================================
   10_Flota.sql
   BD global: EP360_Logistics   (servidor 60, maestro)
   REQUIERE haber corrido antes: 01 a 09.

   Esquema flota: cajas (trailers) de clientes / transportistas / EP,
   transportistas, tractores y choferes, ligados al directorio
   (dir.Cuenta, dir.GrupoCuenta). Las cajas de cliente se ligan a UN grupo
   de cuentas o a UNA cuenta (CK_Caja_propietario).
   Ajusta dir.sp_DesactivarGrupoCuenta y dir.sp_DesactivarCuenta para que
   den de baja las cajas del propietario.

   Codigos de error (mismos que dir.*):
     50001 dato obligatorio   50002 duplicado   50003 no existe
     50004 referencia invalida o inactiva       50005 regla de negocio

   Es IDEMPOTENTE (tablas IF NOT EXISTS, objetos CREATE OR ALTER). No inserta datos.
   El chofer pasa a muchos-a-muchos con transportistas en el 11.
   Quien lo corre: el dueno del proyecto, en SSMS, conectado al 60.
   ===================================================================== */

USE EP360_Logistics;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF SCHEMA_ID(N'flota') IS NULL EXEC (N'CREATE SCHEMA flota AUTHORIZATION dbo;');
GO

/* =====================================================================
   TABLAS
   ===================================================================== */
SET XACT_ABORT ON;
BEGIN TRAN;

/* ---------- Transportista (linea transportista) ---------- */
IF OBJECT_ID(N'flota.Transportista', N'U') IS NULL
BEGIN
    CREATE TABLE flota.Transportista (
        idTransportista   INT IDENTITY(1,1) NOT NULL,
        folio             NVARCHAR(30)  NULL,
        nombre            NVARCHAR(120) NOT NULL,          -- nombre corto como se conoce (WARHORSE, EP CARGO...)
        razonSocial       NVARCHAR(180) NULL,
        rfc               NVARCHAR(20)  NULL,
        idCuenta          INT           NULL,              -- si el transportista tambien es cuenta del directorio
        activo            BIT           NOT NULL CONSTRAINT DF_Transportista_activo DEFAULT (1),
        fechaCreacion     DATETIME      NOT NULL CONSTRAINT DF_Transportista_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion DATETIME      NULL,
        CONSTRAINT PK_Transportista PRIMARY KEY CLUSTERED (idTransportista),
        CONSTRAINT FK_Transportista_Cuenta FOREIGN KEY (idCuenta) REFERENCES dir.Cuenta (idCuenta)
    );
    CREATE UNIQUE INDEX UQ_Transportista_folio  ON flota.Transportista (folio)  WHERE folio IS NOT NULL;
    CREATE UNIQUE INDEX UQ_Transportista_nombre ON flota.Transportista (nombre) WHERE activo = 1;
    CREATE INDEX IX_Transportista_idCuenta ON flota.Transportista (idCuenta) WHERE idCuenta IS NOT NULL;
END

/* ---------- Caja / trailer ----------
   tipoPropiedad:
     Cliente       -> pertenece a UNA cuenta o a UN grupo de cuentas (exactamente uno).
                      idTransportista opcional = transportista que normalmente la mueve.
     Transportista -> pertenece al transportista (idTransportista obligatorio, sin cuenta/grupo).
     EP            -> trailer propio de EP (sin cuenta/grupo; transportista opcional).        */
IF OBJECT_ID(N'flota.Caja', N'U') IS NULL
BEGIN
    CREATE TABLE flota.Caja (
        idCaja            INT IDENTITY(1,1) NOT NULL,
        numeroCaja        NVARCHAR(30)  NOT NULL,          -- normalizado: mayusculas, sin espacios ni guiones
        tipoPropiedad     NVARCHAR(20)  NOT NULL,
        idCuenta          INT           NULL,
        idGrupoCuenta     INT           NULL,
        idTransportista   INT           NULL,
        placa             NVARCHAR(20)  NULL,
        vin               NVARCHAR(20)  NULL,
        anio              SMALLINT      NULL,
        marca             NVARCHAR(60)  NULL,
        pies              TINYINT       NULL,              -- 53, 48...
        activo            BIT           NOT NULL CONSTRAINT DF_Caja_activo DEFAULT (1),
        fechaCreacion     DATETIME      NOT NULL CONSTRAINT DF_Caja_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion DATETIME      NULL,
        CONSTRAINT PK_Caja PRIMARY KEY CLUSTERED (idCaja),
        CONSTRAINT FK_Caja_Cuenta        FOREIGN KEY (idCuenta)        REFERENCES dir.Cuenta (idCuenta),
        CONSTRAINT FK_Caja_GrupoCuenta   FOREIGN KEY (idGrupoCuenta)   REFERENCES dir.GrupoCuenta (idGrupoCuenta),
        CONSTRAINT FK_Caja_Transportista FOREIGN KEY (idTransportista) REFERENCES flota.Transportista (idTransportista),
        CONSTRAINT CK_Caja_tipoPropiedad CHECK (tipoPropiedad IN (N'Cliente', N'Transportista', N'EP')),
        CONSTRAINT CK_Caja_propietario CHECK (
               (tipoPropiedad = N'Cliente'       AND ((idCuenta IS NOT NULL AND idGrupoCuenta IS NULL) OR (idCuenta IS NULL AND idGrupoCuenta IS NOT NULL)))
            OR (tipoPropiedad = N'Transportista' AND idTransportista IS NOT NULL AND idCuenta IS NULL AND idGrupoCuenta IS NULL)
            OR (tipoPropiedad = N'EP'            AND idCuenta IS NULL AND idGrupoCuenta IS NULL)),
        CONSTRAINT CK_Caja_anio CHECK (anio IS NULL OR anio BETWEEN 1950 AND 2100)
    );
    -- Un numero de caja de cliente o de EP es unico; el de un transportista solo es unico dentro de ese transportista
    CREATE UNIQUE INDEX UQ_Caja_numero_activa          ON flota.Caja (numeroCaja)                  WHERE activo = 1 AND tipoPropiedad <> N'Transportista';
    CREATE UNIQUE INDEX UQ_Caja_numero_transportista   ON flota.Caja (numeroCaja, idTransportista) WHERE activo = 1 AND tipoPropiedad = N'Transportista';
    CREATE UNIQUE INDEX UQ_Caja_vin                    ON flota.Caja (vin)                         WHERE vin IS NOT NULL AND activo = 1;
    CREATE INDEX IX_Caja_idCuenta        ON flota.Caja (idCuenta, activo)        WHERE idCuenta IS NOT NULL;
    CREATE INDEX IX_Caja_idGrupoCuenta   ON flota.Caja (idGrupoCuenta, activo)   WHERE idGrupoCuenta IS NOT NULL;
    CREATE INDEX IX_Caja_idTransportista ON flota.Caja (idTransportista, activo) WHERE idTransportista IS NOT NULL;
END

/* ---------- Tractor (economico) ---------- */
IF OBJECT_ID(N'flota.Tractor', N'U') IS NULL
BEGIN
    CREATE TABLE flota.Tractor (
        idTractor         INT IDENTITY(1,1) NOT NULL,
        idTransportista   INT           NOT NULL,
        economico         NVARCHAR(30)  NOT NULL,          -- normalizado: mayusculas, sin espacios ni guiones
        placa             NVARCHAR(20)  NULL,
        vin               NVARCHAR(20)  NULL,
        anio              SMALLINT      NULL,
        marca             NVARCHAR(60)  NULL,
        activo            BIT           NOT NULL CONSTRAINT DF_Tractor_activo DEFAULT (1),
        fechaCreacion     DATETIME      NOT NULL CONSTRAINT DF_Tractor_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion DATETIME      NULL,
        CONSTRAINT PK_Tractor PRIMARY KEY CLUSTERED (idTractor),
        CONSTRAINT FK_Tractor_Transportista FOREIGN KEY (idTransportista) REFERENCES flota.Transportista (idTransportista),
        CONSTRAINT CK_Tractor_anio CHECK (anio IS NULL OR anio BETWEEN 1950 AND 2100)
    );
    CREATE UNIQUE INDEX UQ_Tractor_economico ON flota.Tractor (idTransportista, economico) WHERE activo = 1;
    CREATE UNIQUE INDEX UQ_Tractor_vin       ON flota.Tractor (vin) WHERE vin IS NOT NULL AND activo = 1;
END

/* ---------- Chofer (operador) ---------- */
IF OBJECT_ID(N'flota.Chofer', N'U') IS NULL
BEGIN
    CREATE TABLE flota.Chofer (
        idChofer          INT IDENTITY(1,1) NOT NULL,
        idTransportista   INT           NULL,
        nombreCompleto    NVARCHAR(150) NOT NULL,
        licencia          NVARCHAR(30)  NULL,              -- mayusculas, sin espacios
        telefono          NVARCHAR(40)  NULL,
        activo            BIT           NOT NULL CONSTRAINT DF_Chofer_activo DEFAULT (1),
        fechaCreacion     DATETIME      NOT NULL CONSTRAINT DF_Chofer_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion DATETIME      NULL,
        CONSTRAINT PK_Chofer PRIMARY KEY CLUSTERED (idChofer),
        CONSTRAINT FK_Chofer_Transportista FOREIGN KEY (idTransportista) REFERENCES flota.Transportista (idTransportista)
    );
    CREATE UNIQUE INDEX UQ_Chofer_licencia ON flota.Chofer (licencia) WHERE licencia IS NOT NULL AND activo = 1;
    CREATE INDEX IX_Chofer_idTransportista ON flota.Chofer (idTransportista, activo);
    CREATE INDEX IX_Chofer_nombreCompleto  ON flota.Chofer (nombreCompleto);
END

COMMIT;
GO

/* =====================================================================
   VISTAS (solo activos, igual que dir.vw_*)
   ===================================================================== */
CREATE OR ALTER VIEW flota.vw_Transportista AS
SELECT t.idTransportista, t.folio, t.nombre, t.razonSocial, t.rfc,
       t.idCuenta, c.nombreComercial AS cuenta
FROM flota.Transportista t
LEFT JOIN dir.Cuenta c ON c.idCuenta = t.idCuenta
WHERE t.activo = 1;
GO

CREATE OR ALTER VIEW flota.vw_Caja AS
SELECT k.idCaja, k.numeroCaja, k.tipoPropiedad,
       k.idCuenta, c.nombreComercial AS cuenta,
       k.idGrupoCuenta, g.nombre AS grupoCuenta,
       COALESCE(c.nombreComercial, g.nombre, CASE k.tipoPropiedad WHEN N'EP' THEN N'EP LOGISTICS' ELSE t.nombre END) AS propietario,
       k.idTransportista, t.nombre AS transportista,
       k.placa, k.vin, k.anio, k.marca, k.pies
FROM flota.Caja k
LEFT JOIN dir.Cuenta c            ON c.idCuenta        = k.idCuenta
LEFT JOIN dir.GrupoCuenta g       ON g.idGrupoCuenta   = k.idGrupoCuenta
LEFT JOIN flota.Transportista t   ON t.idTransportista = k.idTransportista
WHERE k.activo = 1;
GO

CREATE OR ALTER VIEW flota.vw_Tractor AS
SELECT r.idTractor, r.economico, r.idTransportista, t.nombre AS transportista,
       r.placa, r.vin, r.anio, r.marca
FROM flota.Tractor r
JOIN flota.Transportista t ON t.idTransportista = r.idTransportista
WHERE r.activo = 1;
GO

CREATE OR ALTER VIEW flota.vw_Chofer AS
SELECT h.idChofer, h.nombreCompleto, h.licencia, h.telefono,
       h.idTransportista, t.nombre AS transportista
FROM flota.Chofer h
LEFT JOIN flota.Transportista t ON t.idTransportista = h.idTransportista
WHERE h.activo = 1;
GO

/* =====================================================================
   TRANSPORTISTAS
   ===================================================================== */
CREATE OR ALTER PROCEDURE flota.sp_ObtenerTransportistas
    @incluirInactivos BIT = 0,
    @buscar           NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT t.idTransportista, t.folio, t.nombre, t.razonSocial, t.rfc,
           t.idCuenta, c.nombreComercial AS cuenta,
           (SELECT COUNT(*) FROM flota.Chofer h  WHERE h.idTransportista = t.idTransportista AND h.activo = 1) AS choferes,
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

CREATE OR ALTER PROCEDURE flota.sp_ObtenerTransportistaPorId @idTransportista INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT t.idTransportista, t.folio, t.nombre, t.razonSocial, t.rfc,
           t.idCuenta, c.nombreComercial AS cuenta,
           t.activo, t.fechaCreacion, t.fechaModificacion
    FROM flota.Transportista t
    LEFT JOIN dir.Cuenta c ON c.idCuenta = t.idCuenta
    WHERE t.idTransportista = @idTransportista;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_InsertarTransportista
    @nombre            NVARCHAR(120),
    @razonSocial       NVARCHAR(180) = NULL,
    @rfc               NVARCHAR(20)  = NULL,
    @idCuenta          INT = NULL,
    @devolverResultado BIT = 1,
    @idTransportista   INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @nombre = UPPER(LTRIM(RTRIM(@nombre)));
    IF @nombre IS NULL OR @nombre = N'' THROW 50001, N'El nombre del transportista es obligatorio.', 1;
    IF @idCuenta IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dir.Cuenta WHERE idCuenta = @idCuenta AND activo = 1)
        THROW 50004, N'La cuenta indicada no existe o esta inactiva.', 1;

    BEGIN TRAN;
    IF EXISTS (SELECT 1 FROM flota.Transportista WITH (UPDLOCK, HOLDLOCK) WHERE nombre = @nombre AND activo = 1)
    BEGIN
        ROLLBACK;
        THROW 50002, N'Ya existe un transportista activo con ese nombre.', 1;
    END

    DECLARE @prefijo NVARCHAR(20) = N'CTRA-' + FORMAT(GETDATE(), 'yyMMdd') + N'-';
    DECLARE @n INT = (SELECT COUNT(*) FROM flota.Transportista WITH (UPDLOCK, HOLDLOCK) WHERE folio LIKE @prefijo + N'%') + 1;

    INSERT INTO flota.Transportista (folio, nombre, razonSocial, rfc, idCuenta)
    VALUES (@prefijo + CAST(@n AS NVARCHAR(10)), @nombre, @razonSocial, @rfc, @idCuenta);
    SET @idTransportista = SCOPE_IDENTITY();
    COMMIT;

    IF @devolverResultado = 1 SELECT @idTransportista AS idTransportista;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_ActualizarTransportista
    @idTransportista INT,
    @nombre          NVARCHAR(120),
    @razonSocial     NVARCHAR(180) = NULL,
    @rfc             NVARCHAR(20)  = NULL,
    @idCuenta        INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @nombre = UPPER(LTRIM(RTRIM(@nombre)));
    IF @nombre IS NULL OR @nombre = N'' THROW 50001, N'El nombre del transportista es obligatorio.', 1;
    IF NOT EXISTS (SELECT 1 FROM flota.Transportista WHERE idTransportista = @idTransportista) THROW 50003, N'El transportista no existe.', 1;
    IF EXISTS (SELECT 1 FROM flota.Transportista WHERE nombre = @nombre AND activo = 1 AND idTransportista <> @idTransportista)
        THROW 50002, N'Ya existe un transportista activo con ese nombre.', 1;
    IF @idCuenta IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dir.Cuenta WHERE idCuenta = @idCuenta AND activo = 1)
        THROW 50004, N'La cuenta indicada no existe o esta inactiva.', 1;

    UPDATE flota.Transportista
    SET nombre = @nombre, razonSocial = @razonSocial, rfc = @rfc, idCuenta = @idCuenta,
        fechaModificacion = GETDATE()
    WHERE idTransportista = @idTransportista;
END
GO

/* Desactivar un transportista da de baja sus choferes, tractores y cajas propias,
   y lo quita como transportista habitual de las cajas de clientes / EP. */
CREATE OR ALTER PROCEDURE flota.sp_DesactivarTransportista @idTransportista INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM flota.Transportista WHERE idTransportista = @idTransportista) THROW 50003, N'El transportista no existe.', 1;

    BEGIN TRAN;
    UPDATE flota.Chofer  SET activo = 0, fechaModificacion = GETDATE() WHERE idTransportista = @idTransportista AND activo = 1;
    UPDATE flota.Tractor SET activo = 0, fechaModificacion = GETDATE() WHERE idTransportista = @idTransportista AND activo = 1;
    UPDATE flota.Caja    SET activo = 0, fechaModificacion = GETDATE() WHERE idTransportista = @idTransportista AND tipoPropiedad = N'Transportista' AND activo = 1;
    UPDATE flota.Caja    SET idTransportista = NULL, fechaModificacion = GETDATE() WHERE idTransportista = @idTransportista AND tipoPropiedad <> N'Transportista';
    UPDATE flota.Transportista SET activo = 0, fechaModificacion = GETDATE() WHERE idTransportista = @idTransportista;
    COMMIT;
END
GO

/* Reactiva solo el transportista; sus choferes/tractores/cajas se reactivan uno por uno. */
CREATE OR ALTER PROCEDURE flota.sp_ReactivarTransportista @idTransportista INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @nombre NVARCHAR(120) = (SELECT nombre FROM flota.Transportista WHERE idTransportista = @idTransportista);
    IF @nombre IS NULL THROW 50003, N'El transportista no existe.', 1;
    IF EXISTS (SELECT 1 FROM flota.Transportista WHERE nombre = @nombre AND activo = 1 AND idTransportista <> @idTransportista)
        THROW 50002, N'Ya existe otro transportista activo con ese nombre.', 1;
    UPDATE flota.Transportista SET activo = 1, fechaModificacion = GETDATE() WHERE idTransportista = @idTransportista;
END
GO

/* =====================================================================
   CAJAS
   ===================================================================== */
CREATE OR ALTER PROCEDURE flota.sp_ObtenerCajas
    @incluirInactivos BIT = 0,
    @tipoPropiedad    NVARCHAR(20) = NULL,
    @idCuenta         INT = NULL,
    @idGrupoCuenta    INT = NULL,          -- trae las cajas del grupo y las de sus cuentas
    @idTransportista  INT = NULL,
    @buscar           NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT k.idCaja, k.numeroCaja, k.tipoPropiedad,
           k.idCuenta, c.nombreComercial AS cuenta,
           k.idGrupoCuenta, g.nombre AS grupoCuenta,
           COALESCE(c.nombreComercial, g.nombre, CASE k.tipoPropiedad WHEN N'EP' THEN N'EP LOGISTICS' ELSE t.nombre END) AS propietario,
           k.idTransportista, t.nombre AS transportista,
           k.placa, k.vin, k.anio, k.marca, k.pies, k.activo
    FROM flota.Caja k
    LEFT JOIN dir.Cuenta c          ON c.idCuenta        = k.idCuenta
    LEFT JOIN dir.GrupoCuenta g     ON g.idGrupoCuenta   = k.idGrupoCuenta
    LEFT JOIN flota.Transportista t ON t.idTransportista = k.idTransportista
    WHERE (@incluirInactivos = 1 OR k.activo = 1)
      AND (@tipoPropiedad   IS NULL OR k.tipoPropiedad   = @tipoPropiedad)
      AND (@idCuenta        IS NULL OR k.idCuenta        = @idCuenta)
      AND (@idGrupoCuenta   IS NULL OR k.idGrupoCuenta   = @idGrupoCuenta OR c.idGrupoCuenta = @idGrupoCuenta)
      AND (@idTransportista IS NULL OR k.idTransportista = @idTransportista)
      AND (@buscar IS NULL OR k.numeroCaja LIKE N'%' + @buscar + N'%' OR k.placa LIKE N'%' + @buscar + N'%' OR k.vin LIKE N'%' + @buscar + N'%')
    ORDER BY propietario, k.numeroCaja;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_ObtenerCajaPorId @idCaja INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT k.idCaja, k.numeroCaja, k.tipoPropiedad,
           k.idCuenta, c.nombreComercial AS cuenta,
           k.idGrupoCuenta, g.nombre AS grupoCuenta,
           k.idTransportista, t.nombre AS transportista,
           k.placa, k.vin, k.anio, k.marca, k.pies,
           k.activo, k.fechaCreacion, k.fechaModificacion
    FROM flota.Caja k
    LEFT JOIN dir.Cuenta c          ON c.idCuenta        = k.idCuenta
    LEFT JOIN dir.GrupoCuenta g     ON g.idGrupoCuenta   = k.idGrupoCuenta
    LEFT JOIN flota.Transportista t ON t.idTransportista = k.idTransportista
    WHERE k.idCaja = @idCaja;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_InsertarCaja
    @numeroCaja        NVARCHAR(30),
    @tipoPropiedad     NVARCHAR(20),
    @idCuenta          INT = NULL,
    @idGrupoCuenta     INT = NULL,
    @idTransportista   INT = NULL,
    @placa             NVARCHAR(20) = NULL,
    @vin               NVARCHAR(20) = NULL,
    @anio              SMALLINT = NULL,
    @marca             NVARCHAR(60) = NULL,
    @pies              TINYINT = NULL,
    @devolverResultado BIT = 1,
    @idCaja            INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @numeroCaja = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(@numeroCaja)), N' ', N''), N'-', N''));
    SET @vin = NULLIF(UPPER(REPLACE(LTRIM(RTRIM(@vin)), N' ', N'')), N'');
    SET @placa = NULLIF(UPPER(LTRIM(RTRIM(@placa))), N'');
    IF @numeroCaja IS NULL OR @numeroCaja = N'' THROW 50001, N'El numero de caja es obligatorio.', 1;
    IF @tipoPropiedad NOT IN (N'Cliente', N'Transportista', N'EP') THROW 50005, N'El tipo de propiedad debe ser Cliente, Transportista o EP.', 1;
    IF @tipoPropiedad = N'Cliente' AND (@idCuenta IS NULL AND @idGrupoCuenta IS NULL OR @idCuenta IS NOT NULL AND @idGrupoCuenta IS NOT NULL)
        THROW 50005, N'Una caja de cliente se liga a una cuenta o a un grupo (solo uno).', 1;
    IF @tipoPropiedad = N'Transportista' AND @idTransportista IS NULL THROW 50005, N'Una caja de transportista requiere el transportista.', 1;
    IF @tipoPropiedad <> N'Cliente' AND (@idCuenta IS NOT NULL OR @idGrupoCuenta IS NOT NULL)
        THROW 50005, N'Solo las cajas de cliente llevan cuenta o grupo.', 1;
    IF @idCuenta IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dir.Cuenta WHERE idCuenta = @idCuenta AND activo = 1)
        THROW 50004, N'La cuenta indicada no existe o esta inactiva.', 1;
    IF @idGrupoCuenta IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dir.GrupoCuenta WHERE idGrupoCuenta = @idGrupoCuenta AND activo = 1)
        THROW 50004, N'El grupo indicado no existe o esta inactivo.', 1;
    IF @idTransportista IS NOT NULL AND NOT EXISTS (SELECT 1 FROM flota.Transportista WHERE idTransportista = @idTransportista AND activo = 1)
        THROW 50004, N'El transportista indicado no existe o esta inactivo.', 1;

    BEGIN TRAN;
    IF EXISTS (SELECT 1 FROM flota.Caja WITH (UPDLOCK, HOLDLOCK)
               WHERE numeroCaja = @numeroCaja AND activo = 1
                 AND (tipoPropiedad <> N'Transportista' AND @tipoPropiedad <> N'Transportista'
                      OR tipoPropiedad = N'Transportista' AND @tipoPropiedad = N'Transportista' AND idTransportista = @idTransportista))
    BEGIN
        ROLLBACK;
        THROW 50002, N'Ya existe una caja activa con ese numero.', 1;
    END
    IF @vin IS NOT NULL AND EXISTS (SELECT 1 FROM flota.Caja WITH (UPDLOCK, HOLDLOCK) WHERE vin = @vin AND activo = 1)
    BEGIN
        ROLLBACK;
        THROW 50002, N'Ya existe una caja activa con ese VIN.', 1;
    END

    INSERT INTO flota.Caja (numeroCaja, tipoPropiedad, idCuenta, idGrupoCuenta, idTransportista, placa, vin, anio, marca, pies)
    VALUES (@numeroCaja, @tipoPropiedad, @idCuenta, @idGrupoCuenta, @idTransportista, @placa, @vin, @anio, @marca, @pies);
    SET @idCaja = SCOPE_IDENTITY();
    COMMIT;

    IF @devolverResultado = 1 SELECT @idCaja AS idCaja;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_ActualizarCaja
    @idCaja          INT,
    @numeroCaja      NVARCHAR(30),
    @tipoPropiedad   NVARCHAR(20),
    @idCuenta        INT = NULL,
    @idGrupoCuenta   INT = NULL,
    @idTransportista INT = NULL,
    @placa           NVARCHAR(20) = NULL,
    @vin             NVARCHAR(20) = NULL,
    @anio            SMALLINT = NULL,
    @marca           NVARCHAR(60) = NULL,
    @pies            TINYINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @numeroCaja = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(@numeroCaja)), N' ', N''), N'-', N''));
    SET @vin = NULLIF(UPPER(REPLACE(LTRIM(RTRIM(@vin)), N' ', N'')), N'');
    SET @placa = NULLIF(UPPER(LTRIM(RTRIM(@placa))), N'');
    IF @numeroCaja IS NULL OR @numeroCaja = N'' THROW 50001, N'El numero de caja es obligatorio.', 1;
    IF NOT EXISTS (SELECT 1 FROM flota.Caja WHERE idCaja = @idCaja) THROW 50003, N'La caja no existe.', 1;
    IF @tipoPropiedad NOT IN (N'Cliente', N'Transportista', N'EP') THROW 50005, N'El tipo de propiedad debe ser Cliente, Transportista o EP.', 1;
    IF @tipoPropiedad = N'Cliente' AND (@idCuenta IS NULL AND @idGrupoCuenta IS NULL OR @idCuenta IS NOT NULL AND @idGrupoCuenta IS NOT NULL)
        THROW 50005, N'Una caja de cliente se liga a una cuenta o a un grupo (solo uno).', 1;
    IF @tipoPropiedad = N'Transportista' AND @idTransportista IS NULL THROW 50005, N'Una caja de transportista requiere el transportista.', 1;
    IF @tipoPropiedad <> N'Cliente' AND (@idCuenta IS NOT NULL OR @idGrupoCuenta IS NOT NULL)
        THROW 50005, N'Solo las cajas de cliente llevan cuenta o grupo.', 1;
    IF @idCuenta IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dir.Cuenta WHERE idCuenta = @idCuenta AND activo = 1)
        THROW 50004, N'La cuenta indicada no existe o esta inactiva.', 1;
    IF @idGrupoCuenta IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dir.GrupoCuenta WHERE idGrupoCuenta = @idGrupoCuenta AND activo = 1)
        THROW 50004, N'El grupo indicado no existe o esta inactivo.', 1;
    IF @idTransportista IS NOT NULL AND NOT EXISTS (SELECT 1 FROM flota.Transportista WHERE idTransportista = @idTransportista AND activo = 1)
        THROW 50004, N'El transportista indicado no existe o esta inactivo.', 1;
    IF EXISTS (SELECT 1 FROM flota.Caja
               WHERE numeroCaja = @numeroCaja AND activo = 1 AND idCaja <> @idCaja
                 AND (tipoPropiedad <> N'Transportista' AND @tipoPropiedad <> N'Transportista'
                      OR tipoPropiedad = N'Transportista' AND @tipoPropiedad = N'Transportista' AND idTransportista = @idTransportista))
        THROW 50002, N'Ya existe una caja activa con ese numero.', 1;
    IF @vin IS NOT NULL AND EXISTS (SELECT 1 FROM flota.Caja WHERE vin = @vin AND activo = 1 AND idCaja <> @idCaja)
        THROW 50002, N'Ya existe una caja activa con ese VIN.', 1;

    UPDATE flota.Caja
    SET numeroCaja = @numeroCaja, tipoPropiedad = @tipoPropiedad, idCuenta = @idCuenta, idGrupoCuenta = @idGrupoCuenta,
        idTransportista = @idTransportista, placa = @placa, vin = @vin, anio = @anio, marca = @marca, pies = @pies,
        fechaModificacion = GETDATE()
    WHERE idCaja = @idCaja;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_DesactivarCaja @idCaja INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM flota.Caja WHERE idCaja = @idCaja) THROW 50003, N'La caja no existe.', 1;
    UPDATE flota.Caja SET activo = 0, fechaModificacion = GETDATE() WHERE idCaja = @idCaja;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_ReactivarCaja @idCaja INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @numeroCaja NVARCHAR(30), @tipo NVARCHAR(20), @idTransportista INT, @vin NVARCHAR(20);
    SELECT @numeroCaja = numeroCaja, @tipo = tipoPropiedad, @idTransportista = idTransportista, @vin = vin FROM flota.Caja WHERE idCaja = @idCaja;
    IF @numeroCaja IS NULL THROW 50003, N'La caja no existe.', 1;
    IF EXISTS (SELECT 1 FROM flota.Caja
               WHERE numeroCaja = @numeroCaja AND activo = 1 AND idCaja <> @idCaja
                 AND (tipoPropiedad <> N'Transportista' AND @tipo <> N'Transportista'
                      OR tipoPropiedad = N'Transportista' AND @tipo = N'Transportista' AND idTransportista = @idTransportista))
        THROW 50002, N'Ya existe otra caja activa con ese numero.', 1;
    IF @vin IS NOT NULL AND EXISTS (SELECT 1 FROM flota.Caja WHERE vin = @vin AND activo = 1 AND idCaja <> @idCaja)
        THROW 50002, N'Ya existe otra caja activa con ese VIN.', 1;
    -- Si su propietario ya no esta activo no se puede reactivar sin reasignarla (sp_ActualizarCaja)
    IF EXISTS (SELECT 1 FROM flota.Caja k
               LEFT JOIN dir.Cuenta c          ON c.idCuenta        = k.idCuenta
               LEFT JOIN dir.GrupoCuenta g     ON g.idGrupoCuenta   = k.idGrupoCuenta
               LEFT JOIN flota.Transportista t ON t.idTransportista = k.idTransportista
               WHERE k.idCaja = @idCaja AND (c.activo = 0 OR g.activo = 0 OR (k.tipoPropiedad = N'Transportista' AND t.activo = 0)))
        THROW 50004, N'El propietario de la caja esta inactivo; reasignala antes de reactivarla.', 1;
    UPDATE flota.Caja SET activo = 1, fechaModificacion = GETDATE() WHERE idCaja = @idCaja;
END
GO

/* =====================================================================
   TRACTORES
   ===================================================================== */
CREATE OR ALTER PROCEDURE flota.sp_ObtenerTractores
    @incluirInactivos BIT = 0,
    @idTransportista  INT = NULL,
    @buscar           NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.idTractor, r.economico, r.idTransportista, t.nombre AS transportista,
           r.placa, r.vin, r.anio, r.marca, r.activo
    FROM flota.Tractor r
    JOIN flota.Transportista t ON t.idTransportista = r.idTransportista
    WHERE (@incluirInactivos = 1 OR r.activo = 1)
      AND (@idTransportista IS NULL OR r.idTransportista = @idTransportista)
      AND (@buscar IS NULL OR r.economico LIKE N'%' + @buscar + N'%' OR r.placa LIKE N'%' + @buscar + N'%')
    ORDER BY t.nombre, r.economico;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_ObtenerTractorPorId @idTractor INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT r.idTractor, r.economico, r.idTransportista, t.nombre AS transportista,
           r.placa, r.vin, r.anio, r.marca, r.activo, r.fechaCreacion, r.fechaModificacion
    FROM flota.Tractor r
    JOIN flota.Transportista t ON t.idTransportista = r.idTransportista
    WHERE r.idTractor = @idTractor;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_InsertarTractor
    @idTransportista   INT,
    @economico         NVARCHAR(30),
    @placa             NVARCHAR(20) = NULL,
    @vin               NVARCHAR(20) = NULL,
    @anio              SMALLINT = NULL,
    @marca             NVARCHAR(60) = NULL,
    @devolverResultado BIT = 1,
    @idTractor         INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @economico = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(@economico)), N' ', N''), N'-', N''));
    SET @vin = NULLIF(UPPER(REPLACE(LTRIM(RTRIM(@vin)), N' ', N'')), N'');
    SET @placa = NULLIF(UPPER(LTRIM(RTRIM(@placa))), N'');
    IF @economico IS NULL OR @economico = N'' THROW 50001, N'El numero economico es obligatorio.', 1;
    IF NOT EXISTS (SELECT 1 FROM flota.Transportista WHERE idTransportista = @idTransportista AND activo = 1)
        THROW 50004, N'El transportista indicado no existe o esta inactivo.', 1;

    BEGIN TRAN;
    IF EXISTS (SELECT 1 FROM flota.Tractor WITH (UPDLOCK, HOLDLOCK) WHERE idTransportista = @idTransportista AND economico = @economico AND activo = 1)
    BEGIN
        ROLLBACK;
        THROW 50002, N'Ese transportista ya tiene un tractor activo con ese economico.', 1;
    END
    IF @vin IS NOT NULL AND EXISTS (SELECT 1 FROM flota.Tractor WITH (UPDLOCK, HOLDLOCK) WHERE vin = @vin AND activo = 1)
    BEGIN
        ROLLBACK;
        THROW 50002, N'Ya existe un tractor activo con ese VIN.', 1;
    END

    INSERT INTO flota.Tractor (idTransportista, economico, placa, vin, anio, marca)
    VALUES (@idTransportista, @economico, @placa, @vin, @anio, @marca);
    SET @idTractor = SCOPE_IDENTITY();
    COMMIT;

    IF @devolverResultado = 1 SELECT @idTractor AS idTractor;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_ActualizarTractor
    @idTractor       INT,
    @idTransportista INT,
    @economico       NVARCHAR(30),
    @placa           NVARCHAR(20) = NULL,
    @vin             NVARCHAR(20) = NULL,
    @anio            SMALLINT = NULL,
    @marca           NVARCHAR(60) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @economico = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(@economico)), N' ', N''), N'-', N''));
    SET @vin = NULLIF(UPPER(REPLACE(LTRIM(RTRIM(@vin)), N' ', N'')), N'');
    SET @placa = NULLIF(UPPER(LTRIM(RTRIM(@placa))), N'');
    IF @economico IS NULL OR @economico = N'' THROW 50001, N'El numero economico es obligatorio.', 1;
    IF NOT EXISTS (SELECT 1 FROM flota.Tractor WHERE idTractor = @idTractor) THROW 50003, N'El tractor no existe.', 1;
    IF NOT EXISTS (SELECT 1 FROM flota.Transportista WHERE idTransportista = @idTransportista AND activo = 1)
        THROW 50004, N'El transportista indicado no existe o esta inactivo.', 1;
    IF EXISTS (SELECT 1 FROM flota.Tractor WHERE idTransportista = @idTransportista AND economico = @economico AND activo = 1 AND idTractor <> @idTractor)
        THROW 50002, N'Ese transportista ya tiene un tractor activo con ese economico.', 1;
    IF @vin IS NOT NULL AND EXISTS (SELECT 1 FROM flota.Tractor WHERE vin = @vin AND activo = 1 AND idTractor <> @idTractor)
        THROW 50002, N'Ya existe un tractor activo con ese VIN.', 1;

    UPDATE flota.Tractor
    SET idTransportista = @idTransportista, economico = @economico, placa = @placa, vin = @vin, anio = @anio, marca = @marca,
        fechaModificacion = GETDATE()
    WHERE idTractor = @idTractor;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_DesactivarTractor @idTractor INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM flota.Tractor WHERE idTractor = @idTractor) THROW 50003, N'El tractor no existe.', 1;
    UPDATE flota.Tractor SET activo = 0, fechaModificacion = GETDATE() WHERE idTractor = @idTractor;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_ReactivarTractor @idTractor INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @idTransportista INT, @economico NVARCHAR(30), @vin NVARCHAR(20);
    SELECT @idTransportista = idTransportista, @economico = economico, @vin = vin FROM flota.Tractor WHERE idTractor = @idTractor;
    IF @economico IS NULL THROW 50003, N'El tractor no existe.', 1;
    IF NOT EXISTS (SELECT 1 FROM flota.Transportista WHERE idTransportista = @idTransportista AND activo = 1)
        THROW 50004, N'Su transportista esta inactivo; reactivalo primero.', 1;
    IF EXISTS (SELECT 1 FROM flota.Tractor WHERE idTransportista = @idTransportista AND economico = @economico AND activo = 1 AND idTractor <> @idTractor)
        THROW 50002, N'Ese transportista ya tiene otro tractor activo con ese economico.', 1;
    IF @vin IS NOT NULL AND EXISTS (SELECT 1 FROM flota.Tractor WHERE vin = @vin AND activo = 1 AND idTractor <> @idTractor)
        THROW 50002, N'Ya existe otro tractor activo con ese VIN.', 1;
    UPDATE flota.Tractor SET activo = 1, fechaModificacion = GETDATE() WHERE idTractor = @idTractor;
END
GO

/* =====================================================================
   CHOFERES
   ===================================================================== */
CREATE OR ALTER PROCEDURE flota.sp_ObtenerChoferes
    @incluirInactivos BIT = 0,
    @idTransportista  INT = NULL,
    @buscar           NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT h.idChofer, h.nombreCompleto, h.licencia, h.telefono,
           h.idTransportista, t.nombre AS transportista, h.activo
    FROM flota.Chofer h
    LEFT JOIN flota.Transportista t ON t.idTransportista = h.idTransportista
    WHERE (@incluirInactivos = 1 OR h.activo = 1)
      AND (@idTransportista IS NULL OR h.idTransportista = @idTransportista)
      AND (@buscar IS NULL OR h.nombreCompleto LIKE N'%' + @buscar + N'%' OR h.licencia LIKE N'%' + @buscar + N'%')
    ORDER BY h.nombreCompleto;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_ObtenerChoferPorId @idChofer INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT h.idChofer, h.nombreCompleto, h.licencia, h.telefono,
           h.idTransportista, t.nombre AS transportista,
           h.activo, h.fechaCreacion, h.fechaModificacion
    FROM flota.Chofer h
    LEFT JOIN flota.Transportista t ON t.idTransportista = h.idTransportista
    WHERE h.idChofer = @idChofer;
END
GO

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

    INSERT INTO flota.Chofer (idTransportista, nombreCompleto, licencia, telefono)
    VALUES (@idTransportista, @nombreCompleto, @licencia, @telefono);
    SET @idChofer = SCOPE_IDENTITY();
    COMMIT;

    IF @devolverResultado = 1 SELECT @idChofer AS idChofer;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_ActualizarChofer
    @idChofer        INT,
    @nombreCompleto  NVARCHAR(150),
    @idTransportista INT = NULL,
    @licencia        NVARCHAR(30) = NULL,
    @telefono        NVARCHAR(40) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @nombreCompleto = UPPER(LTRIM(RTRIM(@nombreCompleto)));
    SET @licencia = NULLIF(UPPER(REPLACE(LTRIM(RTRIM(@licencia)), N' ', N'')), N'');
    IF @nombreCompleto IS NULL OR @nombreCompleto = N'' THROW 50001, N'El nombre del chofer es obligatorio.', 1;
    IF NOT EXISTS (SELECT 1 FROM flota.Chofer WHERE idChofer = @idChofer) THROW 50003, N'El chofer no existe.', 1;
    IF @idTransportista IS NOT NULL AND NOT EXISTS (SELECT 1 FROM flota.Transportista WHERE idTransportista = @idTransportista AND activo = 1)
        THROW 50004, N'El transportista indicado no existe o esta inactivo.', 1;
    IF @licencia IS NOT NULL AND EXISTS (SELECT 1 FROM flota.Chofer WHERE licencia = @licencia AND activo = 1 AND idChofer <> @idChofer)
        THROW 50002, N'Ya existe un chofer activo con esa licencia.', 1;

    UPDATE flota.Chofer
    SET nombreCompleto = @nombreCompleto, idTransportista = @idTransportista, licencia = @licencia, telefono = @telefono,
        fechaModificacion = GETDATE()
    WHERE idChofer = @idChofer;
END
GO

CREATE OR ALTER PROCEDURE flota.sp_DesactivarChofer @idChofer INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM flota.Chofer WHERE idChofer = @idChofer) THROW 50003, N'El chofer no existe.', 1;
    UPDATE flota.Chofer SET activo = 0, fechaModificacion = GETDATE() WHERE idChofer = @idChofer;
END
GO

/* =====================================================================
   AJUSTE A PROCEDIMIENTOS EXISTENTES DE dir.*
   Se conserva su logica original y se agrega el manejo de flota.Caja,
   porque una caja de cliente no puede quedar sin cuenta/grupo
   (CK_Caja_propietario): al desactivar el propietario se desactivan
   sus cajas. Al reactivar la cuenta/grupo las cajas NO se reactivan
   solas (flota.sp_ReactivarCaja una por una), igual que PersonaCuenta.
   ===================================================================== */
/* Desactivar un grupo deja sus cuentas sin grupo (igual que Balance), da de baja las cajas
   ligadas al grupo y luego da de baja el grupo. Las cajas ligadas a sus cuentas no se tocan. */
CREATE OR ALTER PROCEDURE dir.sp_DesactivarGrupoCuenta @idGrupoCuenta INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dir.GrupoCuenta WHERE idGrupoCuenta = @idGrupoCuenta) THROW 50003, N'El grupo no existe.', 1;

    BEGIN TRAN;
    UPDATE flota.Caja SET activo = 0, fechaModificacion = GETDATE() WHERE idGrupoCuenta = @idGrupoCuenta AND activo = 1;
    UPDATE dir.Cuenta SET idGrupoCuenta = NULL, fechaModificacion = GETDATE() WHERE idGrupoCuenta = @idGrupoCuenta;
    UPDATE dir.GrupoCuenta SET activo = 0, fechaModificacion = GETDATE() WHERE idGrupoCuenta = @idGrupoCuenta;
    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_DesactivarCuenta @idCuenta INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dir.Cuenta WHERE idCuenta = @idCuenta) THROW 50003, N'La cuenta no existe.', 1;

    BEGIN TRAN;
    UPDATE flota.Caja SET activo = 0, fechaModificacion = GETDATE() WHERE idCuenta = @idCuenta AND activo = 1;
    UPDATE flota.Transportista SET idCuenta = NULL, fechaModificacion = GETDATE() WHERE idCuenta = @idCuenta;
    UPDATE dir.PersonaCuenta SET activo = 0, fechaModificacion = GETDATE() WHERE idCuenta = @idCuenta AND activo = 1;
    UPDATE dir.Cuenta SET activo = 0, fechaModificacion = GETDATE() WHERE idCuenta = @idCuenta;
    COMMIT;
END
GO

/* Si su transportista ya esta inactivo, el chofer vuelve sin transportista (igual que Cuenta/Grupo). */
CREATE OR ALTER PROCEDURE flota.sp_ReactivarChofer @idChofer INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @licencia NVARCHAR(30);
    IF NOT EXISTS (SELECT 1 FROM flota.Chofer WHERE idChofer = @idChofer) THROW 50003, N'El chofer no existe.', 1;
    SELECT @licencia = licencia FROM flota.Chofer WHERE idChofer = @idChofer;
    IF @licencia IS NOT NULL AND EXISTS (SELECT 1 FROM flota.Chofer WHERE licencia = @licencia AND activo = 1 AND idChofer <> @idChofer)
        THROW 50002, N'Ya existe otro chofer activo con esa licencia.', 1;
    UPDATE h SET h.activo = 1, h.fechaModificacion = GETDATE(),
                 h.idTransportista = CASE WHEN t.activo = 1 THEN h.idTransportista ELSE NULL END
    FROM flota.Chofer h
    LEFT JOIN flota.Transportista t ON t.idTransportista = h.idTransportista
    WHERE h.idChofer = @idChofer;
END
GO
