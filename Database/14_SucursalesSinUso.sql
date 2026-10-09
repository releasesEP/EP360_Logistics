/* =====================================================================
   14_SucursalesSinUso.sql
   BD global: EP360_Logistics   -> correr en el 60 Y en el 11 (cada uno por separado), DESPUES del 13.
   Correr en LOS DOS ANTES de la primera sincronizacion con AD hecha con el codigo nuevo: la sincronizacion llama a este
   procedimiento y el Replicador lo repite en el 11; si alli no existe, la replicacion se detiene (operacion Divergente).

   Problema (2026-10-08): la sincronizacion con AD CREA una sucursal cuando una persona trae una oficina que no existe
   (dir.sp_ObtenerOCrearSucursal), pero nunca la retira. Si alguien se equivoco al capturar la oficina en AD (ej. "CSR",
   "WH2", "PAN AMERICAN") y luego la corrigio, la sucursal equivocada se quedaba activa, sin personas, ofreciendose en
   todos los portales.

   Crea:
     dir.sp_ReconciliarSucursales   se llama al final de cada sincronizacion con AD:
       1. DESACTIVA las sucursales activas que ya no tienen ninguna persona activa ni cuenta activa.
          Se desactiva y no se borra: otros portales (EP360 y sus recibos) pueden tener ligada esa sucursal, y asi su historial no se pierde.
       2. REACTIVA solo las que ESTE mismo mecanismo desactivo, si despues vuelven a tener personas activas. Una sucursal desactivada
          a mano (con la pantalla de Sucursales) NUNCA se reactiva sola: se sabe cual fue cual por el ultimo renglon de
          sync.BitacoraCambio (detalle 'Sin uso (sincronizacion con AD)').

   No cambia dir.sp_ObtenerOCrearSucursal (una sucursal inactiva se sigue reutilizando por nombre, no se duplica) ni agrega columnas.
   Las personas dadas de baja conservan su sucursal pero NO cuentan como "personas activas", igual que en la pantalla de Sucursales.
   Es IDEMPOTENTE. No devuelve resultado a proposito: el Replicador compara el resultado de cada llamada entre el 60 y el 11, y un
   conteo distinto marcaria la operacion como Divergente. La aplicacion calcula lo que cambio leyendo la lista antes y despues.
   ===================================================================== */

USE EP360_Logistics;
GO

-- sqlcmd crea los procedimientos con QUOTED_IDENTIFIER apagado y dir.Sucursal tiene un indice que lo exige (error 1934 al actualizarla).
-- El valor queda guardado en el procedimiento al crearlo, por eso se fija ANTES del CREATE.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

CREATE OR ALTER PROCEDURE dir.sp_ReconciliarSucursales
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @marca NVARCHAR(100) = N'Sin uso (sincronizacion con AD)';
    DECLARE @cambiadas TABLE (idSucursal INT PRIMARY KEY);

    BEGIN TRAN;

    -- 1. Retira las que ya no tienen a nadie.
    UPDATE s SET activo = 0, fechaModificacion = GETDATE()
    OUTPUT inserted.idSucursal INTO @cambiadas
    FROM dir.Sucursal s
    WHERE s.activo = 1
      AND NOT EXISTS (SELECT 1 FROM dir.Persona p WHERE p.idSucursal = s.idSucursal AND p.activo = 1)
      AND NOT EXISTS (SELECT 1 FROM dir.Cuenta  c WHERE c.idSucursal = s.idSucursal AND c.activo = 1);

    INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle)
    SELECT N'Sucursal', idSucursal, N'Desactivar', @marca FROM @cambiadas;

    DELETE FROM @cambiadas;

    -- 2. Reactiva las que retiro este mecanismo y volvieron a tener personas activas.
    UPDATE s SET activo = 1, fechaModificacion = GETDATE()
    OUTPUT inserted.idSucursal INTO @cambiadas
    FROM dir.Sucursal s
    WHERE s.activo = 0
      AND EXISTS (SELECT 1 FROM dir.Persona p WHERE p.idSucursal = s.idSucursal AND p.activo = 1)
      AND ISNULL((SELECT TOP (1) b.detalle FROM sync.BitacoraCambio b
                  WHERE b.entidad = N'Sucursal' AND b.idEntidad = s.idSucursal AND b.accion IN (N'Desactivar', N'Reactivar')
                  ORDER BY b.idBitacoraCambio DESC), N'') = @marca;

    INSERT INTO sync.BitacoraCambio (entidad, idEntidad, accion, detalle)
    SELECT N'Sucursal', idSucursal, N'Reactivar', N'Volvio a tener personas (sincronizacion con AD)' FROM @cambiadas;

    COMMIT;
END
GO

PRINT N'14_SucursalesSinUso.sql terminado.';
GO
