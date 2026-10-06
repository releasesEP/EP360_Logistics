using System;
using System.Collections.Generic;
using System.Globalization;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;
using static EP360_Logistics_Admin.DAL.Infraestructura.AccesoSP;

namespace EP360_Logistics_Admin.DAL
{
    // Accesos de las personas a los portales (Database/11_AccesosAPortales.sql). Otorgar y revocar son escrituras: pasan por
    // AccesoSP.Escalar / Ejecutar, asi tambien se repiten en el 11.
    public class AccesoPortalDAL
    {
        public List<PortalModel> Portales()
        {
            return Lista("dir.sp_ObtenerPortales", r => new PortalModel
            {
                IdPortal = r.Entero("idPortal"),
                Clave = r.Texto("clave"),
                Nombre = r.Texto("nombre"),
                PermiteExternos = r.Booleano("permiteExternos")
            });
        }

        public List<AccesoPersonaModel> Personas()
        {
            return Lista("dir.sp_ObtenerAccesosPortales", r =>
            {
                var persona = new AccesoPersonaModel
                {
                    IdPersona = r.Entero("idPersona"),
                    TipoPersona = r.Texto("tipoPersona"),
                    NombreCompleto = r.Texto("nombreCompleto"),
                    Correo = r.Texto("correo"),
                    Puesto = r.Texto("puesto"),
                    Departamento = r.Texto("departamento"),
                    SamAccountName = r.Texto("samAccountName"),
                    Cuenta = r.Texto("cuenta")
                };
                string ids = r.Texto("idsPortales");
                if (!string.IsNullOrEmpty(ids))
                {
                    foreach (var parte in ids.Split(','))
                    {
                        int id;
                        if (int.TryParse(parte, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) persona.Portales.Add(id);
                    }
                }
                return persona;
            });
        }

        public List<AccesoDePersonaModel> DePersona(int idPersona)
        {
            return Lista("dir.sp_ObtenerAccesosDePersona", r => new AccesoDePersonaModel
            {
                IdPortal = r.Entero("idPortal"),
                Clave = r.Texto("clave"),
                Nombre = r.Texto("nombre"),
                PermiteExternos = r.Booleano("permiteExternos"),
                TieneAcceso = r.Booleano("tieneAcceso"),
                FechaOtorgado = r.Fecha("fechaOtorgado"),
                OtorgadoPor = r.Texto("otorgadoPor")
            }, P("@idPersona", idPersona));
        }

        public void Otorgar(int idPersona, int idPortal, string usuario)
        {
            Escalar("dir.sp_OtorgarAccesoPortal", P("@idPersona", idPersona), P("@idPortal", idPortal), P("@usuario", usuario));
        }

        public void Revocar(int idPersona, int idPortal, string usuario)
        {
            Ejecutar("dir.sp_RevocarAccesoPortal", P("@idPersona", idPersona), P("@idPortal", idPortal), P("@usuario", usuario));
        }
    }
}
