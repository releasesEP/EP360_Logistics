using System;
using System.Collections.Generic;
using System.Linq;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    // Quien entra a que portal. El portal decide despues que puede hacer cada quien (permisos por modulo).
    public class AccesoPortalService
    {
        private readonly AccesoPortalDAL _dal = new AccesoPortalDAL();

        public List<PortalModel> Portales() { return _dal.Portales(); }

        public List<AccesoPersonaModel> Personas() { return _dal.Personas(); }

        public List<AccesoDePersonaModel> DePersona(int idPersona) { return _dal.DePersona(idPersona); }

        public void Otorgar(int idPersona, int idPortal, string usuario) { _dal.Otorgar(idPersona, idPortal, usuario); }

        public void Revocar(int idPersona, int idPortal, string usuario) { _dal.Revocar(idPersona, idPortal, usuario); }

        // Filtro comun de la pantalla y de la accion masiva (lo que se ve es exactamente a lo que se aplica).
        // acceso = "con" | "sin": con idPortal se refiere a ese portal; sin idPortal, a cualquier portal.
        public static List<AccesoPersonaModel> Filtrar(IEnumerable<AccesoPersonaModel> todas, string buscar, string tipo, int? idPortal, string acceso)
        {
            return todas.Where(p =>
                    (string.IsNullOrEmpty(tipo) || p.TipoPersona == tipo) &&
                    TextoUtil.Contiene(buscar, p.NombreCompleto, p.Correo, p.SamAccountName, p.Puesto, p.Departamento, p.Cuenta) &&
                    CumpleAcceso(p, idPortal, acceso))
                .ToList();
        }

        private static bool CumpleAcceso(AccesoPersonaModel p, int? idPortal, string acceso)
        {
            if (acceso != "con" && acceso != "sin") return true;
            bool tiene = idPortal.HasValue ? p.Portales.Contains(idPortal.Value) : p.Portales.Count > 0;
            return acceso == "con" ? tiene : !tiene;
        }

        // Un externo solo puede entrar a portales publicos.
        public static bool PuedeEntrar(AccesoPersonaModel persona, PortalModel portal)
        {
            return persona.TipoPersona != "Externo" || portal.PermiteExternos;
        }

        // Da el acceso a todas las personas de la lista que aun no lo tienen y que pueden tenerlo. Devuelve cuantas.
        public int OtorgarALista(IEnumerable<AccesoPersonaModel> personas, PortalModel portal, string usuario)
        {
            int otorgados = 0;
            foreach (var persona in personas)
            {
                if (persona.Portales.Contains(portal.IdPortal) || !PuedeEntrar(persona, portal)) continue;
                _dal.Otorgar(persona.IdPersona, portal.IdPortal, usuario);
                otorgados++;
            }
            return otorgados;
        }
    }
}
