/* =====================================================================
   11b_MigrarAccesosEP360.sql
   BD global: EP360_Logistics   -> correr en el 60 Y en el 11 (cada uno por separado), DESPUES del 11.
   REQUIERE haber corrido antes: 11_AccesosAPortales.sql.

   Da acceso al portal EP360 a las 164 personas que HOY tienen un usuario activo en EP360 (lista tomada de
   EP360_Traceability.UsuarioEP360 el 2026-10-06), para que el cambio de modelo no le quite el acceso a nadie.
   Se corre igual en los dos servidores (la lista va escrita aqui, no se lee de EP360), asi los ids que se generan
   coinciden entre el 60 y el 11 sin pasar por la replicacion de la app.

   Solo inserta a personas ACTIVAS de tipo AD o Externo que aun no tengan el acceso; correrlo dos veces no duplica.
   ===================================================================== */

USE EP360_Logistics;
GO
SET NOCOUNT ON;

DECLARE @idPortal INT = (SELECT idPortal FROM dir.Portal WHERE clave = N'EP360');
IF @idPortal IS NULL THROW 50054, N'No existe el portal EP360 en dir.Portal.', 1;

DECLARE @personas TABLE (idPersona INT NOT NULL PRIMARY KEY);
DECLARE @lista NVARCHAR(MAX) = N'
    4,5,7,10,16,17,28,30,31,47,51,61,65,67,73,79,80,82,84,87,88,90,92,94,97,98,101,102,105,106,107,108,109,112,114
    ,116,119,120,123,125,126,127,132,133,140,141,142,143,145,146,147,148,151,152,153,157,160,163,164,165,166,169,1
    70,171,173,176,177,178,179,181,182,183,185,186,187,188,189,190,192,193,194,196,198,199,200,201,202,203,204,205
    ,206,207,209,211,212,214,217,218,219,220,222,223,225,226,227,229,230,231,232,233,234,236,237,239,240,241,242,2
    43,244,246,247,248,250,251,252,253,254,259,260,263,265,266,267,268,270,271,272,273,274,275,276,277,280,281,282
    ,283,284,285,287,288,289,290,291,292,293,294,295,296,297,298,299,300,301,302
';
-- Se quitan saltos de linea y espacios antes de separar: la lista esta escrita en varias lineas para poder leerla.
INSERT INTO @personas (idPersona)
SELECT CAST(value AS INT)
FROM STRING_SPLIT(REPLACE(REPLACE(REPLACE(@lista, NCHAR(13), N''), NCHAR(10), N''), N' ', N''), N',')
WHERE value <> N'';

INSERT INTO dir.PersonaPortal (idPersona, idPortal, otorgadoPor)
SELECT x.idPersona, @idPortal, N'migracion EP360 (2026-10-06)'
FROM @personas x
JOIN dir.Persona p ON p.idPersona = x.idPersona AND p.activo = 1 AND p.tipoPersona IN (N'AD', N'Externo')
WHERE NOT EXISTS (SELECT 1 FROM dir.PersonaPortal pp WHERE pp.idPersona = x.idPersona AND pp.idPortal = @idPortal AND pp.activo = 1)
ORDER BY x.idPersona;

PRINT N'Accesos a EP360 insertados: ' + CONVERT(NVARCHAR(10), @@ROWCOUNT);

-- Comprobacion: personas con acceso a EP360 en esta base, y las de la lista que NO quedaron con acceso (inactivas o de otro tipo).
SELECT COUNT(*) AS conAccesoEP360 FROM dir.PersonaPortal WHERE idPortal = @idPortal AND activo = 1;
SELECT x.idPersona, p.nombreCompleto, p.tipoPersona, p.activo
FROM @personas x LEFT JOIN dir.Persona p ON p.idPersona = x.idPersona
WHERE NOT EXISTS (SELECT 1 FROM dir.PersonaPortal pp WHERE pp.idPersona = x.idPersona AND pp.idPortal = @idPortal AND pp.activo = 1);
GO
