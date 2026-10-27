using System;
using System.Collections.Generic;
using System.Data;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;
using static EP360_Logistics_Admin.DAL.Infraestructura.AccesoSP;

namespace EP360_Logistics_Admin.DAL
{
    // Estado de la copia en el 11. Casi todo es lectura. Omitir, Descartar y AlinearEnCopia NO se replican: la cola vive
    // unicamente en el 60 y el ajuste de contadores es de la copia.
    public class ReplicacionDAL
    {
        public ResumenReplicacionModel Resumen()
        {
            return Uno("sync.sp_ResumenReplicacion", r => new ResumenReplicacionModel
            {
                Pendientes = r.Entero("pendientes"),
                Divergentes = r.Entero("divergentes"),
                Aplicadas = r.Entero("aplicadas"),
                PendienteMasAntigua = r.Fecha("pendienteMasAntigua"),
                UltimaAplicada = r.Fecha("ultimaAplicada")
            });
        }

        // El usuario se lee solo si la columna existe (el script 10c puede no haberse corrido todavia).
        private static string TextoOpcional(IDataRecord r, string columna)
        {
            for (int i = 0; i < r.FieldCount; i++)
            {
                if (string.Equals(r.GetName(i), columna, StringComparison.OrdinalIgnoreCase)) return r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));
            }
            return null;
        }

        public List<ErrorReplicacionModel> Errores(int maximo)
        {
            return Lista("sync.sp_ObtenerErroresReplicacion", r => new ErrorReplicacionModel
            {
                IdCola = Convert.ToInt64(r.GetValue(r.GetOrdinal("idCola"))),
                FechaCreacion = r.Fecha("fechaCreacion").Value,
                Procedimiento = r.Texto("procedimiento"),
                Estado = r.Texto("estado"),
                Intentos = r.Entero("intentos"),
                UltimoError = r.Texto("ultimoError"),
                Usuario = TextoOpcional(r, "usuario"),
                FechaUltimoIntento = r.Fecha("fechaUltimoIntento")
            }, P("@maximo", maximo));
        }

        public List<HistorialReplicacionModel> Historial(int maximo)
        {
            return Lista("sync.sp_ObtenerHistorialReplicacion", r => new HistorialReplicacionModel
            {
                IdCola = Convert.ToInt64(r.GetValue(r.GetOrdinal("idCola"))),
                FechaCreacion = r.Fecha("fechaCreacion").Value,
                Procedimiento = r.Texto("procedimiento"),
                Estado = r.Texto("estado"),
                Usuario = TextoOpcional(r, "usuario"),
                ResueltaPor = TextoOpcional(r, "resueltaPor"),
                FechaAplicada = r.Fecha("fechaAplicada")
            }, P("@maximo", maximo));
        }

        // Los parametros se devuelven como JSON crudo; el servicio los presenta (y oculta contrasenas).
        public OperacionReplicacionModel Operacion(long idCola, out string parametrosJson)
        {
            string json = null;
            var operacion = Uno("sync.sp_ObtenerOperacionReplicacion", r =>
            {
                json = r.Texto("parametros");
                return new OperacionReplicacionModel
                {
                    IdCola = Convert.ToInt64(r.GetValue(r.GetOrdinal("idCola"))),
                    FechaCreacion = r.Fecha("fechaCreacion").Value,
                    Procedimiento = r.Texto("procedimiento"),
                    Estado = r.Texto("estado"),
                    Intentos = r.Entero("intentos"),
                    UltimoError = r.Texto("ultimoError"),
                    ResultadoMaestro = r.Texto("resultadoMaestro"),
                    Usuario = TextoOpcional(r, "usuario"),
                    ResueltaPor = TextoOpcional(r, "resueltaPor"),
                    FechaUltimoIntento = r.Fecha("fechaUltimoIntento"),
                    FechaAplicada = r.Fecha("fechaAplicada")
                };
            }, P("@idCola", idCola));
            parametrosJson = json;
            return operacion;
        }

        private static ConteoTablaModel MapaConteo(IDataRecord r)
        {
            int i = r.GetOrdinal("ultimoId");
            return new ConteoTablaModel
            {
                Tabla = r.Texto("tabla"),
                Filas = r.Entero("filas"),
                UltimoId = r.IsDBNull(i) ? (long?)null : Convert.ToInt64(r.GetValue(i))
            };
        }

        public List<ConteoTablaModel> ConteosMaestro() { return Lista("sync.sp_ObtenerConteosReplicacion", MapaConteo); }
        public List<ConteoTablaModel> ConteosCopia() { return ListaReplica("sync.sp_ObtenerConteosReplicacion", MapaConteo); }

        public void Omitir(long idCola, string usuario)
        {
            EjecutarCrudo("sync.sp_OmitirReplicacion", P("@idCola", idCola), P("@usuario", usuario));
        }

        public int Descartar(string usuario)
        {
            var filas = Lista("sync.sp_ReiniciarColaReplicacion", r => r.Entero("descartadas"), P("@usuario", usuario));
            return filas.Count > 0 ? filas[0] : 0;
        }

        // Se ejecuta en el 11. Devuelve true si el contador se movio.
        public bool AlinearEnCopia(string tabla, long ultimoUsadoMaestro)
        {
            var filas = ListaReplica("sync.sp_AlinearIdentidad", r => r.Booleano("ajustada"),
                P("@tabla", tabla), P("@ultimoUsadoMaestro", ultimoUsadoMaestro));
            return filas.Count > 0 && filas[0];
        }
    }
}
