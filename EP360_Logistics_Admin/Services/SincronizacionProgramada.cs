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
    // Mismo mecanismo que el Replicador: un temporizador dentro de la app revisa cada 5 minutos si ya toca. Corre una vez por
    // dia dentro de la ventana [hora, hora + 3 h); si ya hubo una corrida (manual o automatica) desde la hora de hoy, no repite.
    // Si la app estuvo dormida toda la ventana, ese dia se salta: no se corre a media jornada.
    //
    // La hora sale de "SincronizacionAD_Hora" (variable de entorno de MAQUINA o AppSettings, mismo criterio que AD_*):
    // sin valor = 03:00; "off" = apagada (el localhost la apaga para no sincronizar contra la BD real desde una PC).
    // Dos instancias a la vez no se pisan: sync.sp_IniciarSincronizacionAD rechaza una segunda corrida en curso.
    public static class SincronizacionProgramada
    {
        private const string Clave = "SincronizacionAD_Hora";
        private static readonly TimeSpan HoraPorDefecto = new TimeSpan(3, 0, 0);
        private static readonly TimeSpan Ventana = TimeSpan.FromHours(3);

        private static Timer _temporizador;
        private static int _corriendo;

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
            _temporizador = new Timer(_ => Revisar(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        }

        private static void Revisar()
        {
            var hora = Hora;
            if (!hora.HasValue) return;
            DateTime inicioVentana = DateTime.Today + hora.Value;
            DateTime ahora = DateTime.Now;
            if (ahora < inicioVentana || ahora >= inicioVentana + Ventana) return;
            if (Interlocked.Exchange(ref _corriendo, 1) == 1) return;
            try
            {
                var servicio = new SincronizacionADService();
                if (servicio.Historial(5).Any(s => s.FechaInicio >= inicioVentana)) return;
                servicio.Ejecutar("Automática (" + hora.Value.ToString(@"hh\:mm") + ")");
            }
            catch (SqlException ex) when (ex.Number == 50031)
            {
                // Otra corrida ya esta en curso (otra instancia o alguien le dio al boton): esa cuenta como la de hoy.
            }
            catch (Exception ex)
            {
                Trace.TraceError("Sincronizacion automatica con AD: " + ex);
            }
            finally
            {
                Interlocked.Exchange(ref _corriendo, 0);
            }
        }
    }
}
