/* =====================================================================
   09_EstadoCredencial.sql
   BD global: EP360_Logistics   (servidor 60, maestro)
   REQUIERE haber corrido antes: 01 a 08.

   La app de administracion de la global ahora permite fijar la contrasena inicial de un usuario
   EXTERNO (seg.sp_FijarPasswordCredencial, del 08). Este SP solo LEE el estado de la credencial para
   mostrarlo en la ficha de la persona (nunca devuelve el hash).

   Es IDEMPOTENTE (CREATE OR ALTER). No inserta datos ni toca otras bases.
   Quien lo corre: el dueno del proyecto, en SSMS, conectado al 60.
   ===================================================================== */

USE EP360_Logistics;
GO

CREATE OR ALTER PROCEDURE seg.sp_ObtenerEstadoCredencial @idPersona INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Sin filas = la persona no tiene credencial. El hash NUNCA sale de aqui.
    SELECT estado,
           CAST(CASE WHEN passwordHash IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS tienePassword,
           fechaCambioPassword,
           intentosFallidos,
           bloqueadoHasta
    FROM seg.CredencialExterna
    WHERE idPersona = @idPersona;
END
GO

PRINT '09_EstadoCredencial.sql terminado.';
GO
