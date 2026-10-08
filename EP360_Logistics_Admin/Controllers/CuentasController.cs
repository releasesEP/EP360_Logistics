using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.Helpers;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    public class CuentasController : BaseAdminController
    {
        private readonly CuentaService _servicio = new CuentaService();
        private readonly PersonaService _personaServicio = new PersonaService();
        private readonly CatalogoService _catalogos = new CatalogoService();

        // conGrupo: "si" = solo con grupo, "no" = solo sin grupo (idGrupoCuenta gana si viene).
        public ActionResult Index(string estado, int? idGrupoCuenta, string conGrupo, int? idCiudad, int? idSucursal, string buscar,
                                  string orden, int pagina = 1, int tam = 25, string formato = null)
        {
            estado = Estados.Normalizar(estado);
            if (conGrupo != "si" && conGrupo != "no") conGrupo = null;
            var todas = _servicio.Listar(true, null, null);

            var filtradas = todas.Where(c =>
                    (idGrupoCuenta == null || c.IdGrupoCuenta == idGrupoCuenta) &&
                    (idGrupoCuenta != null || conGrupo == null || (conGrupo == "si") == c.IdGrupoCuenta.HasValue) &&
                    (idCiudad == null || c.IdCiudad == idCiudad) &&
                    (idSucursal == null || c.IdSucursal == idSucursal) &&
                    TextoUtil.Contiene(buscar, c.NombreComercial, c.CodigoCuenta, c.RazonSocial, c.Rfc, c.Folio, c.GrupoCuenta))
                .ToList();

            var resultado = ListadoHelpers.Ordenar(Estados.Filtrar(filtradas, estado, c => c.Activo), orden, "nombre",
                new Dictionary<string, Func<CuentaModel, object>>
                {
                    { "nombre", c => c.NombreComercial },
                    { "folio", c => c.IdCuenta },
                    { "grupo", c => c.GrupoCuenta ?? "￿" },
                    { "ciudad", c => c.Ciudad ?? "￿" },
                    { "sucursal", c => c.Sucursal ?? "￿" }
                }).ToList();

            if (formato == "csv")
                return ListadoHelpers.Csv("cuentas", resultado,
                    ("Folio", c => c.Folio), ("Nombre comercial", c => c.NombreComercial), ("Código", c => c.CodigoCuenta),
                    ("Razón social", c => c.RazonSocial), ("RFC", c => c.Rfc), ("Grupo", c => c.GrupoCuenta),
                    ("Ciudad", c => c.Ciudad), ("País", c => c.Pais), ("Sucursal", c => c.Sucursal), ("Activa", c => c.Activo));

            var grupos = _catalogos.Grupos(idGrupoCuenta);
            var ciudades = _catalogos.Ciudades(idCiudad);
            var sucursales = _catalogos.Sucursales(idSucursal);
            var encabezado = new EncabezadoListado
            {
                PestanaActiva = estado,
                Pestanas = Estados.Pestanas(filtradas, c => c.Activo, "as"),
                Total = resultado.Count,
                Sustantivo = resultado.Count == 1 ? "cuenta" : "cuentas"
            };
            if (!string.IsNullOrWhiteSpace(buscar)) encabezado.Chips.Add(new ChipFiltro { Parametro = "buscar", Texto = "\"" + buscar + "\"", Icono = "fa-magnifying-glass" });
            if (idGrupoCuenta != null) encabezado.Chips.Add(new ChipFiltro { Parametro = "idGrupoCuenta", Texto = grupos.Where(g => g.Selected).Select(g => g.Text).FirstOrDefault(), Icono = "fa-layer-group" });
            else if (conGrupo != null) encabezado.Chips.Add(new ChipFiltro { Parametro = "conGrupo", Texto = conGrupo == "si" ? "Con grupo" : "Sin grupo", Icono = "fa-layer-group" });
            if (idCiudad != null) encabezado.Chips.Add(new ChipFiltro { Parametro = "idCiudad", Texto = ciudades.Where(c => c.Selected).Select(c => c.Text).FirstOrDefault(), Icono = "fa-city" });
            if (idSucursal != null) encabezado.Chips.Add(new ChipFiltro { Parametro = "idSucursal", Texto = sucursales.Where(s => s.Selected).Select(s => s.Text).FirstOrDefault(), Icono = "fa-location-dot" });

            // Indicadores de arriba: sobre todas las cuentas, sin filtros.
            var activas = todas.Where(c => c.Activo).ToList();
            ViewBag.TotalActivas = activas.Count;
            ViewBag.ConGrupo = activas.Count(c => c.IdGrupoCuenta.HasValue);
            ViewBag.SinGrupo = activas.Count(c => !c.IdGrupoCuenta.HasValue);
            ViewBag.TotalGrupos = activas.Where(c => c.IdGrupoCuenta.HasValue).Select(c => c.IdGrupoCuenta).Distinct().Count();
            ViewBag.Inactivas = todas.Count(c => !c.Activo);

            ViewBag.Encabezado = encabezado;
            ViewBag.Grupos = grupos;
            ViewBag.Ciudades = ciudades;
            ViewBag.Sucursales = sucursales;
            ViewBag.Buscar = buscar;
            ViewBag.ConGrupoFiltro = conGrupo;
            return View(new Paginado<CuentaModel>(resultado, pagina, tam));
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
            ViewBag.Cajas = _servicio.CajasDeCuenta(cuenta);
            return View(cuenta);
        }

        public ActionResult Crear(int? idGrupoCuenta)
        {
            return Formulario(new CuentaModel { IdGrupoCuenta = idGrupoCuenta });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Crear(CuentaModel modelo)
        {
            if (!ModelState.IsValid) return Formulario(modelo);
            int id;
            return EjecutarConId(() => _servicio.Crear(modelo), "Cuenta creada.", out id)
                ? ExitoFormulario(Url.Action("Detalle", new { id }))
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
                ? ExitoFormulario(Url.Action("Detalle", new { id = modelo.IdCuenta }))
                : Formulario(modelo);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Desactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Desactivar(id), "Cuenta desactivada. Sus contactos y sus cajas se dieron de baja.");
            return Volver(returnUrl, RedirectToAction("Index"));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Reactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Reactivar(id), "Cuenta reactivada. Sus contactos y cajas se reactivan uno por uno.");
            return Volver(returnUrl, RedirectToAction("Detalle", new { id }));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult VincularPersona(int idCuenta, int? idPersona, string categoria, string puestoEnCuenta)
        {
            if (idPersona == null) TempData["Error"] = "Elige una persona.";
            else Ejecutar(() => _personaServicio.Vincular(idPersona.Value, idCuenta, categoria, puestoEnCuenta), "Contacto ligado a la cuenta.");
            return Redirect(Url.Action("Detalle", new { id = idCuenta }) + "#contactos");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult DesvincularPersona(int idCuenta, int idPersona)
        {
            Ejecutar(() => _personaServicio.Desvincular(idPersona, idCuenta), "Contacto quitado de la cuenta.");
            return Redirect(Url.Action("Detalle", new { id = idCuenta }) + "#contactos");
        }

        private ActionResult Formulario(CuentaModel modelo)
        {
            ViewBag.Grupos = _catalogos.Grupos(modelo.IdGrupoCuenta);
            ViewBag.Ciudades = _catalogos.Ciudades(modelo.IdCiudad);
            ViewBag.Sucursales = _catalogos.Sucursales(modelo.IdSucursal);
            return VistaFormulario("Form", "_Modal", modelo);
        }
    }
}
