using System.Collections.Generic;

namespace EP360_Logistics_Admin.Models
{
    // Una fila de las barras del tablero (ej. cajas por grupo).
    public class BarraModel
    {
        public string Etiqueta { get; set; }
        public int Valor { get; set; }
        public string Url { get; set; }
        public int? Id { get; set; }
    }

    public class InicioModel
    {
        public EstadoBaseModel Estado { get; set; }

        // Directorio
        public int PersonasActivas { get; set; }
        public int UsuariosAD { get; set; }
        public int UsuariosExternos { get; set; }
        public int Contactos { get; set; }
        public int CuentasActivas { get; set; }
        public int CuentasSinGrupo { get; set; }
        public int GruposActivos { get; set; }

        // Flota
        public int CajasActivas { get; set; }
        public int CajasCliente { get; set; }
        public int CajasTransportista { get; set; }
        public int CajasEP { get; set; }
        public int TransportistasActivos { get; set; }
        public int TractoresActivos { get; set; }
        public int ChoferesActivos { get; set; }
        public int ChoferesVariosTransportistas { get; set; }

        public List<BarraModel> CajasPorGrupo { get; set; } = new List<BarraModel>();
        public List<BarraModel> TransportistasPorChoferes { get; set; } = new List<BarraModel>();
        public List<BarraModel> PersonasPorSucursal { get; set; } = new List<BarraModel>();

        public SincronizacionADModel UltimaSincronizacion { get; set; }

        // Si alguna lectura falla, el tablero se muestra con lo que haya y este aviso.
        public string ErrorIndicadores { get; set; }
    }
}
