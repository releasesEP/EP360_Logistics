/* =====================================================================
   10c_ReplicacionDetalle.sql
   BD global: EP360_Logistics   -> correr en el 60 Y en el 11 (cada uno por separado), DESPUES del 10.
   REQUIERE haber corrido antes: 10.

   Mejora la pantalla "Replicacion" de la app de administracion:
     * sync.ColaReplicacion guarda QUIEN hizo cada operacion (usuario) y quien la resolvio a mano (resueltaPor).
     * Nuevo estado 'Omitida': una operacion divergente o pendiente que un administrador decidio dejar pasar
       (por ejemplo porque ya corrigio el dato a mano). Al omitirla deja de bloquear la cola.
     * sync.sp_ObtenerHistorialReplicacion / sp_ObtenerOperacionReplicacion / sp_OmitirReplicacion: historial, detalle y omision.
     * sync.sp_AlinearIdentidad: sube el contador de ids de una tabla del 11 hasta el ultimo usado en el 60 (lo mismo que el
       10b, pero con boton en la pantalla). SOLO se usa en el 11; en el 60 queda sin uso.
     * sync.sp_EncolarReplicacion recibe el usuario (parametro opcional: el codigo viejo sigue funcionando).

   Es IDEMPOTENTE. No toca datos del directorio. Quien lo corre: el dueno del proyecto, en SSMS.
   ===================================================================== */

USE EP360_Logistics;
GO

IF COL_LENGTH(N'sync.ColaReplicacion', N'usuario') IS NULL
    ALTER TABLE sync.ColaReplicacion ADD usuario NVARCHAR(150) NULL;
IF COL_LENGTH(N'sync.ColaReplicacion', N'resueltaPor') IS NULL
    ALTER TABLE sync.ColaReplicacion ADD resueltaPor NVARCHAR(150) NULL;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_ColaReplicacion_estado' AND parent_object_id = OBJECT_ID(N'sync.ColaReplicacion'))
    ALTER TABLE sync.ColaReplicacion DROP CONSTRAINT CK_ColaReplicacion_estado;
ALTER TABLE sync.ColaReplicacion ADD CONSTRAINT CK_ColaReplicacion_estado
    CHECK (estado IN (N'Pendiente', N'Aplicada', N'Divergente', N'Omitida'));
GO

CREATE OR ALTER PROCEDURE sync.sp_EncolarReplicacion
    @procedimiento    NVARCHAR(200),
    @parametros       NVARCHAR(MAX),
    @resultadoMaestro NVARCHAR(200) = NULL,
    @usuario          NVARCHAR(150) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sync.ColaReplicacion (procedimiento, parametros, resultadoMaestro, usuario)
    VALUES (@procedimiento, @parametros, @resultadoMaestro, LEFT(@usuario, 150));
    SELECT CAST(SCOPE_IDENTITY() AS BIGINT) AS idCola;
END
GO

-- Operaciones con problema (Pendiente o Divergente), en orden.
CREATE OR ALTER PROCEDURE sync.sp_ObtenerErroresReplicacion
    @maximo INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@maximo) idCola, fechaCreacion, procedimiento, estado, intentos, ultimoError, fechaUltimoIntento, usuario
    FROM sync.ColaReplicacion
    WHERE estado IN (N'Pendiente', N'Divergente')
    ORDER BY idCola;
END
GO

-- Ultimas operaciones ya resueltas (aplicadas en el 11 u omitidas a mano).
CREATE OR ALTER PROCEDURE sync.sp_ObtenerHistorialReplicacion
    @maximo INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@maximo) idCola, fechaCreacion, procedimiento, estado, usuario, resueltaPor, fechaAplicada, ultimoError
    FROM sync.ColaReplicacion
    WHERE estado IN (N'Aplicada', N'Omitida')
    ORDER BY fechaAplicada DESC, idCola DESC;
END
GO

