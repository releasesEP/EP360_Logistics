/* =====================================================================
   17_ImpresorasCompartidas.sql
   BD global: EP360_Logistics   -> correr en el 60 Y en el 11 (cada uno por separado), DESPUES del 16.
   REQUIERE haber corrido antes: 01 a 16.

   Catalogo GLOBAL de impresoras compartidas de documentos (las del servidor de impresion \\192.168.50.4). Se administra desde la app
   de la global (Directorio > Impresoras) y lo puede usar cualquier portal: cada portal decide por su cuenta cuales ocupa.
   (EP360 Traceability las ocupa para imprimir discrepancias e inspecciones; ver su script 151.)

   Crea:
     dir.ImpresoraCompartida          tabla (nombre, ruta compartida unica, sucursal opcional, activo)
     dir.sp_ObtenerImpresoras         catalogo con el nombre de la sucursal
     dir.sp_GuardarImpresora          alta (@idImpresora NULL) o edicion; devuelve idImpresora
     dir.sp_DesactivarImpresora / dir.sp_ReactivarImpresora
   Cambia:
     sync.sp_ObtenerConteosReplicacion   incluye dir.ImpresoraCompartida (el Replicador compara 60 vs 11)
   Siembra las 8 impresoras de documentos del 192.168.50.4 (las de etiquetas ZDesigner/Citizen no se siembran a proposito).

   Errores con mensaje para el usuario: 50071 nombre obligatorio, 50072 ruta obligatoria, 50073 ruta repetida, 50074 no existe.
   Es IDEMPOTENTE. Quien lo corre: el dueno del proyecto (sqlcmd -f 65001 -I).
   ===================================================================== */

USE EP360_Logistics;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dir.ImpresoraCompartida', N'U') IS NULL
BEGIN
    CREATE TABLE dir.ImpresoraCompartida (
        idImpresora        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ImpresoraCompartida PRIMARY KEY,
        nombre             NVARCHAR(100) NOT NULL,
        rutaCompartida     NVARCHAR(260) NOT NULL,
        idSucursal         INT NULL CONSTRAINT FK_ImpresoraCompartida_Sucursal REFERENCES dir.Sucursal(idSucursal),
        activo             BIT NOT NULL CONSTRAINT DF_ImpresoraCompartida_activo DEFAULT (1),
        fechaCreacion      DATETIME NOT NULL CONSTRAINT DF_ImpresoraCompartida_fecha DEFAULT (GETDATE()),
        fechaModificacion  DATETIME NULL,
        CONSTRAINT UQ_ImpresoraCompartida_ruta UNIQUE (rutaCompartida)
    );
END
GO

MERGE dir.ImpresoraCompartida AS destino
USING (VALUES
    (N'EPL1-OFFICE PRINTER',   N'\\192.168.50.4\EPL1-OFFICE PRINTER'),
    (N'EPL1-WH PRINTER',       N'\\192.168.50.4\EPL1-WH PRINTER'),
    (N'EPL1- TRAFFIC PRINTER', N'\\192.168.50.4\EPL1- TRAFFIC PRINTER'),
    (N'EPL1-RH PRINTER',       N'\\192.168.50.4\EPL1-RH PRINTER'),
    (N'EPL2-WH PRINTER',       N'\\192.168.50.4\EPL2-WH PRINTER'),
    (N'EPL11-TFM PRINTER',     N'\\192.168.50.4\EPL11-TFM PRINTER'),
    (N'EPL9-OFFICE PRINTER',   N'\\192.168.50.4\EPL9-OFFICE PRINTER'),
    (N'EPL-JULIO PRINTER',     N'\\192.168.50.4\EPL-JULIO PRINTER')
) AS origen (nombre, rutaCompartida)
ON destino.rutaCompartida = origen.rutaCompartida
WHEN NOT MATCHED THEN INSERT (nombre, rutaCompartida) VALUES (origen.nombre, origen.rutaCompartida);
GO

