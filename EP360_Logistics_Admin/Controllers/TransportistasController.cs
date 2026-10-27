using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.Helpers;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    public class TransportistasController : BaseAdminController
    {
        private readonly TransportistaService _servicio = new TransportistaService();
        private readonly TractorService _tractorServicio = new TractorService();
        private readonly ChoferService _choferServicio = new ChoferService();
        private readonly CajaService _cajaServicio = new CajaService();
        private readonly CatalogoService _catalogos = new CatalogoService();

        public static readonly Dictionary<string, string> Actividades = new Dictionary<string, string>
        {
            { "conchoferes", "Con choferes" },
            { "sinchoferes", "Sin choferes" },
            { "sintractores", "Sin tractores" },
            { "concajas", "Con cajas" }
        };

        public ActionResult Index(string estado, string actividad, string buscar, string orden, int pagina = 1, int tam = 25, string formato = null)
        {
            estado = Estados.Normalizar(estado);
            if (actividad != null && !Actividades.ContainsKey(actividad)) actividad = null;
            var todos = _servicio.Listar(true, null);

            var filtrados = todos.Where(t =>
                    (actividad == null ||
                     (actividad == "conchoferes" && t.TotalChoferes > 0) ||
                     (actividad == "sinchoferes" && t.TotalChoferes == 0) ||
                     (actividad == "sintractores" && t.TotalTractores == 0) ||
                     (actividad == "concajas" && t.TotalCajas > 0)) &&
                    TextoUtil.Contiene(buscar, t.Nombre, t.RazonSocial, t.Rfc, t.Folio, t.Cuenta))
                .ToList();

            var resultado = ListadoHelpers.Ordenar(Estados.Filtrar(filtrados, estado, t => t.Activo), orden, "nombre",
                new Dictionary<string, Func<TransportistaModel, object>>
                {
                    { "nombre", t => t.Nombre },
                    { "choferes", t => t.TotalChoferes },
                    { "tractores", t => t.TotalTractores },
                    { "cajas", t => t.TotalCajas },
                    { "folio", t => t.IdTransportista }
                }).ToList();

            if (formato == "csv")
                return ListadoHelpers.Csv("transportistas", resultado,
                    ("Folio", t => t.Folio), ("Nombre", t => t.Nombre), ("Razón social", t => t.RazonSocial), ("RFC", t => t.Rfc),
                    ("Choferes", t => t.TotalChoferes), ("Tractores", t => t.TotalTractores), ("Cajas", t => t.TotalCajas),
                    ("Cuenta del directorio", t => t.Cuenta), ("Activo", t => t.Activo));

            var encabezado = new EncabezadoListado
            {
                PestanaActiva = estado,
                Pestanas = Estados.Pestanas(filtrados, t => t.Activo),
                Total = resultado.Count,
                Sustantivo = resultado.Count == 1 ? "transportista" : "transportistas"
            };
            if (!string.IsNullOrWhiteSpace(buscar)) encabezado.Chips.Add(new ChipFiltro { Parametro = "buscar", Texto = "\"" + buscar + "\"", Icono = "fa-magnifying-glass" });
            if (actividad != null) encabezado.Chips.Add(new ChipFiltro { Parametro = "actividad", Texto = Actividades[actividad], Icono = "fa-chart-simple" });

            var activos = todos.Where(t => t.Activo).ToList();
            ViewBag.TotalActivos = activos.Count;
            ViewBag.ConChoferes = activos.Count(t => t.TotalChoferes > 0);
            ViewBag.TotalTractores = activos.Sum(t => t.TotalTractores);
            ViewBag.TotalChoferes = activos.Sum(t => t.TotalChoferes);

            ViewBag.Encabezado = encabezado;
            ViewBag.Buscar = buscar;
            ViewBag.Actividad = actividad;
            return View(new Paginado<TransportistaModel>(resultado, pagina, tam));
        }

        public ActionResult Detalle(int id)
        {
            var transportista = _servicio.ObtenerPorId(id);
            if (transportista == null) return HttpNotFound();

            var choferes = _choferServicio.Listar(false, id, null);
            ViewBag.Tractores = _tractorServicio.Listar(true, id, null);
            ViewBag.Choferes = choferes;
            ViewBag.Cajas = _cajaServicio.DeTransportista(id);
            // Para "ligar chofer existente": choferes activos que todavia no trabajan con esta linea.
            var ligados = new HashSet<int>(choferes.Select(c => c.IdChofer));
            ViewBag.ChoferesVinculables = _choferServicio.Listar(false, null, null)
                .Where(c => !ligados.Contains(c.IdChofer))
                .Select(c => new SelectListItem { Value = c.IdChofer.ToString(), Text = c.NombreCompleto + (c.Licencia != null ? " · " + c.Licencia : "") + (c.Transportistas != null ? " (" + c.Transportistas + ")" : "") })
                .ToList();
            return View(transportista);
        }

        public ActionResult Crear()
        {
            return Formulario(new TransportistaModel());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Crear(TransportistaModel modelo)
        {
            if (!ModelState.IsValid) return Formulario(modelo);
            int id;
            return EjecutarConId(() => _servicio.Crear(modelo), "Transportista creado.", out id)
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
        public ActionResult Editar(TransportistaModel modelo)
        {
            if (!ModelState.IsValid) return Formulario(modelo);
            return Ejecutar(() => _servicio.Actualizar(modelo), "Transportista actualizado.")
                ? ExitoFormulario(Url.Action("Detalle", new { id = modelo.IdTransportista }))
                : Formulario(modelo);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Desactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Desactivar(id), "Transportista desactivado. Se dieron de baja sus tractores y cajas propias, y se cerraron sus ligas con choferes.");
            return Volver(returnUrl, RedirectToAction("Index"));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Reactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Reactivar(id), "Transportista reactivado. Sus tractores, cajas y choferes se reactivan uno por uno.");
            return Volver(returnUrl, RedirectToAction("Detalle", new { id }));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult VincularChofer(int idTransportista, int? idChofer)
        {
            if (idChofer == null) TempData["Error"] = "Elige un chofer.";
            else Ejecutar(() => _choferServicio.Vincular(idChofer.Value, idTransportista), "Chofer ligado al transportista.");
            return Redirect(Url.Action("Detalle", new { id = idTransportista }) + "#pestana-choferes");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult DesvincularChofer(int idTransportista, int idChofer)
        {
            Ejecutar(() => _choferServicio.Desvincular(idChofer, idTransportista), "Se cerró la liga del chofer con este transportista.");
            return Redirect(Url.Action("Detalle", new { id = idTransportista }) + "#pestana-choferes");
        }

        private ActionResult Formulario(TransportistaModel modelo)
        {
            ViewBag.Cuentas = _catalogos.Cuentas(modelo.IdCuenta);
            return VistaFormulario("Form", "_Modal", modelo);
        }
    }
}
