/* =====================================================================
   10_ReplicacionAl11.sql
   BD global: EP360_Logistics   -> correr en el 60 (maestro) Y en el 11 (copia), conectado a cada uno por separado.
   REQUIERE haber corrido antes: 01 a 09.

   La app de administracion escribe PRIMERO en el 60 (maestro) y despues repite la misma operacion en el 11. Si el 11
   no responde, la operacion queda en esta cola (sync.ColaReplicacion) y se reintenta en el MISMO orden, de modo que
   los ids autogenerados sigan siendo iguales en los dos servidores.

   Crea:
     sync.ColaReplicacion                 cola de operaciones por replicar (solo se usa en el 60; en el 11 queda vacia)
     sync.sp_EncolarReplicacion           agrega una operacion
     sync.sp_ObtenerPendientesReplicacion devuelve las pendientes, en orden
     sync.sp_MarcarReplicacion            marca una operacion como Aplicada / Pendiente (con error) / Divergente
     sync.sp_ReiniciarColaReplicacion     descarta lo pendiente despues de volver a copiar la base al 11
     sync.sp_ResumenReplicacion           conteos para la pantalla de estado
     sync.sp_ObtenerConteosReplicacion    filas y siguiente id de las tablas principales (se corre en los DOS servidores y se compara)
     sync.sp_ObtenerErroresReplicacion    ultimas operaciones con problema

   Es IDEMPOTENTE. No toca datos del directorio. Quien lo corre: el dueno del proyecto, en SSMS.
   ===================================================================== */

USE EP360_Logistics;
GO

IF OBJECT_ID(N'sync.ColaReplicacion', N'U') IS NULL
BEGIN
    CREATE TABLE sync.ColaReplicacion
    (
        idCola              BIGINT IDENTITY(1,1) NOT NULL,
        fechaCreacion       DATETIME2(0)   NOT NULL CONSTRAINT DF_ColaReplicacion_fecha DEFAULT SYSDATETIME(),
        procedimiento       NVARCHAR(200)  NOT NULL,
        parametros          NVARCHAR(MAX)  NOT NULL,           -- JSON con nombre, tipo y valor de cada parametro
        resultadoMaestro    NVARCHAR(200)  NULL,               -- id u otro escalar devuelto por el 60 (para comprobar que el 11 da el mismo)
        estado              NVARCHAR(20)   NOT NULL CONSTRAINT DF_ColaReplicacion_estado DEFAULT N'Pendiente',
        intentos            INT            NOT NULL CONSTRAINT DF_ColaReplicacion_intentos DEFAULT 0,
        ultimoError         NVARCHAR(1000) NULL,
        fechaUltimoIntento  DATETIME2(0)   NULL,
        fechaAplicada       DATETIME2(0)   NULL,
        CONSTRAINT PK_ColaReplicacion PRIMARY KEY (idCola),
        CONSTRAINT CK_ColaReplicacion_estado CHECK (estado IN (N'Pendiente', N'Aplicada', N'Divergente'))
    );
    CREATE INDEX IX_ColaReplicacion_estado ON sync.ColaReplicacion (estado, idCola);
END
GO

CREATE OR ALTER PROCEDURE sync.sp_EncolarReplicacion
    @procedimiento    NVARCHAR(200),
    @parametros       NVARCHAR(MAX),
    @resultadoMaestro NVARCHAR(200) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sync.ColaReplicacion (procedimiento, parametros, resultadoMaestro)
    VALUES (@procedimiento, @parametros, @resultadoMaestro);
    SELECT CAST(SCOPE_IDENTITY() AS BIGINT) AS idCola;
END
GO

-- Las pendientes en orden de llegada. Si hay una Divergente, NO se devuelve nada despues de ella: replicar mas adelante
-- con ids distintos en los dos servidores corromperia la copia (ver pantalla de Replicacion).
CREATE OR ALTER PROCEDURE sync.sp_ObtenerPendientesReplicacion
    @maximo INT = 200
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @primeraDivergente BIGINT = (SELECT MIN(idCola) FROM sync.ColaReplicacion WHERE estado = N'Divergente');

    SELECT TOP (@maximo) idCola, procedimiento, parametros, resultadoMaestro, intentos
    FROM sync.ColaReplicacion
    WHERE estado = N'Pendiente'
      AND (@primeraDivergente IS NULL OR idCola < @primeraDivergente)
    ORDER BY idCola;
