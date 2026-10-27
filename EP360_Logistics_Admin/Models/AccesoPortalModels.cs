using System.Collections.Generic;

namespace EP360_Logistics_Admin.Models
{
    // Una persona (AD o externa) con los portales a los que tiene acceso.
    public class AccesoPersonaModel
    {
        public AccesoPersonaModel() { Portales = new HashSet<int>(); }

        public int IdPersona { get; set; }
        public string TipoPersona { get; set; }
        public string NombreCompleto { get; set; }
        public string Correo { get; set; }
        public string Puesto { get; set; }
        public string Departamento { get; set; }
        public string SamAccountName { get; set; }
        public string Cuenta { get; set; }
        public HashSet<int> Portales { get; private set; }
    }

    // Acceso de una persona a un portal (ficha de la persona).
    public class AccesoDePersonaModel
    {
        public int IdPortal { get; set; }
        public string Clave { get; set; }
        public string Nombre { get; set; }
        public bool PermiteExternos { get; set; }
        public bool TieneAcceso { get; set; }
        public System.DateTime? FechaOtorgado { get; set; }
        public string OtorgadoPor { get; set; }
    }
}
