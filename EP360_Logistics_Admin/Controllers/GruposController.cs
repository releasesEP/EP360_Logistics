using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.Helpers;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    public class GruposController : BaseAdminController
    {
        private readonly GrupoCuentaService _servicio = new GrupoCuentaService();
        private readonly CuentaService _cuentaServicio = new CuentaService();

        public ActionResult Index(string estado, string buscar, string orden, int pagina = 1, int tam = 25, string formato = null)
        {
            estado = Estados.Normalizar(estado);
            var todos = _servicio.Listar(true);
            var cajas = _servicio.CajasPorGrupo();
            Func<GrupoCuentaModel, int> cajasDe = g => cajas.ContainsKey(g.IdGrupoCuenta) ? cajas[g.IdGrupoCuenta] : 0;

            var filtrados = todos.Where(g => TextoUtil.Contiene(buscar, g.Nombre, g.Folio)).ToList();
            var resultado = ListadoHelpers.Ordenar(Estados.Filtrar(filtrados, estado, g => g.Activo), orden, "nombre",
                new Dictionary<string, Func<GrupoCuentaModel, object>>
                {
                    { "nombre", g => g.Nombre },
                    { "folio", g => g.IdGrupoCuenta },
                    { "cuentas", g => g.TotalCuentas },
                    { "cajas", g => cajasDe(g) }
                }).ToList();

            if (formato == "csv")
                return ListadoHelpers.Csv("grupos", resultado,
                    ("Folio", g => g.Folio), ("Nombre", g => g.Nombre), ("Cuentas", g => g.TotalCuentas),
                    ("Cajas", g => cajasDe(g)), ("Activo", g => g.Activo));

            var encabezado = new EncabezadoListado
            {
                PestanaActiva = estado,
                Pestanas = Estados.Pestanas(filtrados, g => g.Activo),
                Total = resultado.Count,
                Sustantivo = resultado.Count == 1 ? "grupo" : "grupos"
            };
            if (!string.IsNullOrWhiteSpace(buscar)) encabezado.Chips.Add(new ChipFiltro { Parametro = "buscar", Texto = "\"" + buscar + "\"", Icono = "fa-magnifying-glass" });

            var activos = todos.Where(g => g.Activo).ToList();
            ViewBag.TotalActivos = activos.Count;
            ViewBag.CuentasEnGrupos = activos.Sum(g => g.TotalCuentas);
            ViewBag.CajasEnGrupos = activos.Sum(g => cajasDe(g));
            ViewBag.SinCuentas = activos.Count(g => g.TotalCuentas == 0);

            ViewBag.Cajas = cajas;
            ViewBag.Encabezado = encabezado;
            ViewBag.Buscar = buscar;
            return View(new Paginado<GrupoCuentaModel>(resultado, pagina, tam));
        }

        public ActionResult Detalle(int id)
        {
            var grupo = _servicio.ObtenerPorId(id);
            if (grupo == null) return HttpNotFound();

            var cuentas = _servicio.CuentasDelGrupo(id);
            ViewBag.Cuentas = cuentas;
            ViewBag.Cajas = _servicio.CajasDelGrupo(id);
            ViewBag.Contactos = _servicio.ContactosDelGrupo(cuentas);
            ViewBag.CuentasSinGrupo = _servicio.CuentasSinGrupo()
                .Select(c => new SelectListItem { Value = c.IdCuenta.ToString(), Text = c.NombreComercial })
                .ToList();
            return View(grupo);
        }

        public ActionResult Crear()
        {
            return View("Form", new GrupoCuentaModel());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Crear(GrupoCuentaModel modelo)
        {
            if (!ModelState.IsValid) return View("Form", modelo);
            int id;
            return EjecutarConId(() => _servicio.Crear(modelo), "Grupo creado. Ahora agrégale sus cuentas.", out id)
                ? (ActionResult)RedirectToAction("Detalle", new { id })
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
                ? (ActionResult)RedirectToAction("Detalle", new { id = modelo.IdGrupoCuenta })
                : View("Form", modelo);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Desactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Desactivar(id), "Grupo desactivado. Sus cuentas quedaron sin grupo y sus cajas se dieron de baja.");
            return Volver(returnUrl, RedirectToAction("Index"));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Reactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Reactivar(id), "Grupo reactivado. Vuelve a agregarle sus cuentas.");
            return Volver(returnUrl, RedirectToAction("Detalle", new { id }));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult AgregarCuenta(int idGrupoCuenta, int? idCuenta)
        {
            if (idCuenta == null) TempData["Error"] = "Elige una cuenta.";
            else Ejecutar(() => _cuentaServicio.AsignarGrupo(idCuenta.Value, idGrupoCuenta), "Cuenta agregada al grupo.");
            return Redirect(Url.Action("Detalle", new { id = idGrupoCuenta }) + "#cuentas");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult QuitarCuenta(int idGrupoCuenta, int idCuenta)
        {
            Ejecutar(() => _cuentaServicio.QuitarGrupo(idCuenta), "Cuenta quitada del grupo. Sus cajas propias siguen ligadas a ella.");
            return Redirect(Url.Action("Detalle", new { id = idGrupoCuenta }) + "#cuentas");
        }
    }
}
