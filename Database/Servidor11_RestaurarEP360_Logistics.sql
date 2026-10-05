-- =============================================================================================================================
-- Servidor11_RestaurarEP360_Logistics.sql
-- Se corre en el SERVIDOR 11 (192.168.50.11, SQL Express), en SSMS conectado al 11, ventana nueva sobre master.
--
-- Crea la copia local de la base global EP360_Logistics a partir del respaldo hecho en el 60:
--   EP360_Logistics_para_11_20261005.bak   (respaldo COPY_ONLY, verificado en el 60 el 2026-10-05, ~10 MB)
--
-- ANTES: copiar el .bak del 60 al 11. Origen (en el 60):
--   C:\Program Files\Microsoft SQL Server\MSSQL16.EP360\MSSQL\Backup\EP360_Logistics_para_11_20261005.bak
-- Destino sugerido (en el 11): una carpeta que el servicio de SQL Server pueda leer, por ejemplo C:\Respaldos\ (crearla).
-- Cambia @ruta abajo si lo dejas en otro lugar.
--
-- Este script NO toca ninguna otra base del 11. Si ya existe una EP360_Logistics en el 11 se detiene sin hacer nada
-- (para no pisarla por accidente). Los archivos .mdf/.ldf se crean en las carpetas por defecto del 11.
-- =============================================================================================================================
USE master;
GO
SET NOCOUNT ON;

DECLARE @ruta NVARCHAR(400) = N'C:\Respaldos\EP360_Logistics_para_11_20261005.bak';   -- <== AJUSTAR si hace falta

IF DB_ID(N'EP360_Logistics') IS NOT NULL
BEGIN
    PRINT N'YA EXISTE EP360_Logistics en este servidor. No se hizo nada.';
    RETURN;
END

DECLARE @datos NVARCHAR(400) = CONVERT(NVARCHAR(400), SERVERPROPERTY('InstanceDefaultDataPath'));
DECLARE @log   NVARCHAR(400) = CONVERT(NVARCHAR(400), SERVERPROPERTY('InstanceDefaultLogPath'));
PRINT N'Datos: ' + ISNULL(@datos, N'(null)') + N'   Log: ' + ISNULL(@log, N'(null)');

-- 1. Revisar el respaldo antes de restaurar (si falla aqui, el archivo no llego completo)
DECLARE @sqlVerif NVARCHAR(MAX) = N'RESTORE VERIFYONLY FROM DISK = N''' + @ruta + N''' WITH CHECKSUM;';
EXEC (@sqlVerif);

-- 2. Restaurar con los archivos en las carpetas por defecto del 11
DECLARE @sql NVARCHAR(MAX) =
      N'RESTORE DATABASE [EP360_Logistics] FROM DISK = N''' + @ruta + N''' WITH CHECKSUM, RECOVERY, STATS = 25, '
    + N'MOVE N''EP360_Logistics''     TO N''' + @datos + N'EP360_Logistics.mdf'', '
    + N'MOVE N''EP360_Logistics_log'' TO N''' + @log   + N'EP360_Logistics_log.ldf'';';
EXEC (@sql);
GO

-- 3. Comprobacion (debe dar los mismos conteos que en el 60)
USE EP360_Logistics;
GO
SELECT (SELECT COUNT(*) FROM dir.Persona)       AS personas,
       (SELECT COUNT(*) FROM dir.Cuenta)        AS cuentas,
       (SELECT COUNT(*) FROM dir.GrupoCuenta)   AS grupos,
       (SELECT COUNT(*) FROM dir.Departamento)  AS departamentos,
       (SELECT COUNT(*) FROM dir.UsuarioAD)     AS usuariosAD;
GO
