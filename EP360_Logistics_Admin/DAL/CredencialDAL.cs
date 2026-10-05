using System.Linq;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;
using static EP360_Logistics_Admin.DAL.Infraestructura.AccesoSP;

namespace EP360_Logistics_Admin.DAL
{
    public class CredencialDAL
    {
        // seg.sp_ObtenerEstadoCredencial (script 09): nunca devuelve el hash. Null = la persona no tiene credencial.
        public CredencialEstadoModel Obtener(int idPersona)
        {
            return Lista("seg.sp_ObtenerEstadoCredencial", r => new CredencialEstadoModel
            {
                Estado = r.Texto("estado"),
                TienePassword = r.Booleano("tienePassword"),
                FechaCambioPassword = r.Fecha("fechaCambioPassword"),
                IntentosFallidos = r.Entero("intentosFallidos"),
                BloqueadoHasta = r.Fecha("bloqueadoHasta")
            }, P("@idPersona", idPersona)).FirstOrDefault();
        }

        // seg.sp_FijarPasswordCredencial (script 08): recibe el HASH, nunca la contrasena.
        public void FijarPassword(int idPersona, string passwordHash)
        {
            Escalar("seg.sp_FijarPasswordCredencial", P("@idPersona", idPersona), P("@passwordHash", passwordHash), P("@idPersonaModifico", null));
        }
    }
}
