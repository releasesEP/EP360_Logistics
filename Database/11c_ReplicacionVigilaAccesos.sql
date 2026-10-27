/* =====================================================================
   11c_ReplicacionVigilaAccesos.sql
   BD global: EP360_Logistics   -> correr en el 60 Y en el 11 (cada uno por separado), DESPUES del 11.
   REQUIERE haber corrido antes: 10 y 11.

   La pantalla "Copia en el 11" de la app compara filas y siguiente id de las tablas principales entre el 60 y el 11.
   Este script agrega dir.PersonaPortal (accesos a portales) a esa comparacion. Solo redefine un procedimiento de lectura.

   Es IDEMPOTENTE (CREATE OR ALTER). No toca datos.
   ===================================================================== */

USE EP360_Logistics;
GO

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
    UNION ALL SELECT N'dir.PersonaPortal',        (SELECT COUNT(*) FROM dir.PersonaPortal),        CAST(IDENT_CURRENT(N'dir.PersonaPortal') AS BIGINT)
    UNION ALL SELECT N'seg.CredencialExterna',    (SELECT COUNT(*) FROM seg.CredencialExterna),    CAST(IDENT_CURRENT(N'seg.CredencialExterna') AS BIGINT)
    UNION ALL SELECT N'sync.EquivalenciaEntidad', (SELECT COUNT(*) FROM sync.EquivalenciaEntidad), CAST(IDENT_CURRENT(N'sync.EquivalenciaEntidad') AS BIGINT)
    ORDER BY tabla;
END
GO

PRINT N'11c_ReplicacionVigilaAccesos.sql terminado.';
GO
