using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    public class SincronizacionController : BaseAdminController
    {
        private readonly SincronizacionADService _servicio = new SincronizacionADService();

        public ActionResult Index()
        {
            return View(_servicio.Historial());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Sincronizar()
        {
            // Leer todo el dominio y llamar un SP por usuario puede tardar mas que el limite normal de la peticion.
            Server.ScriptTimeout = 900;

            ResultadoSincronizacionModel resultado = null;
            string usuario = User.Identity.Name;
            if (Ejecutar(() => { resultado = _servicio.Ejecutar(usuario); }, null) && resultado != null)
            {
                string resumen = "Sincronización " + resultado.Estado + ": " + resultado.Leidos + " leídos, " +
                                 resultado.Altas + " altas, " + resultado.Actualizaciones + " actualizados, " + resultado.Bajas + " dados de baja, " +
                                 resultado.Omitidos + " omitidos, " + resultado.Errores + " errores. " +
                                 "No cumplieron las reglas: " + resultado.NoCumplenReglas +
                                 (resultado.FaltantesPorRegla.Count == 0 ? "" :
                                    " (les falta: " + string.Join(", ", resultado.FaltantesPorRegla.Select(x => x.Key + " " + x.Value)) + ")") +
                                 ". Cuentas de sistema excluidas: " + resultado.CuentasDeSistema + ".";
                if (resultado.Estado == "Terminada") TempData["Exito"] = resumen; else TempData["Error"] = resumen;
            }
            return RedirectToAction("Index");
        }
    }
}
