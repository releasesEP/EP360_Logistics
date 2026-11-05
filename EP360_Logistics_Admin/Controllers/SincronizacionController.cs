using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.Helpers;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    public class SincronizacionController : BaseAdminController
    {
        private readonly SincronizacionADService _servicio = new SincronizacionADService();

        // ?formato=xlsx descarga TODAS las corridas (no solo las 20 de la pantalla) con el formato de Excel del portal.
        public ActionResult Index(string formato)
        {
            if (formato == "xlsx") return ExcelHistorial();
            return View(_servicio.Historial());
        }

        private ActionResult ExcelHistorial()
        {
            var corridas = _servicio.Historial(5000);
            Func<SincronizacionADModel, string> origen = s => s.EjecutadoPor != null && s.EjecutadoPor.StartsWith("Autom") ? "Automática"
                : (s.EjecutadoPor != null && s.EjecutadoPor.Contains("\\") ? s.EjecutadoPor.Substring(s.EjecutadoPor.IndexOf('\\') + 1) : s.EjecutadoPor);
            Func<string, string> estado = e => e == "Terminada" ? "Terminada" : e == "ConErrores" ? "Con errores" : e == "EnCurso" ? "En curso" : e;
            // Las corridas anteriores al registro detallado no guardaban bajas (lo que se deducia contaba tambien a los que ya estaban
            // inactivos), asi que en el Excel van vacias y no se suman.
            Func<SincronizacionADModel, bool> conDetalle = s => s.TieneDetalle || s.Bajas + s.Ausentes + s.SinCambios > 0;
            var ultima = corridas.FirstOrDefault();

            var tarjetas = new List<ExcelEP360Helper.Tarjeta>
            {
                new ExcelEP360Helper.Tarjeta { Etiqueta = "Corridas", Valor = corridas.Count,
                    Nota = corridas.Count == 0 ? "" : "desde el " + corridas.Last().FechaInicio.ToString("dd/MM/yyyy") },
                new ExcelEP360Helper.Tarjeta { Etiqueta = "Última corrida", Valor = ultima == null ? "—" : ultima.FechaInicio.ToString("dd/MM HH:mm"),
                    Nota = ultima == null ? "" : estado(ultima.Estado) + " · " + origen(ultima) },
                new ExcelEP360Helper.Tarjeta { Etiqueta = "Altas", Valor = corridas.Sum(s => s.Altas), Nota = "personas nuevas en total" },
                new ExcelEP360Helper.Tarjeta { Etiqueta = "Bajas", Valor = corridas.Where(conDetalle).Sum(s => s.Bajas + s.Ausentes),
                    Nota = corridas.Any(conDetalle) ? corridas.Sum(s => s.Ausentes) + " por ya no existir en AD" : "se cuentan desde el registro detallado" },
                new ExcelEP360Helper.Tarjeta { Etiqueta = "Con errores", Valor = corridas.Count(s => s.Errores > 0),
                    Nota = "corridas con algún error", Color = corridas.Any(s => s.Estado == "Fallida") ? "rojo" : null }
            };
            var tablas = new List<ExcelEP360Helper.TablaConteo>
            {
                new ExcelEP360Helper.TablaConteo { Titulo = "Corridas por estado",
                    Renglones = corridas.GroupBy(s => estado(s.Estado)).Select(g => new KeyValuePair<string, int>(g.Key, g.Count())).OrderByDescending(k => k.Value).ToList() },
                new ExcelEP360Helper.TablaConteo { Titulo = "Quién las corrió",
                    Renglones = corridas.GroupBy(origen).Select(g => new KeyValuePair<string, int>(g.Key ?? "—", g.Count())).OrderByDescending(k => k.Value).ToList() }
            };
            var encabezados = new List<string> { "Corrida", "Inicio", "Fin", "Duración (s)", "Origen", "Estado", "Leídos", "Altas", "Actualizados",
                "Sin cambios", "Bajas", "Ya no en AD", "Reactivaciones", "Omitidos", "Errores" };
            var filas = corridas.Select(s => new object[]
            {
                "#" + s.IdSincronizacion, s.FechaInicio, (object)s.FechaFin,
                s.FechaFin.HasValue ? (object)(int)(s.FechaFin.Value - s.FechaInicio).TotalSeconds : null,
                origen(s), estado(s.Estado), s.Leidos, s.Altas, s.Actualizaciones,
                conDetalle(s) ? (object)s.SinCambios : null, conDetalle(s) ? (object)s.Bajas : null,
                conDetalle(s) ? (object)s.Ausentes : null, conDetalle(s) ? (object)s.Reactivaciones : null, s.Omitidos, s.Errores
            }).ToList();

            byte[] bytes = ExcelEP360Helper.Generar("Sincronizaciones con Active Directory",
                "Todas las corridas · Generado el " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"), tarjetas, tablas, encabezados, filas, 5,
                f => (string)f[5] == "Terminada" ? "azul" : (string)f[5] == "Fallida" ? "rojo" : "gris");
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "sincronizaciones_AD_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx");
        }

        // Que se hizo exactamente en una corrida: altas, cada campo cambiado (antes -> despues), bajas, reactivaciones,
        // omitidos, errores y sucursales/departamentos desactivados. ?formato=xlsx descarga todo en Excel.
        public ActionResult Detalle(int id, string filtro, string buscar, string formato)
        {
            if (formato == "xlsx") return ExcelDetalle(id);

            var vista = _servicio.Detalle(id, filtro, buscar);
            if (vista == null) return HttpNotFound();
            ViewBag.Conteos = _servicio.RenglonesDetalle(id).GroupBy(SincronizacionADService.GrupoDe).ToDictionary(g => g.Key, g => g.Count());
            ViewBag.PersonasConCambios = _servicio.RenglonesDetalle(id).Where(r => r.Accion == "Cambio").Select(r => r.IdEntidad).Distinct().Count();
            return View(vista);
        }

        private ActionResult ExcelDetalle(int id)
        {
            var vista = _servicio.Detalle(id, null, null);
            if (vista == null) return HttpNotFound();
            var c = vista.Corrida;
            var renglones = vista.Renglones;

            var tarjetas = new List<ExcelEP360Helper.Tarjeta>
            {
                new ExcelEP360Helper.Tarjeta { Etiqueta = "Leídos de AD", Valor = c.Leidos, Nota = "usuarios del dominio" },
                new ExcelEP360Helper.Tarjeta { Etiqueta = "Altas", Valor = c.Altas, Color = "azul" },
                new ExcelEP360Helper.Tarjeta { Etiqueta = "Con cambios", Valor = c.Actualizaciones, Color = "azul", Nota = "personas con algún dato nuevo" },
                new ExcelEP360Helper.Tarjeta { Etiqueta = "Bajas", Valor = c.Bajas + c.Ausentes, Color = "navy", Nota = c.Ausentes + " ya no existen en AD" },
                new ExcelEP360Helper.Tarjeta { Etiqueta = "Errores", Valor = c.Errores, Color = c.Errores > 0 ? "rojo" : "gris" }
            };
            var tablas = new List<ExcelEP360Helper.TablaConteo>
            {
                new ExcelEP360Helper.TablaConteo
                {
                    Titulo = "Por movimiento",
                    Renglones = renglones.GroupBy(r => NombreMovimiento(r)).Select(g => new KeyValuePair<string, int>(g.Key, g.Count())).OrderByDescending(k => k.Value).ToList()
                },
                new ExcelEP360Helper.TablaConteo
                {
                    Titulo = "Campos que cambiaron",
                    Renglones = renglones.Where(r => r.Accion == "Cambio").GroupBy(r => r.Campo).Select(g => new KeyValuePair<string, int>(g.Key, g.Count())).OrderByDescending(k => k.Value).ToList()
                }
            };

            var encabezados = new List<string> { "Fecha", "Tipo", "Nombre", "Usuario AD", "Movimiento", "Campo", "Valor anterior", "Valor nuevo", "Motivo" };
            var filas = renglones.Select(r => new object[] { r.Fecha, r.Entidad, r.Nombre, r.Usuario, NombreMovimiento(r), r.Campo, r.ValorAnterior, r.ValorNuevo, r.Motivo }).ToList();
            string subtitulo = "Corrida #" + c.IdSincronizacion + " · " + c.FechaInicio.ToString("dd/MM/yyyy HH:mm") + " · " + c.EjecutadoPor + " · " + c.Estado;

            byte[] bytes = ExcelEP360Helper.Generar("Sincronización con Active Directory", subtitulo, tarjetas, tablas, encabezados, filas, 4,
                f => (string)f[4] == "Error" ? "rojo" : (string)f[4] == "Baja" || (string)f[4] == "Desactivada" ? "navy" : (string)f[4] == "Alta" || (string)f[4] == "Reactivación" || (string)f[4] == "Reactivada" ? "azul" : "gris");
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "sincronizacion_AD_" + c.IdSincronizacion + "_" + c.FechaInicio.ToString("yyyyMMdd_HHmm") + ".xlsx");
        }

        public static string NombreMovimiento(EP360_Logistics_Admin.Models.SincronizacionADDetalleModel r)
        {
            switch (r.Accion)
            {
                case "Alta": return "Alta";
                case "Cambio": return "Cambio";
                case "Baja": return "Baja";
                case "Reactivacion": return "Reactivación";
                case "Omitido": return "Omitido";
                case "Error": return "Error";
                case "Desactivar": return "Desactivada";
                case "Reactivar": return "Reactivada";
                default: return r.Accion;
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Sincronizar()
        {
            // Leer todo el dominio y llamar un SP por usuario puede tardar mas que el limite normal de la peticion.
            Server.ScriptTimeout = 900;

            ResultadoSincronizacionModel resultado = null;
            string usuario = User.Identity.Name;
            if (Ejecutar(() => { resultado = _servicio.Ejecutar(usuario); }, null) && resultado != null)
            {
                string resumen = "Sincronización " + resultado.Estado + ": " + resultado.Leidos + " leídos, " +
                                 resultado.Altas + " altas, " + resultado.Actualizaciones + " con cambios, " + resultado.SinCambios + " sin cambios, " +
                                 resultado.Bajas + " dados de baja, " + resultado.Reactivaciones + " reactivados, " +
                                 resultado.Ausentes + " dados de baja por ya no existir en AD, " +
                                 resultado.Omitidos + " omitidos, " + resultado.Errores + " errores. " +
                                 "No cumplieron las reglas: " + resultado.NoCumplenReglas +
                                 (resultado.FaltantesPorRegla.Count == 0 ? "" :
                                    " (les falta: " + string.Join(", ", resultado.FaltantesPorRegla.Select(x => x.Key + " " + x.Value)) + ")") +
                                 ". Cuentas de sistema excluidas: " + resultado.CuentasDeSistema + "." +
                                 (resultado.SucursalesDesactivadas.Count == 0 ? "" :
                                    " Sucursales desactivadas por quedar sin personas: " + string.Join(", ", resultado.SucursalesDesactivadas) + ".") +
                                 (resultado.SucursalesReactivadas.Count == 0 ? "" :
                                    " Sucursales reactivadas por volver a tener personas: " + string.Join(", ", resultado.SucursalesReactivadas) + ".");
                if (resultado.Estado == "Terminada") TempData["Exito"] = resumen; else TempData["Error"] = resumen;
            }
            if (resultado != null) return RedirectToAction("Detalle", new { id = resultado.IdSincronizacion });
            return RedirectToAction("Index");
        }
    }
}
