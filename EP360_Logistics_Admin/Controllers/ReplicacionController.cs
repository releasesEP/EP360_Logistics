using System.Collections.Generic;
using System.Web.Mvc;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    // Estado de la copia de la global en el servidor 11 (ver DAL/Infraestructura/Replicador.cs).
    public class ReplicacionController : BaseAdminController
    {
        private readonly ReplicacionService _servicio = new ReplicacionService();

        public ActionResult Index()
        {
            return View(_servicio.ObtenerEstado());
        }

        public ActionResult Operacion(long id)
        {
            Models.OperacionReplicacionModel operacion = null;
            if (!Ejecutar(() => { operacion = _servicio.ObtenerOperacion(id); }, null) || operacion == null)
            {
                return RedirectToAction("Index");
            }
            return View(operacion);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Reintentar()
        {
            int aplicadas = 0;
            if (Ejecutar(() => { aplicadas = _servicio.ReintentarAhora(); }, null))
            {
                TempData["Exito"] = aplicadas == 0
                    ? "No se aplicó ninguna operación (no había pendientes, o el 11 no respondió: abre la operación para ver el motivo)."
                    : "Se aplicaron " + aplicadas + " operaciones en el 11.";
            }
            return RedirectToAction("Index");
        }

        // Deja pasar una operacion pendiente o divergente que un administrador ya reviso (deja de bloquear la cola).
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Omitir(long id)
        {
            string usuario = User.Identity.Name;
            if (Ejecutar(() => { _servicio.OmitirOperacion(id, usuario); }, "La operación #" + id + " se omitió. El 11 puede no tener ese cambio."))
            {
                // Al quitar un bloqueo, lo que seguia en la cola puede aplicarse ya.
                Ejecutar(() => { _servicio.ReintentarAhora(); }, null);
            }
            return RedirectToAction("Index");
        }

        // Despues de volver a copiar la base del 60 al 11 (respaldo/restauracion), lo pendiente ya no aplica.
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Descartar()
        {
            int descartadas = 0;
            string usuario = User.Identity.Name;
            if (Ejecutar(() => { descartadas = _servicio.DescartarPendientes(usuario); }, null))
            {
                TempData["Exito"] = "Se descartaron " + descartadas + " operaciones. La cola empieza limpia.";
            }
            return RedirectToAction("Index");
        }

        // Sube los contadores de ids del 11 en las tablas con las mismas filas (no toca datos).
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult AlinearContadores()
        {
            List<string> ajustadas = null;
            if (Ejecutar(() => { ajustadas = _servicio.AlinearContadores(); }, null))
            {
                TempData["Exito"] = ajustadas == null || ajustadas.Count == 0
                    ? "No hubo contadores que alinear."
                    : "Se alinearon los contadores de: " + string.Join(", ", ajustadas) + ".";
            }
            return RedirectToAction("Index");
        }
    }
}
