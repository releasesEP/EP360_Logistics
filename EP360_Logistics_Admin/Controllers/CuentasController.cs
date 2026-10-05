using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    public class CuentasController : BaseAdminController
    {
        private readonly CuentaService _servicio = new CuentaService();
        private readonly PersonaService _personaServicio = new PersonaService();
        private readonly CatalogoService _catalogos = new CatalogoService();

        public ActionResult Index(string buscar, int? idGrupoCuenta, bool inactivos = false)
        {
            ViewBag.Buscar = buscar;
            ViewBag.IdGrupoCuenta = idGrupoCuenta;
            ViewBag.Inactivos = inactivos;
            ViewBag.Grupos = _catalogos.Grupos(idGrupoCuenta);
            return View(_servicio.Listar(inactivos, idGrupoCuenta, buscar));
        }

        public ActionResult Detalle(int id)
        {
            var cuenta = _servicio.ObtenerPorId(id);
            if (cuenta == null) return HttpNotFound();

            var vinculadas = _servicio.PersonasDeCuenta(id);
            ViewBag.Personas = vinculadas;
            // Solo se ofrecen personas que aun no son contacto de esta cuenta.
            var yaVinculadas = vinculadas.Select(p => p.IdPersona).ToList();
            ViewBag.Vinculables = _personaServicio.ListarVinculables()
                .Where(p => !yaVinculadas.Contains(p.IdPersona))
                .Select(p => new SelectListItem { Value = p.IdPersona.ToString(), Text = p.NombreCompleto + (p.Correo != null ? " (" + p.Correo + ")" : "") })
                .ToList();
            return View(cuenta);
        }

        public ActionResult Crear()
        {
            return Formulario(new CuentaModel());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Crear(CuentaModel modelo)
        {
            if (!ModelState.IsValid) return Formulario(modelo);
            int id;
            return EjecutarConId(() => _servicio.Crear(modelo), "Cuenta creada.", out id)
                ? (ActionResult)RedirectToAction("Detalle", new { id })
                : Formulario(modelo);
        }

        public ActionResult Editar(int id)
        {
            var modelo = _servicio.ObtenerPorId(id);
            if (modelo == null) return HttpNotFound();
            return Formulario(modelo);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Editar(CuentaModel modelo)
        {
            if (!ModelState.IsValid) return Formulario(modelo);
            return Ejecutar(() => _servicio.Actualizar(modelo), "Cuenta actualizada.")
                ? (ActionResult)RedirectToAction("Detalle", new { id = modelo.IdCuenta })
                : Formulario(modelo);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Desactivar(int id)
        {
            Ejecutar(() => _servicio.Desactivar(id), "Cuenta desactivada.");
            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Reactivar(int id)
        {
            Ejecutar(() => _servicio.Reactivar(id), "Cuenta reactivada.");
            return RedirectToAction("Detalle", new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult VincularPersona(int idCuenta, int? idPersona, string categoria, string puestoEnCuenta)
        {
            if (idPersona == null) TempData["Error"] = "Elige una persona.";
            else Ejecutar(() => _personaServicio.Vincular(idPersona.Value, idCuenta, categoria, puestoEnCuenta), "Contacto vinculado a la cuenta.");
            return RedirectToAction("Detalle", new { id = idCuenta });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult DesvincularPersona(int idCuenta, int idPersona)
        {
            Ejecutar(() => _personaServicio.Desvincular(idPersona, idCuenta), "Contacto quitado de la cuenta.");
            return RedirectToAction("Detalle", new { id = idCuenta });
        }

        private ActionResult Formulario(CuentaModel modelo)
        {
            ViewBag.Grupos = _catalogos.Grupos(modelo.IdGrupoCuenta);
            ViewBag.Ciudades = _catalogos.Ciudades(modelo.IdCiudad);
            ViewBag.Sucursales = _catalogos.Sucursales(modelo.IdSucursal);
            return View("Form", modelo);
        }
    }
}
