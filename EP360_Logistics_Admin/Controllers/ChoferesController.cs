using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.Helpers;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    public class ChoferesController : BaseAdminController
    {
        private readonly ChoferService _servicio = new ChoferService();
        private readonly CatalogoService _catalogos = new CatalogoService();

        public static readonly Dictionary<string, string> Situaciones = new Dictionary<string, string>
        {
            { "varios", "Con varios transportistas" },
            { "sintransportista", "Sin transportista activo" },
            { "sinlicencia", "Sin licencia" }
        };

        public ActionResult Index(string estado, int? idTransportista, string situacion, string buscar, string orden,
                                  int pagina = 1, int tam = 25, string formato = null)
        {
            estado = Estados.Normalizar(estado);
            if (situacion != null && !Situaciones.ContainsKey(situacion)) situacion = null;
            var todos = _servicio.Listar(true, null, null);
            var transportistas = _catalogos.Transportistas(idTransportista);
            string nombreTransportista = transportistas.Where(t => t.Selected).Select(t => t.Text).FirstOrDefault();

            var filtrados = todos.Where(c =>
                    (idTransportista == null || c.ListaTransportistas.Contains(nombreTransportista)) &&
                    (situacion == null ||
                     (situacion == "varios" && c.ListaTransportistas.Count > 1) ||
                     (situacion == "sintransportista" && c.ListaTransportistas.Count == 0) ||
                     (situacion == "sinlicencia" && c.Licencia == null)) &&
                    TextoUtil.Contiene(buscar, c.NombreCompleto, c.Licencia, c.Telefono, c.Transportistas))
                .ToList();

            var resultado = ListadoHelpers.Ordenar(Estados.Filtrar(filtrados, estado, c => c.Activo), orden, "nombre",
                new Dictionary<string, Func<ChoferModel, object>>
                {
                    { "nombre", c => c.NombreCompleto },
                    { "licencia", c => c.Licencia ?? "￿" },
                    { "transportistas", c => c.ListaTransportistas.Count }
                }).ToList();

            if (formato == "csv")
                return ListadoHelpers.Csv("choferes", resultado,
                    ("Nombre", c => c.NombreCompleto), ("Licencia", c => c.Licencia), ("Teléfono", c => c.Telefono),
                    ("Transportistas", c => c.Transportistas), ("Activo", c => c.Activo));

            var encabezado = new EncabezadoListado
            {
                PestanaActiva = estado,
                Pestanas = Estados.Pestanas(filtrados, c => c.Activo),
                Total = resultado.Count,
                Sustantivo = resultado.Count == 1 ? "chofer" : "choferes"
            };
            if (!string.IsNullOrWhiteSpace(buscar)) encabezado.Chips.Add(new ChipFiltro { Parametro = "buscar", Texto = "\"" + buscar + "\"", Icono = "fa-magnifying-glass" });
            if (idTransportista != null) encabezado.Chips.Add(new ChipFiltro { Parametro = "idTransportista", Texto = nombreTransportista, Icono = "fa-truck-fast" });
            if (situacion != null) encabezado.Chips.Add(new ChipFiltro { Parametro = "situacion", Texto = Situaciones[situacion], Icono = "fa-circle-info" });

            var activos = todos.Where(c => c.Activo).ToList();
            ViewBag.TotalActivos = activos.Count;
            ViewBag.Varios = activos.Count(c => c.ListaTransportistas.Count > 1);
            ViewBag.SinTransportista = activos.Count(c => c.ListaTransportistas.Count == 0);
            ViewBag.SinLicencia = activos.Count(c => c.Licencia == null);

            ViewBag.Encabezado = encabezado;
            ViewBag.Transportistas = transportistas;
            ViewBag.Buscar = buscar;
            ViewBag.Situacion = situacion;
            return View(new Paginado<ChoferModel>(resultado, pagina, tam));
        }

        public ActionResult Detalle(int id)
        {
            var chofer = _servicio.ObtenerPorId(id);
            if (chofer == null) return HttpNotFound();

            var ligas = _servicio.TransportistasDeChofer(id);
            ViewBag.Ligas = ligas;
            // Solo se ofrecen transportistas con los que el chofer no tiene liga activa.
            var activos = ligas.Where(l => l.Activo).Select(l => l.IdTransportista).ToList();
            ViewBag.Vinculables = _catalogos.Transportistas().Where(t => !activos.Contains(int.Parse(t.Value))).ToList();
            return View(chofer);
        }

        public ActionResult Crear(int? idTransportista)
        {
            return Formulario(new ChoferModel { IdTransportista = idTransportista });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Crear(ChoferModel modelo)
        {
            if (!ModelState.IsValid) return Formulario(modelo);
            int id;
            return EjecutarConId(() => _servicio.Crear(modelo), "Chofer creado.", out id)
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
        public ActionResult Editar(ChoferModel modelo)
        {
            if (!ModelState.IsValid) return Formulario(modelo);
            return Ejecutar(() => _servicio.Actualizar(modelo), "Chofer actualizado.")
                ? ExitoFormulario(Url.Action("Detalle", new { id = modelo.IdChofer }))
                : Formulario(modelo);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Desactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Desactivar(id), "Chofer desactivado. Se cerraron sus ligas con transportistas.");
            return Volver(returnUrl, RedirectToAction("Index"));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Reactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Reactivar(id), "Chofer reactivado. Vuelve a ligarlo con sus transportistas.");
            return Volver(returnUrl, RedirectToAction("Detalle", new { id }));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult VincularTransportista(int idChofer, int? idTransportista)
        {
            if (idTransportista == null) TempData["Error"] = "Elige un transportista.";
            else Ejecutar(() => _servicio.Vincular(idChofer, idTransportista.Value), "Chofer ligado al transportista.");
            return RedirectToAction("Detalle", new { id = idChofer });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult DesvincularTransportista(int idChofer, int idTransportista)
        {
            Ejecutar(() => _servicio.Desvincular(idChofer, idTransportista), "Se cerró la liga con el transportista.");
            return RedirectToAction("Detalle", new { id = idChofer });
        }

        private ActionResult Formulario(ChoferModel modelo)
        {
            ViewBag.Transportistas = _catalogos.Transportistas(modelo.IdTransportista);
            return VistaFormulario("Form", "_Modal", modelo);
        }
    }
}
