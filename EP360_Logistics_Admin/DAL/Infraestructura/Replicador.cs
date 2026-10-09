using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using static EP360_Logistics_Admin.DAL.Infraestructura.AccesoSP;

namespace EP360_Logistics_Admin.DAL.Infraestructura
{
    // Escritura en los dos servidores (60 = maestro, 11 = copia).
    //
    // Toda escritura de la app pasa por AccesoSP.Escalar / Ejecutar / UnoEscritura, que llaman a Escribir:
    //   1. se ejecuta en el 60 (si falla, el error sube tal cual y no se replica nada);
    //   2. se guarda en sync.ColaReplicacion (en el 60) el procedimiento con sus parametros;
    //   3. se intenta repetir en el 11 todo lo pendiente, EN ORDEN. Si el 11 no responde, queda pendiente y se reintenta
    //      cada minuto (temporizador) o desde la pantalla "Replicacion".
    //
    // Se repite la misma llamada al SP (no se copian filas) y siempre en el mismo orden, asi los ids autogenerados siguen
    // siendo iguales en los dos servidores. Si un SP devuelve en el 11 un resultado distinto al del 60, o falla por una regla
    // de negocio o por un duplicado, la operacion se marca Divergente y NO se replica nada mas hasta que alguien revise
    // (ver Database/10_ReplicacionAl11.sql). Una escritura en el 60 nunca falla por culpa del 11.
    public static class Replicador
    {
        private static readonly object Candado = new object();
        private static readonly TimeSpan EsperaTrasFallo = TimeSpan.FromSeconds(30);
        private static DateTime _noReintentarAntesDe = DateTime.MinValue;
        private static Timer _temporizador;

        // Ultimo problema al REGISTRAR una operacion en la cola (por ejemplo, el script 10 aun no se ha corrido en el 60).
        public static string UltimoErrorLocal { get; private set; }
        public static DateTime? FechaUltimoErrorLocal { get; private set; }

        public static bool Configurada { get { return ConexionBD.ReplicaConfigurada; } }

        private class ParametroSerializado
        {
            public string n { get; set; }
            public string t { get; set; }
            public string v { get; set; }
        }

        public class PendienteReplicacion
        {
            public long IdCola { get; set; }
            public string Procedimiento { get; set; }
            public string Parametros { get; set; }
            public string ResultadoMaestro { get; set; }
        }

        // ---------------------------------------------------------------------------------------------
        // Escritura
        // ---------------------------------------------------------------------------------------------

        public static T Escribir<T>(string sp, SqlParameter[] parametros, Func<T> enMaestro, Func<T, string> aTexto)
        {
            if (!Configurada) return enMaestro();

            // Se toma la "foto" de los parametros ANTES de usarlos: un SqlParameter no se puede reutilizar en otro comando.
            string json = Serializar(parametros);

            lock (Candado)
            {
                T resultado = enMaestro();
                try
                {
                    string texto = aTexto == null ? null : aTexto(resultado);
                    Encolar(sp, json, texto, UsuarioActual());
                    ProcesarSinCandado(false);
                }
                catch (Exception ex)
                {
                    UltimoErrorLocal = ex.Message;
                    FechaUltimoErrorLocal = DateTime.Now;
                    Trace.TraceError("Replicacion: no se pudo registrar/aplicar '" + sp + "': " + ex);
                }
                return resultado;
            }
        }

        // Quien hizo la operacion (la sesion de Windows que esta usando la app); "sistema" si no hay peticion (temporizador).
        public static string UsuarioActual()
        {
            var contexto = System.Web.HttpContext.Current;
            string nombre = contexto != null && contexto.User != null && contexto.User.Identity != null ? contexto.User.Identity.Name : null;
            return string.IsNullOrWhiteSpace(nombre) ? "sistema" : nombre;
        }

        private static void Encolar(string sp, string json, string resultado, string usuario)
        {
            try
            {
                EscalarCrudo("sync.sp_EncolarReplicacion",
                    P("@procedimiento", sp), P("@parametros", json), P("@resultadoMaestro", resultado), P("@usuario", usuario));
            }
            catch (SqlException ex) when (ex.Number == 8144)
            {
                // El script 10c aun no se corrio en el 60 (el SP no conoce @usuario): se encola sin el usuario para no perder la operacion.
                EscalarCrudo("sync.sp_EncolarReplicacion",
                    P("@procedimiento", sp), P("@parametros", json), P("@resultadoMaestro", resultado));
            }
        }

