using System;
using System.Collections.Generic;
using System.Linq;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    public class ReplicacionService
    {
        private readonly ReplicacionDAL _dal = new ReplicacionDAL();

        // Una operacion pendiente mas vieja que esto ya no es "un instante de espera": el 11 probablemente no responde.
        private static readonly TimeSpan PendienteSospechosa = TimeSpan.FromMinutes(5);

        private static readonly Dictionary<string, string> NombresAmigables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "dir.sp_InsertarPersona", "Alta de persona" },
            { "dir.sp_ActualizarPersona", "Edición de persona" },
            { "dir.sp_DesactivarPersona", "Baja de persona" },
            { "dir.sp_ReactivarPersona", "Reactivación de persona" },
            { "dir.sp_VincularPersonaCuenta", "Vincular persona con cuenta" },
            { "dir.sp_DesvincularPersonaCuenta", "Desvincular persona de cuenta" },
            { "dir.sp_InsertarCuenta", "Alta de cuenta" },
            { "dir.sp_ActualizarCuenta", "Edición de cuenta" },
            { "dir.sp_DesactivarCuenta", "Baja de cuenta" },
            { "dir.sp_ReactivarCuenta", "Reactivación de cuenta" },
            { "dir.sp_InsertarGrupoCuenta", "Alta de grupo de cuentas" },
            { "dir.sp_ActualizarGrupoCuenta", "Edición de grupo de cuentas" },
            { "dir.sp_DesactivarGrupoCuenta", "Baja de grupo de cuentas" },
            { "dir.sp_ReactivarGrupoCuenta", "Reactivación de grupo de cuentas" },
            { "seg.sp_FijarPasswordCredencial", "Contraseña de usuario externo" },
            { "sync.sp_IniciarSincronizacionAD", "Inicio de sincronización con AD" },
            { "sync.sp_SincronizarUsuarioAD", "Usuario de AD sincronizado" },
            { "sync.sp_FinalizarSincronizacionAD", "Fin de sincronización con AD" }
        };

        public static string NombreAmigable(string procedimiento)
        {
            string nombre;
            return procedimiento != null && NombresAmigables.TryGetValue(procedimiento, out nombre) ? nombre : procedimiento;
        }

        // Nunca lanza: la pantalla debe poder explicar que falta (script 10, cadena del 11, 11 apagado...).
        public EstadoReplicacionModel ObtenerEstado()
        {
            var estado = new EstadoReplicacionModel
            {
                Configurada = Replicador.Configurada,
                UltimoErrorLocal = Replicador.UltimoErrorLocal,
                FechaUltimoErrorLocal = Replicador.FechaUltimoErrorLocal,
                PasosSugeridos = new List<string>()
            };
            if (!estado.Configurada) return estado;

            List<ConteoTablaModel> maestro = null, copia = null;
            try
            {
                estado.Resumen = _dal.Resumen();
                estado.Errores = _dal.Errores(20);
                maestro = _dal.ConteosMaestro();
            }
            catch (Exception ex) { estado.ErrorMaestro = ex.Message; }

            // El historial usa un SP del script 10c: si aun no esta, solo falta esa seccion.
            if (estado.ErrorMaestro == null)
            {
                try { estado.Historial = _dal.Historial(20); }
                catch (Exception) { estado.Historial = new List<HistorialReplicacionModel>(); }
                foreach (var h in estado.Historial) h.Descripcion = NombreAmigable(h.Procedimiento);
                foreach (var e in estado.Errores) e.Descripcion = NombreAmigable(e.Procedimiento);
            }

            try { copia = _dal.ConteosCopia(); }
            catch (Exception ex) { estado.ErrorCopia = ex.Message; }

            bool hayPorAplicar = estado.Resumen != null && (estado.Resumen.Pendientes > 0 || estado.Resumen.Divergentes > 0);

            if (maestro != null)
            {
                foreach (var m in maestro)
                {
                    var fila = new ComparativoTablaModel
                    {
                        Tabla = m.Tabla,
                        Maestro = m,
                        Copia = copia == null ? null : copia.FirstOrDefault(c => c.Tabla == m.Tabla)
                    };
                    if (copia != null) Diagnosticar(fila, hayPorAplicar);
                    estado.Comparativo.Add(fila);
                }
            }

            estado.PuedeAlinearContadores = !hayPorAplicar && estado.Comparativo.Any(c => c.PuedeAlinear);
            ResumirEstado(estado);
            return estado;
        }

        // ---------------------------------------------------------------------------------------------
        // Explicaciones
        // ---------------------------------------------------------------------------------------------

        private static void Diagnosticar(ComparativoTablaModel c, bool hayPorAplicar)
        {
            if (c.Copia == null)
            {
                c.Clase = "error";
                c.Resumen = "No se pudo leer esta tabla en el 11.";
                c.Sugerencia = "Revisa que los scripts 10 y 10c se hayan corrido en el 11 y que la tabla exista allá.";
                return;
            }

            int dif = c.Copia.Filas - c.Maestro.Filas;
            long? idMaestro = c.Maestro.UltimoId, idCopia = c.Copia.UltimoId;
            bool idIgual = idMaestro == idCopia;

            if (dif == 0 && idIgual) { c.Clase = "ok"; c.Resumen = "Igual."; return; }

            if (dif == 0)
            {
                if (idMaestro.HasValue && idCopia.HasValue && idCopia.Value < idMaestro.Value)
                {
                    c.Clase = "info";
                    c.Resumen = "Mismas filas. Solo el contador de ids del 11 va atrasado (" + idCopia + " contra " + idMaestro + "): en el 60 se usaron ids que no quedaron en la tabla (altas revertidas o filas borradas).";
                    c.Sugerencia = hayPorAplicar
                        ? "Primero deja que se apliquen las operaciones pendientes; después se podrá alinear."
                        : "No afecta los datos, pero el siguiente alta saldría con otro id en el 11. Usa «Alinear contadores».";
                    c.PuedeAlinear = !hayPorAplicar;
                }
                else
                {
                    c.Clase = "aviso";
                    c.Resumen = "Mismas filas, pero el contador de ids del 11 va adelantado o es distinto (" + idCopia + " contra " + idMaestro + ").";
                    c.Sugerencia = "El siguiente alta saldría con otro id en el 11. «Alinear contadores» solo sube contadores, nunca los baja: esta tabla se ajusta a mano.";
                }
                return;
            }

            c.Clase = "error";
            if (dif < 0)
            {
                c.Resumen = "Faltan " + (-dif) + " filas en el 11.";
                c.Sugerencia = hayPorAplicar
                    ? "Hay operaciones pendientes que pueden explicar la diferencia: déjalas aplicarse o usa «Reintentar ahora»."
                    : "Causas comunes: alguien guardó directo en el 60 sin pasar por esta app, o la copia se hizo antes de esos cambios. Si no hay pendientes, hay que volver a copiar la base.";
            }
            else
            {
                c.Resumen = "Sobran " + dif + " filas en el 11.";
                c.Sugerencia = "Probable: alguien escribió directo en el 11, o se borraron filas en el 60 fuera de esta app. Revísalo antes de volver a copiar la base.";
            }
        }

        private static void ResumirEstado(EstadoReplicacionModel e)
        {
            var pasos = e.PasosSugeridos;

            if (e.ErrorMaestro != null)
            {
                e.ClaseGeneral = "error";
                e.TituloGeneral = "No se pudo leer la cola de replicación en el 60";
                e.ExplicacionGeneral = "Sin esa información no se puede saber si la copia está al día.";
                pasos.Add("Confirma que corriste los scripts 10 y 10c en el 60, conectado al 60.");
                return;
            }
            if (e.ErrorCopia != null)
            {
                e.ClaseGeneral = "error";
                e.TituloGeneral = "No se pudo leer la copia en el 11";
                e.ExplicacionGeneral = "Lo que se guarde ahora quedará pendiente hasta que el 11 responda; no se pierde nada.";
                pasos.Add("Revisa que el 11 esté encendido y accesible, y la cadena CadenaSQLReplica.");
                pasos.Add("Revisa que el usuario con el que corre la app tenga permiso en la base EP360_Logistics del 11.");
                pasos.Add("Confirma que corriste los scripts 10 y 10c en el 11.");
                return;
            }

            var r = e.Resumen;
            if (r != null && r.Divergentes > 0)
            {
                e.ClaseGeneral = "error";
                e.TituloGeneral = "Hay " + r.Divergentes + " operación(es) divergente(s)";
                e.ExplicacionGeneral = "Una operación funcionó en el 60 pero el 11 la rechazó o dio un resultado distinto. Mientras exista, no se replica nada más, para no mezclar ids.";
                pasos.Add("Abre la operación divergente (tabla «Operaciones con problema») y lee qué significa y qué hacer.");
                pasos.Add("Si ya corregiste el dato en el 11 a mano, usa «Omitir» en esa operación.");
                pasos.Add("Si la copia quedó desviada, vuelve a copiar la base del 60 al 11 y después «Descartar pendientes».");
                return;
            }
            if (r != null && r.Pendientes > 0)
            {
                bool vieja = r.PendienteMasAntigua.HasValue && DateTime.Now - r.PendienteMasAntigua.Value > PendienteSospechosa;
                e.ClaseGeneral = vieja ? "error" : "aviso";
                e.TituloGeneral = r.Pendientes + " operación(es) pendiente(s) de copiar al 11";
                e.ExplicacionGeneral = vieja
                    ? "Llevan más de 5 minutos sin aplicarse: el 11 probablemente no está respondiendo. No se pierde nada; se aplican en orden en cuanto responda."
                    : "Se están aplicando; se reintenta solo cada minuto.";
                pasos.Add("Pulsa «Reintentar ahora» para forzar el intento.");
                if (vieja) pasos.Add("Si sigue igual, abre la primera operación pendiente: su último error dice por qué no se aplica.");
                return;
            }

            if (e.Comparativo.Count > 0 && e.Comparativo.All(c => c.Coincide))
            {
                e.ClaseGeneral = "ok";
                e.TituloGeneral = "La copia en el 11 está al día";
                e.ExplicacionGeneral = "No hay operaciones por aplicar y todas las tablas coinciden en filas y contador de ids.";
                return;
            }

            bool soloContadores = e.Comparativo.Where(c => !c.Coincide).All(c => c.Clase == "info");
            if (soloContadores)
            {
                e.ClaseGeneral = "info";
                e.TituloGeneral = "Los datos coinciden; solo difieren contadores de ids";
                e.ExplicacionGeneral = "Es normal tras pruebas con rollback o filas borradas en el 60. No hay datos distintos, pero el siguiente alta saldría con otro id en el 11.";
                pasos.Add("Pulsa «Alinear contadores» (sube los contadores del 11; no toca datos).");
            }
            else
            {
                e.ClaseGeneral = "error";
                e.TituloGeneral = "Hay diferencias de datos entre el 60 y el 11";
                e.ExplicacionGeneral = "Alguna tabla tiene distinto número de filas. Lee la explicación de cada tabla marcada.";
                pasos.Add("Revisa primero que no haya operaciones pendientes o divergentes.");
                pasos.Add("Si alguien escribió fuera de esta app, hay que volver a copiar la base del 60 al 11 y después «Descartar pendientes».");
            }
        }

        // Interpreta el ultimo error de una operacion en palabras y dice que hacer.
        private static void Interpretar(string estado, string error, out string significa, out string hacer)
        {
            string e = (error ?? "").ToLowerInvariant();

            if (estado == "Aplicada") { significa = "Se aplicó correctamente en el 11."; hacer = "Nada."; return; }
            if (estado == "Omitida") { significa = "Un administrador la dejó pasar a mano. El 11 puede no tener este cambio."; hacer = "Nada, salvo que necesites igualar esos datos."; return; }

            if (e.Contains("cannot open database") || e.Contains("login failed"))
            {
                significa = "El usuario con el que corre la app no tiene permiso en la base del 11.";
                hacer = "Da permiso en el 11 (lectura, escritura y ejecución) y pulsa «Reintentar ahora».";
            }
            else if (e.Contains("could not find stored procedure") || e.Contains("no se pudo encontrar el procedimiento"))
            {
                significa = "Al 11 le falta un procedimiento almacenado.";
                hacer = "Corre los scripts 10 y 10c en el 11 y pulsa «Reintentar ahora».";
            }
            else if (e.Contains("network-related") || e.Contains("timeout") || e.Contains("not found or not accessible") || e.Contains("error locating server") || e.Contains("tiempo de espera"))
            {
                significa = "El 11 no respondió (apagado, red o puerto).";
                hacer = "Verifica que el 11 esté encendido y accesible. La operación se reintenta sola cada minuto.";
            }
            else if (e.Contains("los ids ya no coinciden"))
            {
                significa = "El 60 y el 11 generaron ids distintos para la misma alta: los contadores de ids están desalineados.";
                hacer = "Si las filas de esa tabla coinciden, usa «Alinear contadores»; si no, vuelve a copiar la base. Después omite esta operación.";
            }
            else if (e.Contains("rechazo la operacion"))
            {
                significa = "El 11 rechazó una operación que en el 60 sí funcionó: sus datos ya no son iguales (por ejemplo la fila no existe o ya existe).";
                hacer = "Compara la tabla afectada. Si ya corregiste el dato en el 11 a mano, omite la operación; si no, vuelve a copiar la base y descarta pendientes.";
            }
            else
            {
                significa = estado == "Divergente" ? "La operación quedó distinta entre el 60 y el 11." : "La operación todavía no se pudo aplicar en el 11.";
                hacer = "Pulsa «Reintentar ahora». Si persiste, copia el último error exacto y compártelo con sistemas.";
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Acciones
        // ---------------------------------------------------------------------------------------------

        public OperacionReplicacionModel ObtenerOperacion(long idCola)
        {
            string json;
            var op = _dal.Operacion(idCola, out json);
            if (op == null) throw new InvalidOperationException("La operación #" + idCola + " no existe.");

            op.Descripcion = NombreAmigable(op.Procedimiento);
            op.Parametros = Replicador.DescribirParametros(json);
            string significa, hacer;
            Interpretar(op.Estado, op.UltimoError, out significa, out hacer);
            op.QueSignifica = significa;
            op.QueHacer = hacer;
            return op;
        }

        public int ReintentarAhora() { return Replicador.ProcesarPendientes(true); }

        public void OmitirOperacion(long idCola, string usuario) { _dal.Omitir(idCola, usuario); }

        public int DescartarPendientes(string usuario) { return _dal.Descartar(usuario); }

        // Sube los contadores de ids del 11 en las tablas con las mismas filas. Con operaciones por aplicar NO se permite:
        // el ultimo id del 60 ya incluye altas que el 11 todavia no ha repetido, y alinear desfasaria esos ids.
        public List<string> AlinearContadores()
        {
            var resumen = _dal.Resumen();
            if (resumen != null && (resumen.Pendientes > 0 || resumen.Divergentes > 0))
            {
                throw new InvalidOperationException("Hay operaciones pendientes o divergentes. Primero deben aplicarse u omitirse; después se pueden alinear los contadores.");
            }

            var maestro = _dal.ConteosMaestro();
            var copia = _dal.ConteosCopia();
            var ajustadas = new List<string>();
            foreach (var m in maestro)
            {
                var c = copia.FirstOrDefault(x => x.Tabla == m.Tabla);
                if (c == null || m.Filas != c.Filas || !m.UltimoId.HasValue) continue;
                if (c.UltimoId.HasValue && c.UltimoId.Value >= m.UltimoId.Value) continue;
                if (_dal.AlinearEnCopia(m.Tabla, m.UltimoId.Value)) ajustadas.Add(m.Tabla);
            }
            return ajustadas;
        }
    }
}
