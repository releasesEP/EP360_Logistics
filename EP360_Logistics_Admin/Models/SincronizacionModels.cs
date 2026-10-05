using System;

namespace EP360_Logistics_Admin.Models
{
    // Un usuario leido de Active Directory.
    public class UsuarioADModel
    {
        public byte[] ObjectSid { get; set; }
        public string SamAccountName { get; set; }
        public string NombreCompleto { get; set; }
        public string UserPrincipalName { get; set; }
        public string Correo { get; set; }
        public string Departamento { get; set; }
        public string Sucursal { get; set; }
        public string Puesto { get; set; }
        public string Compania { get; set; }
        public string Jefe { get; set; }
        public bool Habilitado { get; set; }
    }

    // Resultado de una corrida (lo que se muestra al terminar y lo que se guarda en la bitacora).
    public class ResultadoSincronizacionModel
    {
        public int IdSincronizacion { get; set; }
        public string Estado { get; set; }
        public int Leidos { get; set; }
        public int Altas { get; set; }
        public int Actualizaciones { get; set; }
        public int Omitidos { get; set; }
        public int Errores { get; set; }
        public string DetalleErrores { get; set; }

        // Solo para el aviso al terminar (no se guarda): cuantos NO cumplieron cada regla.
        public int Bajas { get; set; }
        public int NoCumplenReglas { get; set; }
        public int CuentasDeSistema { get; set; }
        public System.Collections.Generic.Dictionary<string, int> FaltantesPorRegla { get; set; } = new System.Collections.Generic.Dictionary<string, int>();
    }

    public class SincronizacionADModel
    {
        public int IdSincronizacion { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public string EjecutadoPor { get; set; }
        public string Estado { get; set; }
        public int Leidos { get; set; }
        public int Altas { get; set; }
        public int Actualizaciones { get; set; }
        public int Omitidos { get; set; }
        public int Errores { get; set; }
        public string DetalleErrores { get; set; }
    }
}
