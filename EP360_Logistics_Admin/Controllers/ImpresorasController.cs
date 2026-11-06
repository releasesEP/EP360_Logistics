using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.Helpers;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    // Catalogo global de impresoras compartidas de documentos (servidor de impresion 192.168.50.4). Se dan de alta y se editan
    // una sola vez aqui; cada portal elige cuales ocupa (EP360 Traceability las usa para imprimir discrepancias e inspecciones).
    public class ImpresorasController : BaseAdminController
    {
        private readonly ImpresoraAdminService _servicio = new ImpresoraAdminService();
        private readonly CatalogoService _catalogos = new CatalogoService();

        public ActionResult Index(string estado, string buscar, string orden, int pagina = 1, int tam = 25)
        {
            estado = Estados.Normalizar(estado);
            var todas = _servicio.Listar();

            var filtradas = todas.Where(i => TextoUtil.Contiene(buscar, i.Nombre) || TextoUtil.Contiene(buscar, i.RutaCompartida)).ToList();
            var resultado = ListadoHelpers.Ordenar(Estados.Filtrar(filtradas, estado, i => i.Activo), orden, "nombre",
                new Dictionary<string, Func<ImpresoraAdminModel, object>>
                {
                    { "nombre", i => i.Nombre },
                    { "ruta", i => i.RutaCompartida },
                    { "sucursal", i => i.Sucursal ?? "\uffff" }
                }).ToList();

            var encabezado = new EncabezadoListado
            {
                PestanaActiva = estado,
                Pestanas = Estados.Pestanas(filtradas, i => i.Activo),
                Total = resultado.Count,
                Sustantivo = resultado.Count == 1 ? "impresora" : "impresoras"
            };
            if (!string.IsNullOrWhiteSpace(buscar)) encabezado.Chips.Add(new ChipFiltro { Parametro = "buscar", Texto = "\"" + buscar + "\"", Icono = "fa-magnifying-glass" });

            ViewBag.Encabezado = encabezado;
            ViewBag.Buscar = buscar;
            return View(new Paginado<ImpresoraAdminModel>(resultado, pagina, tam));
        }

        public ActionResult Crear()
        {
            ViewBag.Sucursales = _catalogos.Sucursales();
            return View("Form", new ImpresoraAdminModel());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Crear(ImpresoraAdminModel modelo)
        {
            modelo.IdImpresora = 0;
            ViewBag.Sucursales = _catalogos.Sucursales(modelo.IdSucursal);
            if (!ModelState.IsValid) return View("Form", modelo);
            return Ejecutar(() => _servicio.Guardar(modelo), "Impresora creada. Ya puede usarse desde los portales que la ocupen.")
                ? (ActionResult)RedirectToAction("Index")
                : View("Form", modelo);
        }

        public ActionResult Editar(int id)
        {
            var modelo = _servicio.ObtenerPorId(id);
            if (modelo == null) return HttpNotFound();
            ViewBag.Sucursales = _catalogos.Sucursales(modelo.IdSucursal);
            return View("Form", modelo);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Editar(ImpresoraAdminModel modelo)
        {
            ViewBag.Sucursales = _catalogos.Sucursales(modelo.IdSucursal);
            if (!ModelState.IsValid) return View("Form", modelo);
            return Ejecutar(() => _servicio.Guardar(modelo), "Impresora actualizada.")
                ? (ActionResult)RedirectToAction("Index")
                : View("Form", modelo);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Desactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Desactivar(id), "Impresora desactivada. Deja de ofrecerse en todos los portales.");
            return Volver(returnUrl, RedirectToAction("Index"));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Reactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Reactivar(id), "Impresora reactivada.");
            return Volver(returnUrl, RedirectToAction("Index"));
        }
    }
}
