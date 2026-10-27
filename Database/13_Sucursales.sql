/* =====================================================================
   13_Sucursales.sql
   BD global: EP360_Logistics   -> correr en el 60 Y en el 11 (cada uno por separado), DESPUES del 11c.
   REQUIERE haber corrido antes: 01 a 11c.

   El catalogo de SUCURSALES se administra desde la app de la global (antes se "inhabilitaban" desde EP360, solo para EP360).
   Ahora el estado activo/inactivo vive aqui y vale para todos los portales.

   Crea:
     dir.sp_ObtenerSucursalesConUso   sucursales con cuantas personas y cuentas activas tienen asignadas
     dir.sp_InsertarSucursal          alta (devuelve idSucursal)
     dir.sp_ActualizarSucursal        renombrar
     dir.sp_DesactivarSucursal / dir.sp_ReactivarSucursal
   Cambia:
     dir.sp_ObtenerOCrearSucursal     la usa la sincronizacion con AD. Ahora reutiliza la sucursal por nombre AUNQUE este inactiva
                                      (antes solo buscaba entre las activas: con una inactiva del mismo nombre intentaba insertar otra y
                                      chocaba con UQ_Sucursal_nombre). Una sucursal inactiva se queda inactiva; no se vuelve a crear.

   Reglas (errores 50061-50063, con mensaje para el usuario): nombre obligatorio; el nombre no se repite (ni entre inactivas, ni por
   mayusculas o acentos); la sucursal debe existir. Desactivar NO quita a las personas su sucursal: solo deja de ofrecerse como opcion.

   Es IDEMPOTENTE. No toca datos. Quien lo corre: el dueno del proyecto, en SSMS.
   ===================================================================== */

USE EP360_Logistics;
GO

CREATE OR ALTER PROCEDURE dir.sp_ObtenerSucursalesConUso
    @incluirInactivos BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.idSucursal, s.nombre, s.activo, s.fechaCreacion,
           (SELECT COUNT(*) FROM dir.Persona p WHERE p.idSucursal = s.idSucursal AND p.activo = 1) AS personas,
           (SELECT COUNT(*) FROM dir.Cuenta  c WHERE c.idSucursal = s.idSucursal AND c.activo = 1) AS cuentas
    FROM dir.Sucursal s
    WHERE @incluirInactivos = 1 OR s.activo = 1
    ORDER BY s.nombre;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_InsertarSucursal
    @nombre NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @nombre = LTRIM(RTRIM(@nombre));
    IF @nombre IS NULL OR @nombre = N'' THROW 50061, N'El nombre de la sucursal es obligatorio.', 1;

    BEGIN TRAN;
    IF EXISTS (SELECT 1 FROM dir.Sucursal WITH (UPDLOCK, HOLDLOCK) WHERE nombre COLLATE Latin1_General_CI_AI = @nombre COLLATE Latin1_General_CI_AI)
    BEGIN
        ROLLBACK;
        THROW 50062, N'Ya existe una sucursal con ese nombre (si esta inactiva, reactivala en vez de crearla).', 1;
    END

    INSERT INTO dir.Sucursal (nombre) VALUES (@nombre);
    DECLARE @id INT = SCOPE_IDENTITY();

    INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle) VALUES (N'Sucursal', @id, N'Crear', LEFT(@nombre, 500));
    COMMIT;

    SELECT @id AS idSucursal;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ActualizarSucursal
    @idSucursal INT,
    @nombre     NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @nombre = LTRIM(RTRIM(@nombre));
    IF @nombre IS NULL OR @nombre = N'' THROW 50061, N'El nombre de la sucursal es obligatorio.', 1;
    IF NOT EXISTS (SELECT 1 FROM dir.Sucursal WHERE idSucursal = @idSucursal) THROW 50063, N'La sucursal no existe.', 1;

    BEGIN TRAN;
    IF EXISTS (SELECT 1 FROM dir.Sucursal WITH (UPDLOCK, HOLDLOCK)
               WHERE idSucursal <> @idSucursal AND nombre COLLATE Latin1_General_CI_AI = @nombre COLLATE Latin1_General_CI_AI)
    BEGIN
        ROLLBACK;
        THROW 50062, N'Ya existe otra sucursal con ese nombre.', 1;
    END

    UPDATE dir.Sucursal SET nombre = @nombre, fechaModificacion = GETDATE() WHERE idSucursal = @idSucursal;
    INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle) VALUES (N'Sucursal', @idSucursal, N'Renombrar', LEFT(@nombre, 500));
    COMMIT;
END
GO

CREATE OR ALTER PROCEDURE dir.sp_DesactivarSucursal @idSucursal INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dir.Sucursal WHERE idSucursal = @idSucursal) THROW 50063, N'La sucursal no existe.', 1;

    UPDATE dir.Sucursal SET activo = 0, fechaModificacion = GETDATE() WHERE idSucursal = @idSucursal AND activo = 1;
    IF @@ROWCOUNT > 0
        INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle) VALUES (N'Sucursal', @idSucursal, N'Desactivar', NULL);
END
GO

CREATE OR ALTER PROCEDURE dir.sp_ReactivarSucursal @idSucursal INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dir.Sucursal WHERE idSucursal = @idSucursal) THROW 50063, N'La sucursal no existe.', 1;

    UPDATE dir.Sucursal SET activo = 1, fechaModificacion = GETDATE() WHERE idSucursal = @idSucursal AND activo = 0;
    IF @@ROWCOUNT > 0
        INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle) VALUES (N'Sucursal', @idSucursal, N'Reactivar', NULL);
END
GO

-- La usa la sincronizacion con AD: devuelve la sucursal por nombre, activa o no (nunca duplica).
CREATE OR ALTER PROCEDURE dir.sp_ObtenerOCrearSucursal @nombre NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @nombre = LTRIM(RTRIM(@nombre));
    IF @nombre IS NULL OR @nombre = N'' THROW 50001, N'El nombre de la sucursal es obligatorio.', 1;

    DECLARE @id INT;
    BEGIN TRAN;
    SELECT TOP (1) @id = idSucursal FROM dir.Sucursal WITH (UPDLOCK, HOLDLOCK)
    WHERE nombre COLLATE Latin1_General_CI_AI = @nombre COLLATE Latin1_General_CI_AI
    ORDER BY activo DESC, idSucursal;
    IF @id IS NULL
    BEGIN
        INSERT INTO dir.Sucursal (nombre) VALUES (@nombre);
        SET @id = SCOPE_IDENTITY();
    END
    COMMIT;
    SELECT @id AS idSucursal;
END
GO

PRINT N'13_Sucursales.sql terminado.';
GO
