using System;
using System.Collections.Generic;

namespace EP360_Logistics_Admin.Models
{
    public class PortalModel
    {
        public int IdPortal { get; set; }
        public string Clave { get; set; }
        public string Nombre { get; set; }
    }

    public class EstadoBaseModel
    {
        public string BaseDatos { get; set; }
        public string Servidor { get; set; }
        public string Edicion { get; set; }
        public DateTime FechaServidor { get; set; }
        public int TablasDir { get; set; }
        public int TablasSeg { get; set; }
        public int TablasSync { get; set; }
        public int PortalesActivos { get; set; }
        public List<PortalModel> Portales { get; set; } = new List<PortalModel>();

        // Si la base no responde (no existe, sin permisos, SP sin crear), aqui va el motivo.
        public string Error { get; set; }
        public bool Conectado { get { return string.IsNullOrEmpty(Error); } }
    }
}
