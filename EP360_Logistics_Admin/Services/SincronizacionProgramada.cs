using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace EP360_Logistics_Admin.Services
{
    // Sincronizacion con AD automatica todos los dias (2026-10-27, pedido explicito: "a las 3 de la manana todos los dias").
    // Mismo mecanismo que el Replicador: un temporizador dentro de la app revisa cada 5 minutos si ya toca.
    //
    // 2026-11-03: dejo de correr despues del 28-oct. IIS recicla el pool (cada 29 h por defecto) y, si el servidor no carga la
    // app sola (Application Initialization), el temporizador no existe hasta que alguien abre una pagina; a las 3 a.m. nadie
    // la abre y el codigo de antes se saltaba el dia completo. Ahora:
    //   - RECUPERA: la "corrida que toca" es la de hoy a la hora (o la de ayer si aun no da la hora). Si desde entonces no ha
    //     habido ninguna corrida exitosa (manual o automatica), corre en cuanto la app este despierta, aunque sea tarde.
    //   - REINTENTA: si la unica corrida desde esa hora fue Fallida (no pudo leer AD), reintenta cada hora, maximo 3 intentos.
    //   - SE VE: la pantalla de Sincronizacion muestra desde cuando esta despierta la app, la ultima revision y el ultimo
    //     error del temporizador (antes solo iba a Trace y no lo veia nadie).
    // El deploy ademas instala Application Initialization y quita el reciclaje cada 29 h (ver cd-deploy-prod.yml).
    //
    // La hora sale de "SincronizacionAD_Hora" (variable de entorno de MAQUINA o AppSettings, mismo criterio que AD_*):
    // sin valor = 03:00; "off" = apagada (el localhost la apaga para no sincronizar contra la BD real desde una PC).
    // Dos instancias a la vez no se pisan: sync.sp_IniciarSincronizacionAD rechaza una segunda corrida en curso.
    public static class SincronizacionProgramada
    {
        private const string Clave = "SincronizacionAD_Hora";
        private static readonly TimeSpan HoraPorDefecto = new TimeSpan(3, 0, 0);
        private static readonly TimeSpan EsperaEntreReintentos = TimeSpan.FromHours(1);
        private const int MaximoIntentos = 3;
        // Pasada esta tolerancia despues de la hora, la corrida se marca como recuperada en "Ejecutado por".
        private static readonly TimeSpan Tolerancia = TimeSpan.FromMinutes(15);

        private static Timer _temporizador;
        private static int _corriendo;

        // Para la pantalla: si "Despierta desde" es de hoy en la manana despues de las 3, la app estuvo dormida a la hora.
        public static DateTime? DespiertaDesde { get; private set; }
        public static DateTime? UltimaRevision { get; private set; }
        public static DateTime? UltimoErrorFecha { get; private set; }
        public static string UltimoError { get; private set; }

        public static TimeSpan? Hora
        {
            get
            {
                string valor = Environment.GetEnvironmentVariable(Clave, EnvironmentVariableTarget.Machine) ?? ConfigurationManager.AppSettings[Clave];
                if (string.IsNullOrWhiteSpace(valor)) return HoraPorDefecto;
                if (valor.Trim().Equals("off", StringComparison.OrdinalIgnoreCase)) return null;
                TimeSpan hora;
                return TimeSpan.TryParseExact(valor.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out hora) ? hora : HoraPorDefecto;
            }
        }

        public static void Iniciar()
        {
            if (_temporizador != null || !Hora.HasValue) return;
            DespiertaDesde = DateTime.Now;
            _temporizador = new Timer(_ => Revisar(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        }

        // La ultima hora programada que ya paso: hoy a la hora, o ayer si todavia no llega la de hoy.
        public static DateTime? CorridaQueToca(DateTime ahora)
        {
            var hora = Hora;
            if (!hora.HasValue) return null;
            DateTime hoy = ahora.Date + hora.Value;
            return ahora >= hoy ? hoy : hoy.AddDays(-1);
        }

        private static void Revisar()
        {
            DateTime ahora = DateTime.Now;
            UltimaRevision = ahora;
            var hora = Hora;
            DateTime? toca = CorridaQueToca(ahora);
            if (!hora.HasValue || !toca.HasValue) return;
            if (Interlocked.Exchange(ref _corriendo, 1) == 1) return;
            try
            {
                var servicio = new SincronizacionADService();
                var desdeLaHora = servicio.Historial(20).Where(s => s.FechaInicio >= toca.Value).ToList();

                // Ya hubo una corrida buena (o una en curso) desde la hora: nada que hacer.
                if (desdeLaHora.Any(s => s.Estado != "Fallida")) return;
                // Solo fallidas: reintentar cada hora, sin pasar del maximo.
                if (desdeLaHora.Count >= MaximoIntentos) return;
                if (desdeLaHora.Any(s => ahora - s.FechaInicio < EsperaEntreReintentos)) return;

                string etiqueta = "Automática (" + hora.Value.ToString(@"hh\:mm");
                if (desdeLaHora.Count > 0) etiqueta += ", reintento " + (desdeLaHora.Count + 1);
                else if (ahora - toca.Value > Tolerancia) etiqueta += ", recuperada";
                servicio.Ejecutar(etiqueta + ")");
            }
            catch (SqlException ex) when (ex.Number == 50031)
            {
                // Otra corrida ya esta en curso (otra instancia o alguien le dio al boton): esa cuenta como la de hoy.
            }
            catch (Exception ex)
            {
                UltimoErrorFecha = DateTime.Now;
                UltimoError = ex.Message;
                Trace.TraceError("Sincronizacion automatica con AD: " + ex);
            }
            finally
            {
                Interlocked.Exchange(ref _corriendo, 0);
            }
        }
    }
}