        // Parametros de una operacion para mostrarlos en pantalla. Lo que parezca contrasena o hash se oculta.
        public static List<KeyValuePair<string, string>> DescribirParametros(string json)
        {
            var resultado = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(json)) return resultado;
            List<ParametroSerializado> lista;
            try { lista = new JavaScriptSerializer().Deserialize<List<ParametroSerializado>>(json); }
            catch (Exception) { return resultado; }
            if (lista == null) return resultado;

            foreach (var p in lista)
            {
                string nombre = p.n ?? "";
                string valor;
                if (p.t == "null") valor = "(vacío)";
                else if (nombre.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 || nombre.IndexOf("hash", StringComparison.OrdinalIgnoreCase) >= 0) valor = "•••••• (oculto)";
                else if (p.t == "bytes") valor = "(datos binarios)";
                else valor = p.v;
                resultado.Add(new KeyValuePair<string, string>(nombre, valor));
            }
            return resultado;
        }

        // ---------------------------------------------------------------------------------------------
        // Aplicar lo pendiente en el 11
        // ---------------------------------------------------------------------------------------------

        // Devuelve cuantas operaciones se aplicaron. "forzar" ignora la espera posterior a un fallo (boton de la pantalla).
        public static int ProcesarPendientes(bool forzar)
        {
            if (!Configurada) return 0;
            lock (Candado)
            {
                return ProcesarSinCandado(forzar);
            }
        }

        private static int ProcesarSinCandado(bool forzar)
        {
            if (!forzar && DateTime.UtcNow < _noReintentarAntesDe) return 0;

            var pendientes = Lista("sync.sp_ObtenerPendientesReplicacion", r => new PendienteReplicacion
            {
                IdCola = Convert.ToInt64(r.GetValue(r.GetOrdinal("idCola"))),
                Procedimiento = r.Texto("procedimiento"),
                Parametros = r.Texto("parametros"),
                ResultadoMaestro = r.Texto("resultadoMaestro")
            }, P("@maximo", 200));

            int aplicadas = 0;
            foreach (var pendiente in pendientes)
            {
                try
                {
                    string resultadoCopia = EjecutarEnReplica(pendiente);

                    if (pendiente.ResultadoMaestro != null && resultadoCopia != null && resultadoCopia != pendiente.ResultadoMaestro)
                    {
                        Marcar(pendiente.IdCola, "Divergente",
                            "El 11 devolvio '" + resultadoCopia + "' y el 60 '" + pendiente.ResultadoMaestro + "'. Los ids ya no coinciden.");
                        break;
                    }

                    Marcar(pendiente.IdCola, "Aplicada", null);
                    aplicadas++;
                }
                catch (SqlException ex) when (EsDivergencia(ex))
                {
                    Marcar(pendiente.IdCola, "Divergente", "El 11 rechazo la operacion (error " + ex.Number + "): " + ex.Message);
                    break;
                }
                catch (Exception ex)
                {
                    // Sin conexion, timeout, falta el SP en el 11, etc.: se queda Pendiente y se reintenta mas tarde, en orden.
                    Marcar(pendiente.IdCola, "Pendiente", ex.Message);
                    _noReintentarAntesDe = DateTime.UtcNow + EsperaTrasFallo;
                    break;
                }
            }
            return aplicadas;
        }

        // Reglas de negocio (THROW 50000-50999), duplicados (2601/2627) y llaves foraneas (547): el 11 tiene datos distintos
        // a los del 60. Reintentar no lo arregla.
        private static bool EsDivergencia(SqlException ex)
        {
            foreach (SqlError e in ex.Errors)
            {
                if (e.Number >= 50000 && e.Number < 51000) return true;
                if (e.Number == 2601 || e.Number == 2627 || e.Number == 547) return true;
            }
            return false;
        }

