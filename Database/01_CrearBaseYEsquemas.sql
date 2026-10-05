/* =====================================================================
   01_CrearBaseYEsquemas.sql
   BD global de directorio: EP360_Logistics
   Servidor: 192.168.50.60  (EPL1-EP360SERVER, instancia .\ep360)  -- MAESTRO

   Que hace:
     1. Crea la base EP360_Logistics (si no existe).
     2. Crea los esquemas dir, seg y sync (si no existen).
     3. Crea el catalogo dir.Portal y lo siembra con los portales actuales.
     4. Crea dir.sp_ObtenerEstadoBase (la usa la app de administracion
        para confirmar que esta conectada a la base correcta).

   Es IDEMPOTENTE: se puede correr varias veces sin danar nada.
   NO crea logins ni permisos (eso va en un script aparte, cuando se
   confirme el nombre del pool de IIS de la app de administracion).
   NO toca ninguna otra base (PortalEP, EP360_Traceability, etc.).

   Quien lo corre: el dueno del proyecto, en SSMS, conectado al 60.
   ===================================================================== */

USE master;
GO

IF DB_ID(N'EP360_Logistics') IS NULL
BEGIN
    CREATE DATABASE EP360_Logistics COLLATE SQL_Latin1_General_CP1_CI_AS;
    PRINT 'Base EP360_Logistics creada.';
END
ELSE
    PRINT 'Base EP360_Logistics ya existia.';
GO

-- Lecturas sin bloqueos contra las escrituras (los portales leen mientras la app escribe).
IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'EP360_Logistics') = 0
    ALTER DATABASE EP360_Logistics SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO

USE EP360_Logistics;
GO

IF SCHEMA_ID(N'dir')  IS NULL EXEC (N'CREATE SCHEMA dir');
IF SCHEMA_ID(N'seg')  IS NULL EXEC (N'CREATE SCHEMA seg');
IF SCHEMA_ID(N'sync') IS NULL EXEC (N'CREATE SCHEMA sync');
GO

/* ---------------------------------------------------------------------
   dir.Portal : catalogo de portales que consumen la global.
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dir.Portal', N'U') IS NULL
BEGIN
    CREATE TABLE dir.Portal
    (
        idPortal          INT IDENTITY(1,1) NOT NULL,
        clave             NVARCHAR(30)      NOT NULL,
        nombre            NVARCHAR(100)     NOT NULL,
        activo            BIT               NOT NULL CONSTRAINT DF_Portal_activo DEFAULT (1),
        fechaCreacion     DATETIME          NOT NULL CONSTRAINT DF_Portal_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion DATETIME          NULL,
        CONSTRAINT PK_Portal PRIMARY KEY CLUSTERED (idPortal),
        CONSTRAINT UQ_Portal_clave UNIQUE (clave)
    );
END
GO

INSERT INTO dir.Portal (clave, nombre)
SELECT v.clave, v.nombre
FROM (VALUES
        (N'EP360',    N'EP360 (trazabilidad de procesos)'),
        (N'BALANCE',  N'EP360 Balance (finanzas y cobranza)'),
        (N'HELPDESK', N'Help Desk'),
        (N'INTRANET', N'Intranet EP Logistics')
     ) AS v (clave, nombre)
WHERE NOT EXISTS (SELECT 1 FROM dir.Portal p WHERE p.clave = v.clave);
GO

/* ---------------------------------------------------------------------
   dir.sp_ObtenerEstadoBase : diagnostico de conexion para la app admin.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dir.sp_ObtenerEstadoBase
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        DB_NAME()                                       AS baseDatos,
        @@SERVERNAME                                    AS servidor,
        CAST(SERVERPROPERTY('Edition') AS NVARCHAR(100)) AS edicion,
        GETDATE()                                       AS fechaServidor,
        (SELECT COUNT(*) FROM sys.tables  t WHERE SCHEMA_NAME(t.schema_id) = N'dir')  AS tablasDir,
        (SELECT COUNT(*) FROM sys.tables  t WHERE SCHEMA_NAME(t.schema_id) = N'seg')  AS tablasSeg,
        (SELECT COUNT(*) FROM sys.tables  t WHERE SCHEMA_NAME(t.schema_id) = N'sync') AS tablasSync,
        (SELECT COUNT(*) FROM dir.Portal WHERE activo = 1)                            AS portalesActivos;

    SELECT idPortal, clave, nombre
    FROM dir.Portal
    WHERE activo = 1
    ORDER BY idPortal;
END
GO

PRINT '01_CrearBaseYEsquemas.sql terminado.';
GO
