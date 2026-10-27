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
                P("@errores", r.Errores), P("@detalleErrores", r.DetalleErrores));
        }

        // Devuelve la accion realizada: Alta, Actualizacion u Omitido.
        public string SincronizarUsuario(UsuarioADModel u, bool cumpleReglas)
        {
            var resultado = UnoEscritura("sync.sp_SincronizarUsuarioAD", r => new ResultadoSincronizarUsuario { Accion = r.Texto("accion"), IdPersona = r.EnteroNulo("idPersona") },
                P("@objectSid", u.ObjectSid), P("@samAccountName", u.SamAccountName), P("@nombreCompleto", u.NombreCompleto),
                P("@userPrincipalName", u.UserPrincipalName), P("@correoAD", u.Correo), P("@puesto", u.Puesto),
                P("@departamento", u.Departamento), P("@sucursal", u.Sucursal),
                P("@habilitadoAD", u.Habilitado), P("@cumpleReglas", cumpleReglas));
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

        public List<SincronizacionADModel> Historial(int maximo)
        {
            return Lista("sync.sp_ObtenerSincronizacionesAD", r => new SincronizacionADModel
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
                DetalleErrores = r.Texto("detalleErrores")
            }, P("@maximo", maximo));
        }
    }
}
