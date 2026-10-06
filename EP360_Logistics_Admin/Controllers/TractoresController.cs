using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.Helpers;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    public class TractoresController : BaseAdminController
    {
        private readonly TractorService _servicio = new TractorService();
        private readonly CatalogoService _catalogos = new CatalogoService();

        public ActionResult Index(string estado, int? idTransportista, string buscar, string orden, int pagina = 1, int tam = 25, string formato = null)
        {
            estado = Estados.Normalizar(estado);
            var todos = _servicio.Listar(true, null, null);

            var filtrados = todos.Where(t =>
                    (idTransportista == null || t.IdTransportista == idTransportista) &&
                    TextoUtil.Contiene(buscar, t.Economico, t.Placa, t.Vin, t.Marca, t.Transportista))
                .ToList();

            var resultado = ListadoHelpers.Ordenar(Estados.Filtrar(filtrados, estado, t => t.Activo), orden, "economico",
                new Dictionary<string, Func<TractorModel, object>>
                {
                    { "economico", t => t.Economico },
                    { "transportista", t => t.Transportista },
                    { "anio", t => t.Anio ?? 0 }
                }).ToList();

            if (formato == "csv")
                return ListadoHelpers.Csv("tractores", resultado,
                    ("Económico", t => t.Economico), ("Transportista", t => t.Transportista), ("Placa", t => t.Placa),
                    ("VIN", t => t.Vin), ("Año", t => t.Anio), ("Marca", t => t.Marca), ("Activo", t => t.Activo));

            var transportistas = _catalogos.Transportistas(idTransportista);
            var encabezado = new EncabezadoListado
            {
                PestanaActiva = estado,
                Pestanas = Estados.Pestanas(filtrados, t => t.Activo),
                Total = resultado.Count,
                Sustantivo = resultado.Count == 1 ? "tractor" : "tractores"
            };
            if (!string.IsNullOrWhiteSpace(buscar)) encabezado.Chips.Add(new ChipFiltro { Parametro = "buscar", Texto = "\"" + buscar + "\"", Icono = "fa-magnifying-glass" });
            if (idTransportista != null) encabezado.Chips.Add(new ChipFiltro { Parametro = "idTransportista", Texto = transportistas.Where(t => t.Selected).Select(t => t.Text).FirstOrDefault(), Icono = "fa-truck-fast" });

            var activos = todos.Where(t => t.Activo).ToList();
            ViewBag.TotalActivos = activos.Count;
            ViewBag.SinPlaca = activos.Count(t => t.Placa == null);
            ViewBag.SinVin = activos.Count(t => t.Vin == null);
            ViewBag.TransportistasConTractores = activos.Select(t => t.IdTransportista).Distinct().Count();

            ViewBag.Encabezado = encabezado;
            ViewBag.CatalogoTransportistas = transportistas;
            ViewBag.Buscar = buscar;
            return View(new Paginado<TractorModel>(resultado, pagina, tam));
        }

        public ActionResult Crear(int? idTransportista)
        {
            return Formulario(new TractorModel { IdTransportista = idTransportista });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Crear(TractorModel modelo)
        {
            if (!ModelState.IsValid) return Formulario(modelo);
            return Ejecutar(() => _servicio.Crear(modelo), "Tractor creado.")
                ? (ActionResult)Redirect(Url.Action("Detalle", "Transportistas", new { id = modelo.IdTransportista }) + "#pestana-tractores")
                : Formulario(modelo);
        }

        public ActionResult Editar(int id)
        {
            var modelo = _servicio.ObtenerPorId(id);
            if (modelo == null) return HttpNotFound();
            return Formulario(modelo);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Editar(TractorModel modelo)
        {
            if (!ModelState.IsValid) return Formulario(modelo);
            return Ejecutar(() => _servicio.Actualizar(modelo), "Tractor actualizado.")
                ? (ActionResult)Redirect(Url.Action("Detalle", "Transportistas", new { id = modelo.IdTransportista }) + "#pestana-tractores")
                : Formulario(modelo);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Desactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Desactivar(id), "Tractor desactivado.");
            return Volver(returnUrl, AlTransportista(id));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Reactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Reactivar(id), "Tractor reactivado.");
            return Volver(returnUrl, AlTransportista(id));
        }

        // Sin returnUrl se regresa a la ficha del transportista del tractor.
        private ActionResult AlTransportista(int idTractor)
        {
            var tractor = _servicio.ObtenerPorId(idTractor);
            return tractor == null ? (ActionResult)RedirectToAction("Index")
                : Redirect(Url.Action("Detalle", "Transportistas", new { id = tractor.IdTransportista }) + "#pestana-tractores");
        }

        private ActionResult Formulario(TractorModel modelo)
        {
            ViewBag.Transportistas = _catalogos.Transportistas(modelo.IdTransportista);
            return View("Form", modelo);
        }
    }
}
