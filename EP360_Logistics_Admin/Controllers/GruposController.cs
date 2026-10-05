using System.Web.Mvc;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    public class GruposController : BaseAdminController
    {
        private readonly GrupoCuentaService _servicio = new GrupoCuentaService();

        public ActionResult Index(bool inactivos = false)
        {
            ViewBag.Inactivos = inactivos;
            return View(_servicio.Listar(inactivos));
        }

        public ActionResult Crear()
        {
            return View("Form", new GrupoCuentaModel());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Crear(GrupoCuentaModel modelo)
        {
            if (!ModelState.IsValid) return View("Form", modelo);
            return EjecutarConId(() => _servicio.Crear(modelo), "Grupo creado.", out _)
                ? (ActionResult)RedirectToAction("Index")
                : View("Form", modelo);
        }

        public ActionResult Editar(int id)
        {
            var modelo = _servicio.ObtenerPorId(id);
            if (modelo == null) return HttpNotFound();
            return View("Form", modelo);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Editar(GrupoCuentaModel modelo)
        {
            if (!ModelState.IsValid) return View("Form", modelo);
            return Ejecutar(() => _servicio.Actualizar(modelo), "Grupo actualizado.")
                ? (ActionResult)RedirectToAction("Index")
                : View("Form", modelo);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Desactivar(int id)
        {
            Ejecutar(() => _servicio.Desactivar(id), "Grupo desactivado. Sus cuentas quedaron sin grupo.");
            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Reactivar(int id)
        {
            Ejecutar(() => _servicio.Reactivar(id), "Grupo reactivado.");
            return RedirectToAction("Index", new { inactivos = true });
        }
    }
}
