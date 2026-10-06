/* =====================================================================
   12b_PermisosAppGlobal.sql
   BD global: EP360_Logistics   -> correr en el 60 Y en el 11 (cada uno por separado), conectado como administrador del servidor.
   REQUIERE haber corrido antes: 01 a 13.

   Da a la app de administracion global (EP360_Logistics_Admin) el acceso que necesita a la base global. Esta app SI escribe (altas,
   accesos, sucursales, sincronizacion con AD, replicacion), pero siempre a traves de procedimientos: no se le da db_owner.

   QUE PONER EN @cuenta (la identidad con la que la app entra a SQL Server):
     * En el 60 (la app corre aqui):  el Application Pool del sitio, por ejemplo  IIS APPPOOL\ep360logistics
       (el pool debe existir ya en IIS: SQL Server solo crea el login si Windows conoce la cuenta).
     * En el 11 (donde la app escribe la copia):  la CUENTA DE MAQUINA del servidor 60, no el pool. Un pool con ApplicationPoolIdentity
       sale a la red como  DOMINIO\NOMBRE_DEL_SERVIDOR$  (con el signo $ al final), por ejemplo  EPLOGISTICS\EPL1-EP360SERVER$.

   Es IDEMPOTENTE: crea el login y el usuario solo si faltan, y repetir los GRANT no cambia nada.
   Es UN SOLO BLOQUE (sin GO), asi que el valor de @cuenta aplica a todo.
   ===================================================================== */

SET NOCOUNT ON;

DECLARE @cuenta NVARCHAR(256) = N'IIS APPPOOL\ep360logistics';     -- <== CAMBIAR segun el servidor (ver arriba)

IF DB_ID(N'EP360_Logistics') IS NULL
    THROW 50099, N'No existe la base EP360_Logistics en este servidor. NO se hizo nada.', 1;

-- 1. Login de Windows (a nivel servidor)
IF SUSER_ID(@cuenta) IS NULL
BEGIN
    DECLARE @sqlLogin NVARCHAR(MAX) = N'CREATE LOGIN ' + QUOTENAME(@cuenta) + N' FROM WINDOWS;';
    EXEC (@sqlLogin);
    PRINT N'Login creado: ' + @cuenta;
END
ELSE PRINT N'El login ya existia: ' + @cuenta;

-- 2. Usuario en la base global y permisos minimos (esquemas dir, seg, sync y, si existe, flota)
DECLARE @q NVARCHAR(256) = QUOTENAME(@cuenta);
DECLARE @sql NVARCHAR(MAX) = N'
USE EP360_Logistics;
IF USER_ID(' + QUOTENAME(@cuenta, '''') + N') IS NULL CREATE USER ' + @q + N' FOR LOGIN ' + @q + N';
GRANT SELECT, EXECUTE ON SCHEMA::dir  TO ' + @q + N';
GRANT SELECT, EXECUTE ON SCHEMA::seg  TO ' + @q + N';
GRANT SELECT, EXECUTE ON SCHEMA::sync TO ' + @q + N';
IF SCHEMA_ID(N''flota'') IS NOT NULL GRANT SELECT, EXECUTE ON SCHEMA::flota TO ' + @q + N';
';
EXEC (@sql);
PRINT N'Permisos otorgados a ' + @cuenta + N' en EP360_Logistics.';

-- 3. Comprobacion
SET @sql = N'
USE EP360_Logistics;
SELECT dp.name AS usuario, pe.permission_name, pe.state_desc, SCHEMA_NAME(pe.major_id) AS esquema
FROM sys.database_principals dp
JOIN sys.database_permissions pe ON pe.grantee_principal_id = dp.principal_id AND pe.class = 3
WHERE dp.name = ' + QUOTENAME(@cuenta, '''') + N'
ORDER BY esquema, pe.permission_name;';
EXEC (@sql);
