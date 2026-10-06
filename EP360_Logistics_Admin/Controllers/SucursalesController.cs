using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.Helpers;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    // Catalogo de sucursales de la global: lo que se ofrece aqui lo usan todos los portales (antes cada portal "inhabilitaba" las suyas).
    public class SucursalesController : BaseAdminController
    {
        private readonly SucursalAdminService _servicio = new SucursalAdminService();

        public ActionResult Index(string estado, string buscar, string orden, int pagina = 1, int tam = 25, string formato = null)
        {
            estado = Estados.Normalizar(estado);
            var todas = _servicio.Listar();

            var filtradas = todas.Where(s => TextoUtil.Contiene(buscar, s.Nombre)).ToList();
            var resultado = ListadoHelpers.Ordenar(Estados.Filtrar(filtradas, estado, s => s.Activo), orden, "nombre",
                new Dictionary<string, Func<SucursalAdminModel, object>>
                {
                    { "nombre", s => s.Nombre },
                    { "personas", s => s.Personas },
                    { "cuentas", s => s.Cuentas }
                }).ToList();

            if (formato == "csv")
                return ListadoHelpers.Csv("sucursales", resultado,
                    ("Nombre", s => s.Nombre), ("Personas", s => s.Personas), ("Cuentas", s => s.Cuentas), ("Activa", s => s.Activo));

            var encabezado = new EncabezadoListado
            {
                PestanaActiva = estado,
                Pestanas = Estados.Pestanas(filtradas, s => s.Activo),
                Total = resultado.Count,
                Sustantivo = resultado.Count == 1 ? "sucursal" : "sucursales"
            };
            if (!string.IsNullOrWhiteSpace(buscar)) encabezado.Chips.Add(new ChipFiltro { Parametro = "buscar", Texto = "\"" + buscar + "\"", Icono = "fa-magnifying-glass" });

            var activas = todas.Where(s => s.Activo).ToList();
            ViewBag.TotalActivas = activas.Count;
            ViewBag.PersonasAsignadas = activas.Sum(s => s.Personas);
            ViewBag.SinUso = activas.Count(s => s.Personas == 0 && s.Cuentas == 0);

            ViewBag.Encabezado = encabezado;
            ViewBag.Buscar = buscar;
            return View(new Paginado<SucursalAdminModel>(resultado, pagina, tam));
        }

        public ActionResult Crear()
        {
            return View("Form", new SucursalAdminModel());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Crear(SucursalAdminModel modelo)
        {
            if (!ModelState.IsValid) return View("Form", modelo);
            int id;
            return EjecutarConId(() => _servicio.Crear(modelo), "Sucursal creada.", out id)
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
        public ActionResult Editar(SucursalAdminModel modelo)
        {
            if (!ModelState.IsValid) return View("Form", modelo);
            return Ejecutar(() => _servicio.Actualizar(modelo), "Sucursal actualizada.")
                ? (ActionResult)RedirectToAction("Index")
                : View("Form", modelo);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Desactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Desactivar(id), "Sucursal desactivada. Deja de ofrecerse como opción; las personas que ya la tienen la conservan.");
            return Volver(returnUrl, RedirectToAction("Index"));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Reactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Reactivar(id), "Sucursal reactivada.");
            return Volver(returnUrl, RedirectToAction("Index"));
        }
    }
}