CREATE OR ALTER PROCEDURE sync.sp_ObtenerOperacionReplicacion
    @idCola BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT idCola, fechaCreacion, procedimiento, parametros, resultadoMaestro, estado, intentos, ultimoError,
           fechaUltimoIntento, fechaAplicada, usuario, resueltaPor
    FROM sync.ColaReplicacion
    WHERE idCola = @idCola;
END
GO

-- Deja pasar una operacion Pendiente o Divergente (el administrador ya la reviso). Deja de bloquear la cola.
CREATE OR ALTER PROCEDURE sync.sp_OmitirReplicacion
    @idCola  BIGINT,
    @usuario NVARCHAR(150) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM sync.ColaReplicacion WHERE idCola = @idCola AND estado IN (N'Pendiente', N'Divergente'))
        THROW 50902, N'Solo se puede omitir una operacion pendiente o divergente.', 1;

    UPDATE sync.ColaReplicacion
       SET estado        = N'Omitida',
           parametros    = N'[]',                       -- ya no se va a aplicar: se borra el contenido (puede traer hashes)
           resueltaPor   = LEFT(@usuario, 150),
           fechaAplicada = SYSDATETIME()
     WHERE idCola = @idCola;
END
GO

-- Se redefine para que "Descartar" tambien deje constancia.
CREATE OR ALTER PROCEDURE sync.sp_ReiniciarColaReplicacion
    @usuario NVARCHAR(150) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE sync.ColaReplicacion
       SET estado        = N'Omitida',
           parametros    = N'[]',
           ultimoError   = N'Descartada tras volver a copiar la base al 11.',
           resueltaPor   = LEFT(@usuario, 150),
           fechaAplicada = SYSDATETIME()
     WHERE estado IN (N'Pendiente', N'Divergente');
    SELECT @@ROWCOUNT AS descartadas;
END
GO

-- SOLO EN EL 11. Sube el contador de ids de una tabla hasta el ultimo id usado en el 60 (nunca lo baja).
-- Solo acepta tablas con identidad de los esquemas dir y sync (la validacion consulta el catalogo, no hay SQL libre).
CREATE OR ALTER PROCEDURE sync.sp_AlinearIdentidad
    @tabla              NVARCHAR(200),
    @ultimoUsadoMaestro BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @objectId INT =
        (SELECT t.object_id
         FROM sys.tables t
         WHERE SCHEMA_NAME(t.schema_id) IN (N'dir', N'sync')
           AND SCHEMA_NAME(t.schema_id) + N'.' + t.name = @tabla
           AND t.name <> N'ColaReplicacion'
           AND EXISTS (SELECT 1 FROM sys.identity_columns ic WHERE ic.object_id = t.object_id));
    IF @objectId IS NULL THROW 50903, N'Tabla no valida para alinear contadores.', 1;

    DECLARE @antes BIGINT = (SELECT CAST(last_value AS BIGINT) FROM sys.identity_columns WHERE object_id = @objectId);
    -- Tabla que nunca uso su contador: tras RESEED el primer id es el valor indicado (ultimo + 1).
    -- Tabla con filas: el siguiente id es el valor indicado + 1 (ultimo usado).
    DECLARE @reseed BIGINT = CASE WHEN @antes IS NULL THEN @ultimoUsadoMaestro + 1
                                  WHEN @antes < @ultimoUsadoMaestro THEN @ultimoUsadoMaestro
                                  ELSE NULL END;
    IF @reseed IS NOT NULL
    BEGIN
        DECLARE @sql NVARCHAR(500) = N'DBCC CHECKIDENT (N''' + REPLACE(@tabla, N'''', N'''''') + N''', RESEED, '
                                     + CONVERT(NVARCHAR(30), @reseed) + N') WITH NO_INFOMSGS;';
        EXEC (@sql);
    END

    SELECT @tabla AS tabla, @antes AS antes,
           CASE WHEN @reseed IS NULL THEN @antes ELSE @ultimoUsadoMaestro END AS despues,
           CAST(CASE WHEN @reseed IS NULL THEN 0 ELSE 1 END AS BIT) AS ajustada;
END
GO

PRINT N'10c_ReplicacionDetalle.sql terminado.';
GO