CREATE OR ALTER PROCEDURE dir.sp_ObtenerImpresoras
    @incluirInactivas BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT i.idImpresora, i.nombre, i.rutaCompartida, i.idSucursal, s.nombre AS sucursal, i.activo, i.fechaCreacion
    FROM dir.ImpresoraCompartida i
    LEFT JOIN dir.Sucursal s ON s.idSucursal = i.idSucursal
    WHERE @incluirInactivas = 1 OR i.activo = 1
    ORDER BY i.nombre;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_GuardarImpresora
    @idImpresora    INT = NULL,
    @nombre         NVARCHAR(100),
    @rutaCompartida NVARCHAR(260),
    @idSucursal     INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @nombre = LTRIM(RTRIM(@nombre));
    SET @rutaCompartida = LTRIM(RTRIM(@rutaCompartida));
    IF @nombre IS NULL OR @nombre = N'' THROW 50071, N'El nombre de la impresora es obligatorio.', 1;
    IF @rutaCompartida IS NULL OR @rutaCompartida = N'' THROW 50072, N'La ruta compartida es obligatoria (por ejemplo \\192.168.50.4\EPL1-WH PRINTER).', 1;
    IF @idImpresora IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dir.ImpresoraCompartida WHERE idImpresora = @idImpresora)
        THROW 50074, N'La impresora no existe.', 1;

    BEGIN TRAN;
    IF EXISTS (SELECT 1 FROM dir.ImpresoraCompartida WITH (UPDLOCK, HOLDLOCK)
               WHERE rutaCompartida COLLATE Latin1_General_CI_AI = @rutaCompartida COLLATE Latin1_General_CI_AI
                 AND idImpresora <> ISNULL(@idImpresora, 0))
    BEGIN
        ROLLBACK;
        THROW 50073, N'Ya existe una impresora con esa ruta compartida.', 1;
    END

    IF @idImpresora IS NULL
    BEGIN
        INSERT INTO dir.ImpresoraCompartida (nombre, rutaCompartida, idSucursal) VALUES (@nombre, @rutaCompartida, @idSucursal);
        SET @idImpresora = CAST(SCOPE_IDENTITY() AS INT);
        INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle) VALUES (N'Impresora', @idImpresora, N'Crear', LEFT(@rutaCompartida, 500));
    END
    ELSE
    BEGIN
        UPDATE dir.ImpresoraCompartida
           SET nombre = @nombre, rutaCompartida = @rutaCompartida, idSucursal = @idSucursal, fechaModificacion = GETDATE()
         WHERE idImpresora = @idImpresora;
        INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle) VALUES (N'Impresora', @idImpresora, N'Editar', LEFT(@rutaCompartida, 500));
    END
    COMMIT;

    SELECT @idImpresora AS idImpresora;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_DesactivarImpresora @idImpresora INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dir.ImpresoraCompartida WHERE idImpresora = @idImpresora) THROW 50074, N'La impresora no existe.', 1;

    UPDATE dir.ImpresoraCompartida SET activo = 0, fechaModificacion = GETDATE() WHERE idImpresora = @idImpresora AND activo = 1;
    IF @@ROWCOUNT > 0
        INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle) VALUES (N'Impresora', @idImpresora, N'Desactivar', NULL);
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ReactivarImpresora @idImpresora INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dir.ImpresoraCompartida WHERE idImpresora = @idImpresora) THROW 50074, N'La impresora no existe.', 1;

    UPDATE dir.ImpresoraCompartida SET activo = 1, fechaModificacion = GETDATE() WHERE idImpresora = @idImpresora AND activo = 0;
    IF @@ROWCOUNT > 0
        INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle) VALUES (N'Impresora', @idImpresora, N'Reactivar', NULL);
END
GO

-- Mismo SP de conteos vigente del 11c_ReplicacionVigilaAccesos con una linea mas (dir.ImpresoraCompartida).
CREATE OR ALTER PROCEDURE sync.sp_ObtenerConteosReplicacion
AS
BEGIN
    SET NOCOUNT ON;
    SELECT N'dir.Departamento'            AS tabla, (SELECT COUNT(*) FROM dir.Departamento)            AS filas, CAST(IDENT_CURRENT(N'dir.Departamento') AS BIGINT)            AS ultimoId
    UNION ALL SELECT N'dir.Sucursal',             (SELECT COUNT(*) FROM dir.Sucursal),             CAST(IDENT_CURRENT(N'dir.Sucursal') AS BIGINT)
    UNION ALL SELECT N'dir.Ciudad',               (SELECT COUNT(*) FROM dir.Ciudad),               CAST(IDENT_CURRENT(N'dir.Ciudad') AS BIGINT)
    UNION ALL SELECT N'dir.GrupoCuenta',          (SELECT COUNT(*) FROM dir.GrupoCuenta),          CAST(IDENT_CURRENT(N'dir.GrupoCuenta') AS BIGINT)
    UNION ALL SELECT N'dir.Cuenta',               (SELECT COUNT(*) FROM dir.Cuenta),               CAST(IDENT_CURRENT(N'dir.Cuenta') AS BIGINT)
    UNION ALL SELECT N'dir.Persona',              (SELECT COUNT(*) FROM dir.Persona),              CAST(IDENT_CURRENT(N'dir.Persona') AS BIGINT)
    UNION ALL SELECT N'dir.UsuarioAD',            (SELECT COUNT(*) FROM dir.UsuarioAD),            CAST(IDENT_CURRENT(N'dir.UsuarioAD') AS BIGINT)
    UNION ALL SELECT N'dir.PersonaCuenta',        (SELECT COUNT(*) FROM dir.PersonaCuenta),        CAST(IDENT_CURRENT(N'dir.PersonaCuenta') AS BIGINT)
    UNION ALL SELECT N'dir.PersonaMedioContacto', (SELECT COUNT(*) FROM dir.PersonaMedioContacto), CAST(IDENT_CURRENT(N'dir.PersonaMedioContacto') AS BIGINT)
    UNION ALL SELECT N'dir.ImpresoraCompartida',  (SELECT COUNT(*) FROM dir.ImpresoraCompartida),  CAST(IDENT_CURRENT(N'dir.ImpresoraCompartida') AS BIGINT)
    UNION ALL SELECT N'dir.PersonaPortal',        (SELECT COUNT(*) FROM dir.PersonaPortal),        CAST(IDENT_CURRENT(N'dir.PersonaPortal') AS BIGINT)
    UNION ALL SELECT N'seg.CredencialExterna',    (SELECT COUNT(*) FROM seg.CredencialExterna),    CAST(IDENT_CURRENT(N'seg.CredencialExterna') AS BIGINT)
    UNION ALL SELECT N'sync.EquivalenciaEntidad', (SELECT COUNT(*) FROM sync.EquivalenciaEntidad), CAST(IDENT_CURRENT(N'sync.EquivalenciaEntidad') AS BIGINT)
    ORDER BY tabla;
END
GO

PRINT N'17_ImpresorasCompartidas.sql terminado.';
GO
