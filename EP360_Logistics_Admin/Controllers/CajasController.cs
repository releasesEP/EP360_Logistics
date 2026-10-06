using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.Helpers;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    public class CajasController : BaseAdminController
    {
        private readonly CajaService _servicio = new CajaService();
        private readonly CatalogoService _catalogos = new CatalogoService();

        public ActionResult Index(string tipoPropiedad, string estado, int? idGrupoCuenta, int? idTransportista, bool sinTransportista = false,
                                  string buscar = null, string orden = null, int pagina = 1, int tam = 25, string formato = null)
        {
            estado = Estados.Normalizar(estado);
            if (tipoPropiedad != CajaModel.Cliente && tipoPropiedad != CajaModel.DeTransportista && tipoPropiedad != CajaModel.EP) tipoPropiedad = null;
            var todas = _servicio.ListarTodas();
            // El filtro por grupo incluye las cajas ligadas a las cuentas de ese grupo (igual que flota.sp_ObtenerCajas).
            var cuentasDelGrupo = idGrupoCuenta == null ? new HashSet<int>()
                : new HashSet<int>(new CuentaService().Listar(true, idGrupoCuenta, null).Select(c => c.IdCuenta));

            // Todo menos el tipo de propiedad: de aqui salen los conteos de las pestanas.
            var filtradas = Estados.Filtrar(todas, estado, k => k.Activo).Where(k =>
                    (idGrupoCuenta == null || k.IdGrupoCuenta == idGrupoCuenta || (k.IdCuenta.HasValue && cuentasDelGrupo.Contains(k.IdCuenta.Value))) &&
                    (idTransportista == null || k.IdTransportista == idTransportista) &&
                    (!sinTransportista || k.IdTransportista == null) &&
                    TextoUtil.Contiene(buscar, k.NumeroCaja, k.Placa, k.Vin, k.Propietario, k.Transportista, k.Marca))
                .ToList();

            var resultado = ListadoHelpers.Ordenar(filtradas.Where(k => tipoPropiedad == null || k.TipoPropiedad == tipoPropiedad), orden, "numero",
                new Dictionary<string, Func<CajaModel, object>>
                {
                    { "numero", k => k.NumeroCaja },
                    { "propietario", k => k.Propietario ?? "￿" },
                    { "transportista", k => k.Transportista ?? "￿" },
                    { "tipo", k => k.TipoPropiedad }
                }).ToList();

            if (formato == "csv")
                return ListadoHelpers.Csv("cajas", resultado,
                    ("Número", k => k.NumeroCaja), ("Propiedad", k => k.TipoPropiedad), ("Grupo", k => k.GrupoCuenta), ("Cuenta", k => k.Cuenta),
                    ("Transportista", k => k.Transportista), ("Placa", k => k.Placa), ("VIN", k => k.Vin), ("Año", k => k.Anio),
                    ("Marca", k => k.Marca), ("Pies", k => k.Pies), ("Activa", k => k.Activo));

            var grupos = _catalogos.Grupos(idGrupoCuenta);
            var transportistas = _catalogos.Transportistas(idTransportista);
            var encabezado = new EncabezadoListado
            {
                ParametroPestana = "tipoPropiedad",
                PestanaActiva = tipoPropiedad ?? "",
                Pestanas = new List<PestanaFiltro>
                {
                    new PestanaFiltro { Clave = "", Texto = "Todas", Icono = "fa-list", Conteo = filtradas.Count },
                    new PestanaFiltro { Clave = CajaModel.Cliente, Texto = "De cliente", Icono = "fa-building", Conteo = filtradas.Count(k => k.TipoPropiedad == CajaModel.Cliente), Color = "azul" },
                    new PestanaFiltro { Clave = CajaModel.DeTransportista, Texto = "De transportista", Icono = "fa-truck-fast", Conteo = filtradas.Count(k => k.TipoPropiedad == CajaModel.DeTransportista), Color = "ambar" },
                    new PestanaFiltro { Clave = CajaModel.EP, Texto = "Propias de EP", Icono = "fa-warehouse", Conteo = filtradas.Count(k => k.TipoPropiedad == CajaModel.EP), Color = "morado" }
                },
                Total = resultado.Count,
                Sustantivo = resultado.Count == 1 ? "caja" : "cajas"
            };
            if (!string.IsNullOrWhiteSpace(buscar)) encabezado.Chips.Add(new ChipFiltro { Parametro = "buscar", Texto = "\"" + buscar + "\"", Icono = "fa-magnifying-glass" });
            if (estado != Estados.Activos) encabezado.Chips.Add(new ChipFiltro { Parametro = "estado", Texto = estado == Estados.Inactivos ? "Inactivas" : "Activas e inactivas", Icono = "fa-circle-half-stroke" });
            if (idGrupoCuenta != null) encabezado.Chips.Add(new ChipFiltro { Parametro = "idGrupoCuenta", Texto = grupos.Where(g => g.Selected).Select(g => g.Text).FirstOrDefault(), Icono = "fa-layer-group" });
            if (idTransportista != null) encabezado.Chips.Add(new ChipFiltro { Parametro = "idTransportista", Texto = transportistas.Where(t => t.Selected).Select(t => t.Text).FirstOrDefault(), Icono = "fa-truck-fast" });
            if (sinTransportista) encabezado.Chips.Add(new ChipFiltro { Parametro = "sinTransportista", Texto = "Sin transportista habitual", Icono = "fa-circle-question" });

            // Indicadores de arriba: sobre todas las cajas activas, sin filtros.
            var activas = todas.Where(k => k.Activo).ToList();
            ViewBag.TotalActivas = activas.Count;
            ViewBag.TotalCliente = activas.Count(k => k.TipoPropiedad == CajaModel.Cliente);
            ViewBag.TotalTransportista = activas.Count(k => k.TipoPropiedad == CajaModel.DeTransportista);
            ViewBag.TotalEP = activas.Count(k => k.TipoPropiedad == CajaModel.EP);
            ViewBag.SinTransportista = activas.Count(k => k.IdTransportista == null);

            ViewBag.Encabezado = encabezado;
            ViewBag.Grupos = grupos;
            ViewBag.Transportistas = transportistas;
            ViewBag.Buscar = buscar;
            ViewBag.Estado = estado;
            ViewBag.SinTransportistaFiltro = sinTransportista;
            return View(new Paginado<CajaModel>(resultado, pagina, tam));
        }

        public ActionResult Detalle(int id)
        {
            var caja = _servicio.ObtenerPorId(id);
            if (caja == null) return HttpNotFound();
            return View(caja);
        }

        public ActionResult Crear()
        {
            return Formulario(new CajaModel());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Crear(CajaModel modelo)
        {
            if (!ModelState.IsValid) return Formulario(modelo);
            int id;
            return EjecutarConId(() => _servicio.Crear(modelo), "Caja creada.", out id)
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
        public ActionResult Editar(CajaModel modelo)
        {
            if (!ModelState.IsValid) return Formulario(modelo);
            return Ejecutar(() => _servicio.Actualizar(modelo), "Caja actualizada.")
                ? (ActionResult)RedirectToAction("Detalle", new { id = modelo.IdCaja })
                : Formulario(modelo);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Desactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Desactivar(id), "Caja desactivada.");
            return Volver(returnUrl, RedirectToAction("Detalle", new { id }));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Reactivar(int id, string returnUrl)
        {
            Ejecutar(() => _servicio.Reactivar(id), "Caja reactivada.");
            return Volver(returnUrl, RedirectToAction("Detalle", new { id }));
        }

        private ActionResult Formulario(CajaModel modelo)
        {
            ViewBag.Grupos = _catalogos.Grupos(modelo.IdGrupoCuenta);
            ViewBag.Cuentas = _catalogos.Cuentas(modelo.IdCuenta);
            ViewBag.Transportistas = _catalogos.Transportistas(modelo.IdTransportista);
            return View("Form", modelo);
        }
    }
}