        private static void Marcar(long idCola, string estado, string error)
        {
            EjecutarCrudo("sync.sp_MarcarReplicacion", P("@idCola", idCola), P("@estado", estado), P("@error", error));
        }

        private static string EjecutarEnReplica(PendienteReplicacion pendiente)
        {
            using (var conexion = ConexionBD.ObtenerConexionReplica())
            using (var comando = new SqlCommand(pendiente.Procedimiento, conexion) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 })
            {
                comando.Parameters.AddRange(Deserializar(pendiente.Parametros));
                conexion.Open();
                object r = comando.ExecuteScalar();
                return r == null || r == DBNull.Value ? null : Convert.ToString(r, CultureInfo.InvariantCulture);
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Reintento automatico
        // ---------------------------------------------------------------------------------------------

        public static void IniciarReintentos()
        {
            if (_temporizador != null || !Configurada) return;
            _temporizador = new Timer(_ =>
            {
                try { ProcesarPendientes(false); }
                catch (Exception ex) { Trace.TraceError("Replicacion (reintento): " + ex); }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
        }

        // ---------------------------------------------------------------------------------------------
        // Parametros <-> JSON
        // ---------------------------------------------------------------------------------------------

        private static string Serializar(SqlParameter[] parametros)
        {
            var lista = new List<ParametroSerializado>();
            if (parametros != null)
            {
                foreach (var p in parametros)
                {
                    var item = new ParametroSerializado { n = p.ParameterName };
                    object valor = p.Value;
                    if (valor == null || valor == DBNull.Value) { item.t = "null"; }
                    else if (valor is string) { item.t = "string"; item.v = (string)valor; }
                    else if (valor is bool) { item.t = "bool"; item.v = (bool)valor ? "1" : "0"; }
                    else if (valor is int) { item.t = "int"; item.v = ((int)valor).ToString(CultureInfo.InvariantCulture); }
                    else if (valor is long) { item.t = "long"; item.v = ((long)valor).ToString(CultureInfo.InvariantCulture); }
                    else if (valor is short || valor is byte) { item.t = "int"; item.v = Convert.ToInt32(valor).ToString(CultureInfo.InvariantCulture); }
                    else if (valor is decimal) { item.t = "decimal"; item.v = ((decimal)valor).ToString(CultureInfo.InvariantCulture); }
                    else if (valor is double) { item.t = "double"; item.v = ((double)valor).ToString("R", CultureInfo.InvariantCulture); }
                    else if (valor is DateTime) { item.t = "datetime"; item.v = ((DateTime)valor).ToString("o", CultureInfo.InvariantCulture); }
                    else if (valor is Guid) { item.t = "guid"; item.v = ((Guid)valor).ToString(); }
                    else if (valor is byte[]) { item.t = "bytes"; item.v = Convert.ToBase64String((byte[])valor); }
                    else throw new NotSupportedException("Tipo de parametro no soportado para replicar: " + valor.GetType().Name);
                    lista.Add(item);
                }
            }
            return new JavaScriptSerializer().Serialize(lista);
        }

        private static SqlParameter[] Deserializar(string json)
        {
            var lista = new JavaScriptSerializer().Deserialize<List<ParametroSerializado>>(json) ?? new List<ParametroSerializado>();
            return lista.Select(item => new SqlParameter(item.n, Convertir(item))).ToArray();
        }

        private static object Convertir(ParametroSerializado item)
        {
            switch (item.t)
            {
                case "null": return DBNull.Value;
                case "string": return item.v;
                case "bool": return item.v == "1";
                case "int": return int.Parse(item.v, CultureInfo.InvariantCulture);
                case "long": return long.Parse(item.v, CultureInfo.InvariantCulture);
                case "decimal": return decimal.Parse(item.v, CultureInfo.InvariantCulture);
                case "double": return double.Parse(item.v, CultureInfo.InvariantCulture);
                case "datetime": return DateTime.Parse(item.v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                case "guid": return Guid.Parse(item.v);
                case "bytes": return Convert.FromBase64String(item.v);
                default: throw new NotSupportedException("Tipo de parametro desconocido en la cola: " + item.t);
            }
        }
    }
}
