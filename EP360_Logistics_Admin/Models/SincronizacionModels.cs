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
        // Atributo ipPhone de AD: la extension de un usuario de AD sale solo de ahi (2026-11-09).
        public string Extension { get; set; }
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

        public int Bajas { get; set; }
        public int Reactivaciones { get; set; }
        public int SinCambios { get; set; }
        // Personas de AD dadas de baja porque ya no existen en AD (las cuenta sync.sp_DesactivarAusentesAD).
        public int Ausentes { get; set; }

        // Solo para el aviso al terminar (no se guarda): cuantos NO cumplieron cada regla.
        public int NoCumplenReglas { get; set; }
        public int CuentasDeSistema { get; set; }

        // Sucursales que la sincronizacion retiro por quedar sin personas ni cuentas (o volvio a activar), por nombre.
        public System.Collections.Generic.List<string> SucursalesDesactivadas { get; set; } = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> SucursalesReactivadas { get; set; } = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> DepartamentosDesactivados { get; set; } = new System.Collections.Generic.List<string>();
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
        public int Bajas { get; set; }
        public int Reactivaciones { get; set; }
        public int SinCambios { get; set; }
        public int Ausentes { get; set; }
        // Renglones en sync.SincronizacionADDetalle. 0 en las corridas anteriores al registro detallado (script 16).
        public int RenglonesDetalle { get; set; }
        public bool TieneDetalle { get { return RenglonesDetalle > 0; } }
    }

    // Un renglon de lo que hizo una corrida (Database/16_SincronizacionADDetalle.sql).
    public class SincronizacionADDetalleModel
    {
        public long IdDetalle { get; set; }
        public DateTime Fecha { get; set; }
        public string Entidad { get; set; }
        public int? IdEntidad { get; set; }
        public string Nombre { get; set; }
        public string Usuario { get; set; }
        public string Accion { get; set; }
        public string Campo { get; set; }
        public string ValorAnterior { get; set; }
        public string ValorNuevo { get; set; }
        public string Motivo { get; set; }
    }

    // Pantalla de detalle de una corrida: la corrida, sus renglones y el filtro elegido.
    public class SincronizacionADDetalleVistaModel
    {
        public SincronizacionADModel Corrida { get; set; }
        public System.Collections.Generic.List<SincronizacionADDetalleModel> Renglones { get; set; } = new System.Collections.Generic.List<SincronizacionADDetalleModel>();
        public string Filtro { get; set; }
        public string Buscar { get; set; }
        public int? Anterior { get; set; }
        public int? Siguiente { get; set; }
    }
}
