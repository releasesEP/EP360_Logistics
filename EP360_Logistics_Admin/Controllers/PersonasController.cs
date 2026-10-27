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
            ViewBag.GruposLigables = _catalogos.Grupos();
            return View(persona);
        }

        // idCuenta / idGrupoCuenta preseleccionan a que cliente se liga la persona desde que se crea;
        // returnUrl regresa a la pantalla de origen (ej. el grupo) en vez del detalle de la persona.
        public ActionResult Crear(int? idCuenta, int? idGrupoCuenta, string returnUrl)
        {
            ViewBag.IdCuentasSel = idCuenta.HasValue ? new[] { idCuenta.Value } : new int[0];
            ViewBag.IdGrupoSel = idGrupoCuenta;
            ViewBag.ReturnUrl = returnUrl;
            return Formulario(new PersonaModel { TipoPersona = "Contacto" });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Crear(PersonaModel modelo, int[] idCuentas, int? idGrupoCuenta, string categoria, string puestoEnCuenta, string returnUrl)
        {
            if (modelo.TipoPersona != "Contacto" && modelo.TipoPersona != "Externo")
                ModelState.AddModelError("TipoPersona", "Solo se pueden crear contactos o usuarios externos; los usuarios de AD se sincronizan.");
            if (modelo.TipoPersona == "Externo" && string.IsNullOrWhiteSpace(modelo.Correo))
                ModelState.AddModelError("Correo", "Un usuario externo necesita correo.");
            if (!ModelState.IsValid)
            {
                ViewBag.IdCuentasSel = idCuentas ?? new int[0];
                ViewBag.IdGrupoSel = idGrupoCuenta;
                ViewBag.ReturnUrl = returnUrl;
                return Formulario(modelo);
            }

            int id;
            if (!EjecutarConId(() => _servicio.Crear(modelo), null, out id))
            {
                ViewBag.IdCuentasSel = idCuentas ?? new int[0];
                ViewBag.IdGrupoSel = idGrupoCuenta;
                ViewBag.ReturnUrl = returnUrl;
                return Formulario(modelo);
            }

            // La persona ya existe: si ligarla falla no se vuelve a mostrar el formulario (duplicaria a la persona),
            // se avisa y se deja en su detalle para completar el vinculo desde ahi.
            var ligadas = new List<int>();
            string mensaje = "Persona creada.";
            bool pidioVinculo = (idCuentas != null && idCuentas.Length > 0) || idGrupoCuenta.HasValue;
            if (pidioVinculo)
            {
                if (Ejecutar(() => { ligadas = _servicio.VincularVarias(id, idCuentas, idGrupoCuenta, categoria, puestoEnCuenta); }, null))
                    mensaje = "Persona creada y ligada a " + ligadas.Count + (ligadas.Count == 1 ? " cuenta." : " cuentas.");
                else
                {
                    TempData["Error"] = "La persona se creó, pero no se pudo ligar a todas las cuentas: " + TempData["Error"] + " Complétalo en la pestaña Cuentas.";
                    return ExitoFormulario(Url.Action("Detalle", new { id }) + "#cuentas");
                }
            }
            Ejecutar(() => { }, mensaje);   // aviso de exito (+ nota de replicacion pendiente si la hay)

            string destino = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Detalle", new { id });
            return ExitoFormulario(destino);
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
                ? ExitoFormulario(Url.Action("Detalle", new { id = modelo.IdPersona }))
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
        public ActionResult VincularCuenta(int idPersona, int[] idCuentas, int? idGrupoCuenta, string categoria, string puestoEnCuenta)
        {
            if ((idCuentas == null || idCuentas.Length == 0) && idGrupoCuenta == null) TempData["Error"] = "Elige al menos una cuenta o un grupo.";
            else
            {
                var ligadas = new List<int>();
                if (Ejecutar(() => { ligadas = _servicio.VincularVarias(idPersona, idCuentas, idGrupoCuenta, categoria, puestoEnCuenta); }, null))
                {
                    string mensaje = ligadas.Count == 0
                        ? "Ya era contacto de todas esas cuentas; no hubo cambios."
                        : "Ligada a " + ligadas.Count + (ligadas.Count == 1 ? " cuenta." : " cuentas.");
                    Ejecutar(() => { }, mensaje);
                }
            }
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
            if (modelo.IdPersona == 0)
            {
                // A que cliente se liga al crearla: cuentas sueltas y/o todas las de un grupo.
                ViewBag.CuentasLigables = _catalogos.Cuentas();
                ViewBag.GruposLigables = _catalogos.Grupos((int?)ViewBag.IdGrupoSel);
            }
            return VistaFormulario("Form", "_Modal", modelo);
        }
    }
}
