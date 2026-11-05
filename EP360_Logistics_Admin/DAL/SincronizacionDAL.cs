using System.Collections.Generic;
using System.Globalization;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;
using static EP360_Logistics_Admin.DAL.Infraestructura.AccesoSP;

namespace EP360_Logistics_Admin.DAL
{
    public class SincronizacionDAL
    {
        public int Iniciar(string ejecutadoPor)
        {
            return Escalar("sync.sp_IniciarSincronizacionAD", P("@ejecutadoPor", ejecutadoPor));
        }

        public void Finalizar(ResultadoSincronizacionModel r)
        {
            Ejecutar("sync.sp_FinalizarSincronizacionAD",
                P("@idSincronizacion", r.IdSincronizacion), P("@estado", r.Estado), P("@leidos", r.Leidos),
                P("@altas", r.Altas), P("@actualizaciones", r.Actualizaciones), P("@omitidos", r.Omitidos),
                P("@errores", r.Errores), P("@detalleErrores", r.DetalleErrores),
                P("@bajas", r.Bajas), P("@reactivaciones", r.Reactivaciones), P("@sinCambios", r.SinCambios));
        }

        // Devuelve la accion realizada: Alta, Actualizacion, Baja, Reactivacion, SinCambios u Omitido.
        // idSincronizacion y motivo hacen que el procedimiento deje en sync.SincronizacionADDetalle cada valor que cambio.
        public string SincronizarUsuario(UsuarioADModel u, bool cumpleReglas, int idSincronizacion, string motivo)
        {
            var resultado = UnoEscritura("sync.sp_SincronizarUsuarioAD", r => new ResultadoSincronizarUsuario { Accion = r.Texto("accion"), IdPersona = r.EnteroNulo("idPersona") },
                P("@objectSid", u.ObjectSid), P("@samAccountName", u.SamAccountName), P("@nombreCompleto", u.NombreCompleto),
                P("@userPrincipalName", u.UserPrincipalName), P("@correoAD", u.Correo), P("@puesto", u.Puesto),
                P("@departamento", u.Departamento), P("@sucursal", u.Sucursal),
                P("@habilitadoAD", u.Habilitado), P("@cumpleReglas", cumpleReglas),
                P("@idSincronizacion", idSincronizacion), P("@motivo", motivo));
            return resultado == null ? null : resultado.Accion;
        }

        // El Replicador compara, entre el 60 y el 11, el texto de la PRIMERA columna que devuelve el SP (idPersona; NULL si se omitio).
        // Guardar aqui la accion (Alta/Actualizacion) hacia que toda actualizacion se marcara Divergente.
        private class ResultadoSincronizarUsuario
        {
            public string Accion;
            public int? IdPersona;
            public override string ToString() { return IdPersona.HasValue ? IdPersona.Value.ToString(CultureInfo.InvariantCulture) : null; }
        }

        // Da de baja a las personas de AD que no se leyeron en esta corrida. Lanza 50033 si la lectura parece incompleta.
        public void DesactivarAusentes(int idSincronizacion, int leidos)
        {
            Ejecutar("sync.sp_DesactivarAusentesAD", P("@idSincronizacion", idSincronizacion), P("@leidos", leidos));
        }

        // Desactiva los departamentos sin personas activas (y lo deja en el detalle de la corrida).
        public void ReconciliarDepartamentos(int idSincronizacion)
        {
            Ejecutar("dir.sp_ReconciliarDepartamentos", P("@idSincronizacion", idSincronizacion));
        }

        // Renglon del detalle que registra la aplicacion (por ahora, el error de un usuario). u siempre trae objectSid
        // (byte[]), asi el parametro viaja como varbinary y el usuario queda marcado como visto en esta corrida.
        public void RegistrarDetalle(int idSincronizacion, UsuarioADModel u, string accion, string motivo)
        {
            Ejecutar("sync.sp_RegistrarDetalleSincronizacionAD", P("@idSincronizacion", idSincronizacion),
                P("@objectSid", u.ObjectSid), P("@usuario", u.SamAccountName),
                P("@nombre", u.NombreCompleto), P("@accion", accion), P("@motivo", motivo));
        }

        public SincronizacionADModel Obtener(int idSincronizacion)
        {
            return Uno("sync.sp_ObtenerSincronizacionAD", MapaCorrida, P("@idSincronizacion", idSincronizacion));
        }

        public List<SincronizacionADDetalleModel> Detalle(int idSincronizacion)
        {
            return Lista("sync.sp_ObtenerDetalleSincronizacionAD", r => new SincronizacionADDetalleModel
            {
                IdDetalle = r.EnteroLargo("idDetalle"),
                Fecha = r.Fecha("fecha").Value,
                Entidad = r.Texto("entidad"),
                IdEntidad = r.EnteroNulo("idEntidad"),
                Nombre = r.Texto("nombre"),
                Usuario = r.Texto("usuario"),
                Accion = r.Texto("accion"),
                Campo = r.Texto("campo"),
                ValorAnterior = r.Texto("valorAnterior"),
                ValorNuevo = r.Texto("valorNuevo"),
                Motivo = r.Texto("motivo")
            }, P("@idSincronizacion", idSincronizacion));
        }

        public List<SincronizacionADModel> Historial(int maximo)
        {
            return Lista("sync.sp_ObtenerSincronizacionesAD", MapaCorrida, P("@maximo", maximo));
        }

        private static SincronizacionADModel MapaCorrida(System.Data.IDataRecord r)
        {
            return new SincronizacionADModel
            {
                IdSincronizacion = r.Entero("idSincronizacion"),
                FechaInicio = r.Fecha("fechaInicio").Value,
                FechaFin = r.Fecha("fechaFin"),
                EjecutadoPor = r.Texto("ejecutadoPor"),
                Estado = r.Texto("estado"),
                Leidos = r.Entero("leidos"),
                Altas = r.Entero("altas"),
                Actualizaciones = r.Entero("actualizaciones"),
                Omitidos = r.Entero("omitidos"),
                Errores = r.Entero("errores"),
                DetalleErrores = r.Texto("detalleErrores"),
                Bajas = r.Entero("bajas"),
                Reactivaciones = r.Entero("reactivaciones"),
                SinCambios = r.Entero("sinCambios"),
                Ausentes = r.Entero("ausentes"),
                RenglonesDetalle = r.Entero("renglonesDetalle")
            };
        }
    }
}
