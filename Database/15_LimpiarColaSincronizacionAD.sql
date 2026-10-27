/* =====================================================================
   15_LimpiarColaSincronizacionAD.sql
   BD global: EP360_Logistics   -> correr SOLO en el 60 (la cola de replicacion, sync.ColaReplicacion, vive unicamente ahi).
   La corre quien lo plantea (Kevin), no se ejecuta desde el repo ni desde la app.

   Problema (2026-10-09): el Replicador compara, entre el 60 y el 11, el texto del resultado de cada operacion.
   sync.sp_SincronizarUsuarioAD devuelve (idPersona, accion). Hasta el commit 4c21373 el 60 guardaba la columna "accion"
   ("Actualizacion"/"Alta"/"Omitido") en sync.ColaReplicacion.resultadoMaestro, mientras el 11 devolvia la primera columna
   (idPersona, p. ej. '1'): toda actualizacion salia Divergente ("El 11 devolvio '1' y el 60 'Actualizacion'. Los ids ya no
   coinciden.") aunque los ids SI coincidian, y la cola quedaba detenida (una Divergente bloquea todo lo que sigue, ver
   sync.sp_ObtenerPendientesReplicacion).

   El codigo nuevo (SincronizacionDAL.SincronizarUsuario) ya guarda idPersona. Pero las operaciones que YA estaban encoladas
   conservan el texto viejo. Este script lo corrige sin tocar ninguna tabla de datos:
     1) pone resultadoMaestro = NULL en las Pendiente/Divergente de sync.sp_SincronizarUsuarioAD (con NULL el Replicador no
        compara y simplemente aplica la operacion; sirve igual con el codigo viejo o el nuevo desplegado en el servidor);
     2) vuelve a dejar Pendiente la(s) Divergente(s) de ese procedimiento, para que la cola las procese.
   NO usar "Omitir" en lote (se pierden esas actualizaciones en el 11) ni "Alinear contadores" / volver a copiar la base:
   los ids ya coinciden.

   Antes de correrlo comprobar que dir.sp_ReconciliarSucursales (script 14) existe en el 11: las operaciones pendientes la
   incluyen y, si falta alli, la cola se detendria de nuevo.

   Es idempotente: se puede volver a correr y no cambia nada si ya no hay pendientes/divergentes de ese procedimiento.
   ===================================================================== */
USE EP360_Logistics;
GO

-- Estado ANTES
SELECT 'antes' AS momento, estado, COUNT(*) AS n
FROM sync.ColaReplicacion
WHERE procedimiento = N'sync.sp_SincronizarUsuarioAD' AND estado IN (N'Pendiente', N'Divergente')
GROUP BY estado;
GO

SET XACT_ABORT ON;
BEGIN TRAN;

-- 1) Quitar el resultado viejo ("Actualizacion"/"Alta"/"Omitido"): con NULL no se compara.
UPDATE sync.ColaReplicacion
   SET resultadoMaestro = NULL
 WHERE procedimiento = N'sync.sp_SincronizarUsuarioAD'
   AND estado IN (N'Pendiente', N'Divergente');

-- 2) Volver a dejar como Pendiente cada Divergente de ese procedimiento.
DECLARE @idCola BIGINT;
DECLARE divergentes CURSOR LOCAL FAST_FORWARD FOR
    SELECT idCola FROM sync.ColaReplicacion
    WHERE procedimiento = N'sync.sp_SincronizarUsuarioAD' AND estado = N'Divergente'
    ORDER BY idCola;
OPEN divergentes;
FETCH NEXT FROM divergentes INTO @idCola;
WHILE @@FETCH_STATUS = 0
BEGIN
    EXEC sync.sp_MarcarReplicacion @idCola = @idCola, @estado = N'Pendiente', @error = NULL;
    FETCH NEXT FROM divergentes INTO @idCola;
END
CLOSE divergentes;
DEALLOCATE divergentes;

COMMIT TRAN;
GO

-- Estado DESPUES: no debe quedar ninguna Divergente de ese procedimiento (las Pendientes se aplican solas, cada minuto).
SELECT 'despues' AS momento, estado, COUNT(*) AS n FROM sync.ColaReplicacion GROUP BY estado ORDER BY estado;
GO
