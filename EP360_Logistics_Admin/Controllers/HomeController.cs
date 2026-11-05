using System;
using System.Collections.Generic;
using System.Web.Mvc;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Helpers;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    public class HomeController : Controller
    {
        private readonly InicioService _inicioService = new InicioService();

        public ActionResult Index()
        {
            ViewBag.NombreSaludo = new ActiveDirectoryService().NombreParaSaludo(User.Identity.Name);
            var modelo = _inicioService.Obtener();
            // ?vistaPreviaAlertas=1 llena el modal con alertas de ejemplo, SOLO entrando desde la misma maquina (para ver el diseno).
            ViewBag.Alertas = Request.IsLocal && Request.QueryString["vistaPreviaAlertas"] == "1" ? AlertasDeEjemplo() : Alertas(modelo);
            return View(modelo);
        }

        // Todo lo que fallo o hay que revisar, de lo mas grave a lo menos. Lista vacia = no se muestra el modal.
        private List<AlertaInicioModel> Alertas(InicioModel m)
        {
            var alertas = new List<AlertaInicioModel>();
            var estado = m.Estado;
            if (estado != null && !estado.Conectado)
            {
                alertas.Add(new AlertaInicioModel
                {
                    Critica = true, Area = "Base de datos", Titulo = "No se pudo leer la base global",
                    Detalle = estado.Error, TextoAccion = null
                });
                return alertas;   // sin base, lo demas no se puede revisar
            }

            if (Replicador.Configurada)
            {
                string replica = AlertaReplicacion.Texto();
                if (replica != null)
                    alertas.Add(new AlertaInicioModel
                    {
                        Critica = replica.Contains("divergente"), Area = "Copia en el servidor 11",
                        Titulo = replica.Contains("divergente") ? "La copia al 11 está detenida" : "Hay cambios sin copiar al 11",
                        Detalle = char.ToUpper(replica[0]) + replica.Substring(1) + ". Mientras no se resuelva, los portales que leen del 11 (EP360 Balance) ven datos atrasados.",
                        TextoAccion = "Ver replicación", UrlAccion = Url.Action("Index", "Replicacion")
                    });
            }
            else
            {
                alertas.Add(new AlertaInicioModel
                {
                    Critica = false, Area = "Copia en el servidor 11", Titulo = "La copia al servidor 11 está apagada",
                    Detalle = "Este servidor no tiene configurada la cadena del 11 (CadenaSQLReplica): lo que se guarda solo queda en el 60 y EP360 Balance, que lee del 11, no lo ve.",
                    TextoAccion = "Ver replicación", UrlAccion = Url.Action("Index", "Replicacion")
                });
            }

            var sync = m.UltimaSincronizacion;
            string urlSync = sync != null ? Url.Action("Detalle", "Sincronizacion", new { id = sync.IdSincronizacion }) : Url.Action("Index", "Sincronizacion");
            if (SincronizacionProgramada.UltimoError != null)
                alertas.Add(new AlertaInicioModel
                {
                    Critica = true, Area = "Active Directory", Titulo = "La sincronización automática falló",
                    Detalle = SincronizacionProgramada.UltimoErrorFecha.Value.ToString("dd/MM HH:mm") + ": " + SincronizacionProgramada.UltimoError,
                    TextoAccion = "Ir a Sincronización", UrlAccion = Url.Action("Index", "Sincronizacion")
                });
            if (sync == null)
                alertas.Add(new AlertaInicioModel
                {
                    Area = "Active Directory", Titulo = "Nunca se ha sincronizado con AD",
                    Detalle = "El directorio no tiene a los usuarios del dominio.", TextoAccion = "Ir a Sincronización", UrlAccion = urlSync
                });
            else if (sync.Estado == "Fallida")
                alertas.Add(new AlertaInicioModel
                {
                    Critica = true, Area = "Active Directory", Titulo = "La última sincronización falló",
                    Detalle = "Corrida #" + sync.IdSincronizacion + " del " + sync.FechaInicio.ToString("dd/MM HH:mm") + ". " + (sync.DetalleErrores ?? "No se pudo leer AD."),
                    TextoAccion = "Ver la corrida", UrlAccion = urlSync
                });
            else if (sync.Estado == "ConErrores")
                alertas.Add(new AlertaInicioModel
                {
                    Area = "Active Directory", Titulo = "La última sincronización terminó con " + sync.Errores + " error(es)",
                    Detalle = "Corrida #" + sync.IdSincronizacion + " del " + sync.FechaInicio.ToString("dd/MM HH:mm") + ". Los demás usuarios sí se actualizaron.",
                    TextoAccion = "Ver la corrida", UrlAccion = urlSync
                });
            else
            {
                DateTime? tocaba = SincronizacionProgramada.CorridaQueToca(DateTime.Now);
                if (tocaba.HasValue && sync.FechaInicio < tocaba.Value && DateTime.Now - tocaba.Value > TimeSpan.FromMinutes(30))
                    alertas.Add(new AlertaInicioModel
                    {
                        Area = "Active Directory", Titulo = "No ha corrido la sincronización de las " + tocaba.Value.ToString("HH:mm"),
                        Detalle = "La última fue el " + sync.FechaInicio.ToString("dd/MM HH:mm") + ". La app la recupera sola en cuanto la revisa; si no, usa \"Sincronizar ahora\".",
                        TextoAccion = "Ir a Sincronización", UrlAccion = Url.Action("Index", "Sincronizacion")
                    });
            }

            if (m.ErrorIndicadores != null)
                alertas.Add(new AlertaInicioModel
                {
                    Area = "Indicadores", Titulo = "Algunos indicadores no se pudieron calcular", Detalle = m.ErrorIndicadores
                });

            alertas.Sort((a, b) => b.Critica.CompareTo(a.Critica));
            return alertas;
        }

        private List<AlertaInicioModel> AlertasDeEjemplo()
        {
            return new List<AlertaInicioModel>
            {
                new AlertaInicioModel { Critica = true, Area = "Copia en el servidor 11", Titulo = "La copia al 11 está detenida",
                    Detalle = "1 operación(es) divergente(s) en la copia del 11. Mientras no se resuelva, los portales que leen del 11 (EP360 Balance) ven datos atrasados.",
                    TextoAccion = "Ver replicación", UrlAccion = Url.Action("Index", "Replicacion") },
                new AlertaInicioModel { Critica = true, Area = "Active Directory", Titulo = "La sincronización automática falló",
                    Detalle = "06/11 03:00: The server is not operational.", TextoAccion = "Ir a Sincronización", UrlAccion = Url.Action("Index", "Sincronizacion") },
                new AlertaInicioModel { Area = "Active Directory", Titulo = "La última sincronización terminó con 2 error(es)",
                    Detalle = "Corrida #16 del 06/11 09:12. Los demás usuarios sí se actualizaron.", TextoAccion = "Ver la corrida", UrlAccion = Url.Action("Index", "Sincronizacion") },
                new AlertaInicioModel { Area = "Indicadores", Titulo = "Algunos indicadores no se pudieron calcular",
                    Detalle = "flota: Could not find stored procedure 'flota.sp_ObtenerIndicadores'." }
            };
        }

        // Novedades del sistema (versionamiento), como la pagina de HelpDesk. Se abre desde la version del pie.
        public ActionResult Novedades()
        {
            return View();
        }
    }
}
