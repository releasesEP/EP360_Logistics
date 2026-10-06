/* =====================================================================
   12_PermisosProduccion.sql
   BD global: EP360_Logistics   -> correr en el 60 (donde corre EP360 en produccion), ANTES de migrar PortalEP.
   REQUIERE haber corrido antes: 01 a 11c.

   Hoy NINGUNA identidad de IIS tiene usuario en la global: solo la tienen personas (el dueno del proyecto). En produccion, las
   vistas y procedimientos de PortalEP leen y escriben en EP360_Logistics (consultas entre bases en la misma instancia), asi que el
   Application Pool del portal y el de Ep360Api necesitan permiso AQUI. Sin esto, el portal falla al abrir cualquier pantalla con
   "The server principal ... is not able to access the database EP360_Logistics under the current security context".

   Permisos MINIMOS (no se da db_owner):
     IIS AppPool\ep360     (portal)    SELECT en los esquemas dir, seg y sync + EXECUTE en dir y seg
                                       (lee las vistas Usuario/Cliente/Sucursal/...; llama a dir.sp_* y seg.sp_* para escribir)
     IIS APPPOOL\ep360api  (Ep360Api)  SOLO SELECT en dir, seg y sync (sus consultas leen las vistas; no llama SPs de la global)

   Es IDEMPOTENTE. Ambos logins ya existen en el servidor (se comprobo con solo lectura). No toca datos.
   Si el nombre de algun pool cambia, ajustar aqui. Para revertir: REVOKE y DROP USER de estos dos usuarios.
   ===================================================================== */

USE EP360_Logistics;
GO
SET NOCOUNT ON;

IF SUSER_ID(N'IIS AppPool\ep360') IS NULL
BEGIN
    RAISERROR(N'No existe el login IIS AppPool\ep360 en este servidor. NO se hizo nada.', 16, 1);
    SET NOEXEC ON;
END
GO
IF SUSER_ID(N'IIS APPPOOL\ep360api') IS NULL
BEGIN
    RAISERROR(N'No existe el login IIS APPPOOL\ep360api en este servidor. NO se hizo nada.', 16, 1);
    SET NOEXEC ON;
END
GO

IF USER_ID(N'IIS AppPool\ep360') IS NULL    CREATE USER [IIS AppPool\ep360]    FOR LOGIN [IIS AppPool\ep360];
IF USER_ID(N'IIS APPPOOL\ep360api') IS NULL CREATE USER [IIS APPPOOL\ep360api] FOR LOGIN [IIS APPPOOL\ep360api];
GO

-- Portal: lee y escribe a traves de procedimientos.
GRANT SELECT  ON SCHEMA::dir  TO [IIS AppPool\ep360];
GRANT SELECT  ON SCHEMA::seg  TO [IIS AppPool\ep360];
GRANT SELECT  ON SCHEMA::sync TO [IIS AppPool\ep360];
GRANT EXECUTE ON SCHEMA::dir  TO [IIS AppPool\ep360];
GRANT EXECUTE ON SCHEMA::seg  TO [IIS AppPool\ep360];

-- Ep360Api: solo lectura (las vistas de PortalEP).
GRANT SELECT  ON SCHEMA::dir  TO [IIS APPPOOL\ep360api];
GRANT SELECT  ON SCHEMA::seg  TO [IIS APPPOOL\ep360api];
GRANT SELECT  ON SCHEMA::sync TO [IIS APPPOOL\ep360api];
GO

SET NOEXEC OFF;
GO

-- Comprobacion
SELECT dp.name AS usuario, pe.permission_name, pe.state_desc, ISNULL(SCHEMA_NAME(pe.major_id), N'(base)') AS esquema
FROM sys.database_principals dp
JOIN sys.database_permissions pe ON pe.grantee_principal_id = dp.principal_id AND pe.class = 3
WHERE dp.name IN (N'IIS AppPool\ep360', N'IIS APPPOOL\ep360api')
ORDER BY dp.name, esquema, pe.permission_name;
GO
PRINT N'12_PermisosProduccion.sql terminado.';
GO