END
GO

CREATE OR ALTER PROCEDURE sync.sp_MarcarReplicacion
    @idCola BIGINT,
    @estado NVARCHAR(20),
    @error  NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @estado NOT IN (N'Pendiente', N'Aplicada', N'Divergente')
        THROW 50901, N'Estado de replicacion no valido.', 1;

    -- Al quedar Aplicada se borra el contenido de los parametros (pueden incluir hashes de contrasena): ya no hace falta.
    UPDATE sync.ColaReplicacion
       SET estado             = @estado,
           parametros         = CASE WHEN @estado = N'Aplicada' THEN N'[]' ELSE parametros END,
           intentos           = intentos + 1,
           ultimoError        = CASE WHEN @estado = N'Aplicada' THEN NULL ELSE LEFT(@error, 1000) END,
           fechaUltimoIntento = SYSDATETIME(),
           fechaAplicada      = CASE WHEN @estado = N'Aplicada' THEN SYSDATETIME() ELSE fechaAplicada END
     WHERE idCola = @idCola;
END
GO

-- Despues de volver a copiar la base del 60 al 11 (respaldo/restauracion), las operaciones Pendientes o Divergentes ya
-- no tienen sentido: se descartan para que la cola vuelva a empezar limpia. NO se llama desde ninguna otra parte.
CREATE OR ALTER PROCEDURE sync.sp_ReiniciarColaReplicacion
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE sync.ColaReplicacion
       SET estado        = N'Aplicada',
           parametros    = N'[]',
           ultimoError   = N'Descartada tras volver a copiar la base al 11.',
           fechaAplicada = SYSDATETIME()
     WHERE estado <> N'Aplicada';
    SELECT @@ROWCOUNT AS descartadas;
END
GO

CREATE OR ALTER PROCEDURE sync.sp_ResumenReplicacion
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        (SELECT COUNT(*) FROM sync.ColaReplicacion WHERE estado = N'Pendiente')   AS pendientes,
        (SELECT COUNT(*) FROM sync.ColaReplicacion WHERE estado = N'Divergente')  AS divergentes,
        (SELECT COUNT(*) FROM sync.ColaReplicacion WHERE estado = N'Aplicada')    AS aplicadas,
        (SELECT MIN(fechaCreacion) FROM sync.ColaReplicacion WHERE estado = N'Pendiente') AS pendienteMasAntigua,
        (SELECT MAX(fechaAplicada) FROM sync.ColaReplicacion)                     AS ultimaAplicada;
END
GO

CREATE OR ALTER PROCEDURE sync.sp_ObtenerErroresReplicacion
    @maximo INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@maximo) idCola, fechaCreacion, procedimiento, estado, intentos, ultimoError, fechaUltimoIntento
    FROM sync.ColaReplicacion
    WHERE estado <> N'Aplicada'
    ORDER BY idCola;
END
GO

-- Filas y "siguiente id" de cada tabla principal. Se ejecuta en los dos servidores y la app los compara: si las filas o el
-- siguiente id difieren, la copia se desvio (por ejemplo alguien escribio directo en el 60 sin pasar por la app).
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
    UNION ALL SELECT N'seg.CredencialExterna',    (SELECT COUNT(*) FROM seg.CredencialExterna),    CAST(IDENT_CURRENT(N'seg.CredencialExterna') AS BIGINT)
    UNION ALL SELECT N'sync.EquivalenciaEntidad', (SELECT COUNT(*) FROM sync.EquivalenciaEntidad), CAST(IDENT_CURRENT(N'sync.EquivalenciaEntidad') AS BIGINT)
    ORDER BY tabla;
END
GO

PRINT N'10_ReplicacionAl11.sql terminado.';
GO
