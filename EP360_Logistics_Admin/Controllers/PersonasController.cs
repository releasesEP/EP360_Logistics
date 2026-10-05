using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    public class PersonasController : BaseAdminController
    {
        private readonly PersonaService _servicio = new PersonaService();
        private readonly CuentaService _cuentaServicio = new CuentaService();
        private readonly CatalogoService _catalogos = new CatalogoService();

        public ActionResult Index(string tipo, string buscar, bool inactivos = false)
        {
            ViewBag.Tipo = tipo;
            ViewBag.Buscar = buscar;
            ViewBag.Inactivos = inactivos;
            return View(_servicio.Listar(tipo, inactivos, buscar));
        }

        public ActionResult Detalle(int id)
        {
            var persona = _servicio.ObtenerPorId(id);
            if (persona == null) return HttpNotFound();

            // Solo los externos y contactos tienen credencial propia; un usuario de AD entra con Windows.
            ViewBag.Credencial = persona.EsAD ? null : _servicio.ObtenerCredencial(id);

            var vinculadas = _servicio.CuentasDePersona(id);
            ViewBag.Cuentas = vinculadas;
            var yaVinculadas = vinculadas.Select(c => c.IdCuenta).ToList();
            ViewBag.CuentasVinculables = _cuentaServicio.Listar(false, null, null)
                .Where(c => !yaVinculadas.Contains(c.IdCuenta))
                .Select(c => new SelectListItem { Value = c.IdCuenta.ToString(), Text = c.NombreComercial })
                .ToList();
            return View(persona);
        }

        public ActionResult Crear()
        {
            return Formulario(new PersonaModel { TipoPersona = "Contacto" });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Crear(PersonaModel modelo)
        {
            if (modelo.TipoPersona != "Contacto" && modelo.TipoPersona != "Externo")
                ModelState.AddModelError("TipoPersona", "Solo se pueden crear contactos o usuarios externos; los usuarios de AD se sincronizan.");
            if (modelo.TipoPersona == "Externo" && string.IsNullOrWhiteSpace(modelo.Correo))
                ModelState.AddModelError("Correo", "Un usuario externo necesita correo.");
            if (!ModelState.IsValid) return Formulario(modelo);

            int id;
            return EjecutarConId(() => _servicio.Crear(modelo), "Persona creada.", out id)
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
        public ActionResult Editar(PersonaModel modelo)
        {
            // Un usuario de AD solo permite editar telefono, extension y puesto: el resto no viaja en el
            // formulario (campos de solo lectura) y la validacion de nombre no debe bloquear el guardado.
            var original = _servicio.ObtenerPorId(modelo.IdPersona);
            if (original == null) return HttpNotFound();
            modelo.TipoPersona = original.TipoPersona;
            if (original.EsAD)
            {
                modelo.NombreCompleto = original.NombreCompleto;
                modelo.Correo = original.Correo;
                modelo.Puesto = original.Puesto;
                modelo.IdDepartamento = original.IdDepartamento;
                modelo.IdSucursal = original.IdSucursal;
                ModelState.Remove("NombreCompleto");
                ModelState.Remove("Correo");
            }
            else if (original.TipoPersona == "Externo" && string.IsNullOrWhiteSpace(modelo.Correo))
            {
                ModelState.AddModelError("Correo", "Un usuario externo necesita correo.");
            }
            if (!ModelState.IsValid) return Formulario(modelo);

            return Ejecutar(() => _servicio.Actualizar(modelo), "Persona actualizada.")
                ? (ActionResult)RedirectToAction("Detalle", new { id = modelo.IdPersona })
                : Formulario(modelo);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Desactivar(int id)
        {
            Ejecutar(() => _servicio.Desactivar(id), "Persona desactivada.");
            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Reactivar(int id)
        {
            Ejecutar(() => _servicio.Reactivar(id), "Persona reactivada.");
            return RedirectToAction("Detalle", new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult FijarPassword(int idPersona, string password, string confirmar)
        {
            Ejecutar(() => _servicio.FijarPassword(idPersona, password, confirmar), "Contraseña guardada. La persona ya puede entrar como usuario externo.");
            return RedirectToAction("Detalle", new { id = idPersona });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult VincularCuenta(int idPersona, int? idCuenta, string categoria, string puestoEnCuenta)
        {
            if (idCuenta == null) TempData["Error"] = "Elige una cuenta.";
            else Ejecutar(() => _servicio.Vincular(idPersona, idCuenta.Value, categoria, puestoEnCuenta), "Cuenta vinculada.");
            return RedirectToAction("Detalle", new { id = idPersona });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult DesvincularCuenta(int idPersona, int idCuenta)
        {
            Ejecutar(() => _servicio.Desvincular(idPersona, idCuenta), "Cuenta quitada.");
            return RedirectToAction("Detalle", new { id = idPersona });
        }

        private ActionResult Formulario(PersonaModel modelo)
        {
            ViewBag.Departamentos = _catalogos.Departamentos(modelo.IdDepartamento);
            ViewBag.Sucursales = _catalogos.Sucursales(modelo.IdSucursal);
            return View("Form", modelo);
        }
    }
}
