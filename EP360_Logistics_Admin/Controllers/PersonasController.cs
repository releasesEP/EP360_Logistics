using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.Helpers;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    public class PersonasController : BaseAdminController
    {
        private readonly PersonaService _servicio = new PersonaService();
        private readonly CuentaService _cuentaServicio = new CuentaService();
        private readonly CatalogoService _catalogos = new CatalogoService();

        public ActionResult Index(string estado, string tipo, int? idDepartamento, int? idSucursal, string buscar,
                                  string orden, int pagina = 1, int tam = 25, string formato = null)
        {
            estado = Estados.Normalizar(estado);
            var todas = _servicio.ListarTodas();

            // Todo menos el estado: de aqui salen los conteos de las pestanas.
            var filtradas = todas.Where(p =>
                    (tipo == null || p.TipoPersona == tipo) &&
                    (idDepartamento == null || p.IdDepartamento == idDepartamento) &&
                    (idSucursal == null || p.IdSucursal == idSucursal) &&
                    TextoUtil.Contiene(buscar, p.NombreCompleto, p.Correo, p.SamAccountName, p.Puesto, p.Departamento, p.Extension))
                .ToList();

            var resultado = ListadoHelpers.Ordenar(Estados.Filtrar(filtradas, estado, p => p.Activo), orden, "nombre",
                new Dictionary<string, Func<PersonaModel, object>>
                {
                    { "nombre", p => p.NombreCompleto },
                    { "tipo", p => p.TipoPersona },
                    { "departamento", p => p.Departamento ?? "￿" },
                    { "sucursal", p => p.Sucursal ?? "￿" }
                }).ToList();

            if (formato == "csv")
                return ListadoHelpers.Csv("personas", resultado,
                    ("Nombre", p => p.NombreCompleto), ("Tipo", p => p.TipoPersona), ("Correo", p => p.Correo),
                    ("Usuario AD", p => p.SamAccountName), ("Puesto", p => p.Puesto), ("Departamento", p => p.Departamento),
                    ("Sucursal", p => p.Sucursal), ("Teléfono", p => p.Telefono), ("Extensión", p => p.Extension), ("Activa", p => p.Activo));

            var departamentos = _catalogos.Departamentos(idDepartamento);
            var sucursales = _catalogos.Sucursales(idSucursal);
            var encabezado = new EncabezadoListado
            {
                PestanaActiva = estado,
                Pestanas = Estados.Pestanas(filtradas, p => p.Activo, "as"),
                Total = resultado.Count,
                Sustantivo = resultado.Count == 1 ? "persona" : "personas"
            };
            if (!string.IsNullOrWhiteSpace(buscar)) encabezado.Chips.Add(new ChipFiltro { Parametro = "buscar", Texto = "\"" + buscar + "\"", Icono = "fa-magnifying-glass" });
            if (tipo != null) encabezado.Chips.Add(new ChipFiltro { Parametro = "tipo", Texto = NombreTipo(tipo), Icono = "fa-user-tag" });
            if (idDepartamento != null) encabezado.Chips.Add(new ChipFiltro { Parametro = "idDepartamento", Texto = departamentos.Where(d => d.Selected).Select(d => d.Text).FirstOrDefault(), Icono = "fa-sitemap" });
            if (idSucursal != null) encabezado.Chips.Add(new ChipFiltro { Parametro = "idSucursal", Texto = sucursales.Where(s => s.Selected).Select(s => s.Text).FirstOrDefault(), Icono = "fa-location-dot" });

            // Indicadores de arriba: sobre todo el directorio, sin filtros.
            ViewBag.TotalActivas = todas.Count(p => p.Activo);
            ViewBag.TotalAD = todas.Count(p => p.Activo && p.TipoPersona == "AD");
            ViewBag.TotalExternos = todas.Count(p => p.Activo && p.TipoPersona == "Externo");
            ViewBag.TotalContactos = todas.Count(p => p.Activo && p.TipoPersona == "Contacto");
            ViewBag.SinDepartamento = todas.Count(p => p.Activo && p.IdDepartamento == null);

            ViewBag.Encabezado = encabezado;
            ViewBag.Departamentos = departamentos;
            ViewBag.Sucursales = sucursales;
            ViewBag.Buscar = buscar;
            ViewBag.Tipo = tipo;
            return View(new Paginado<PersonaModel>(resultado, pagina, tam));
        }

        public static string NombreTipo(string tipo)
        {
            return tipo == "AD" ? "Usuarios de AD" : tipo == "Externo" ? "Usuarios externos" : tipo == "Contacto" ? "Contactos" : tipo;
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
        public ActionResult Desactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Desactivar(id), "Persona desactivada.");
            return Volver(returnUrl, RedirectToAction("Index"));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Reactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Reactivar(id), "Persona reactivada.");
            return Volver(returnUrl, RedirectToAction("Detalle", new { id }));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult FijarPassword(int idPersona, string password, string confirmar)
        {
            Ejecutar(() => _servicio.FijarPassword(idPersona, password, confirmar), "Contraseña guardada. La persona ya puede entrar como usuario externo.");
            return Redirect(Url.Action("Detalle", new { id = idPersona }) + "#acceso");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult VincularCuenta(int idPersona, int? idCuenta, string categoria, string puestoEnCuenta)
        {
            if (idCuenta == null) TempData["Error"] = "Elige una cuenta.";
            else Ejecutar(() => _servicio.Vincular(idPersona, idCuenta.Value, categoria, puestoEnCuenta), "Cuenta vinculada.");
            return Redirect(Url.Action("Detalle", new { id = idPersona }) + "#cuentas");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult DesvincularCuenta(int idPersona, int idCuenta)
        {
            Ejecutar(() => _servicio.Desvincular(idPersona, idCuenta), "Cuenta quitada.");
            return Redirect(Url.Action("Detalle", new { id = idPersona }) + "#cuentas");
        }

        private ActionResult Formulario(PersonaModel modelo)
        {
            ViewBag.Departamentos = _catalogos.Departamentos(modelo.IdDepartamento);
            ViewBag.Sucursales = _catalogos.Sucursales(modelo.IdSucursal);
            return View("Form", modelo);
        }
    }
}
