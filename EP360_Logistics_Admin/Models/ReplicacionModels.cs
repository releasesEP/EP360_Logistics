using System;
using System.Collections.Generic;

namespace EP360_Logistics_Admin.Models
{
    public class ResumenReplicacionModel
    {
        public int Pendientes { get; set; }
        public int Divergentes { get; set; }
        public int Aplicadas { get; set; }
        public DateTime? PendienteMasAntigua { get; set; }
        public DateTime? UltimaAplicada { get; set; }
    }

    // Operacion con problema (Pendiente o Divergente).
    public class ErrorReplicacionModel
    {
        public long IdCola { get; set; }
        public DateTime FechaCreacion { get; set; }
        public string Procedimiento { get; set; }
        public string Descripcion { get; set; }
        public string Estado { get; set; }
        public int Intentos { get; set; }
        public string UltimoError { get; set; }
        public string Usuario { get; set; }
        public DateTime? FechaUltimoIntento { get; set; }
    }

    // Operacion ya resuelta (aplicada en el 11 u omitida a mano).
    public class HistorialReplicacionModel
    {
        public long IdCola { get; set; }
        public DateTime FechaCreacion { get; set; }
        public string Procedimiento { get; set; }
        public string Descripcion { get; set; }
        public string Estado { get; set; }
        public string Usuario { get; set; }
        public string ResueltaPor { get; set; }
        public DateTime? FechaAplicada { get; set; }
    }

    public class OperacionReplicacionModel
    {
        public long IdCola { get; set; }
        public DateTime FechaCreacion { get; set; }
        public string Procedimiento { get; set; }
        public string Descripcion { get; set; }
        public string Estado { get; set; }
        public int Intentos { get; set; }
        public string UltimoError { get; set; }
        public string QueSignifica { get; set; }
        public string QueHacer { get; set; }
        public string ResultadoMaestro { get; set; }
        public string Usuario { get; set; }
        public string ResueltaPor { get; set; }
        public DateTime? FechaUltimoIntento { get; set; }
        public DateTime? FechaAplicada { get; set; }
        public List<KeyValuePair<string, string>> Parametros { get; set; }
        public bool PuedeOmitir { get { return Estado == "Pendiente" || Estado == "Divergente"; } }
    }

    public class ConteoTablaModel
    {
        public string Tabla { get; set; }
        public int Filas { get; set; }
        public long? UltimoId { get; set; }
    }

    public class ComparativoTablaModel
    {
        public string Tabla { get; set; }
        public ConteoTablaModel Maestro { get; set; }
        public ConteoTablaModel Copia { get; set; }
        public bool Coincide
        {
            get { return Maestro != null && Copia != null && Maestro.Filas == Copia.Filas && Maestro.UltimoId == Copia.UltimoId; }
        }

        // "ok" | "info" | "aviso" | "error": decide el color de la insignia.
        public string Clase { get; set; }
        public string Resumen { get; set; }          // lo que pasa, en palabras
        public string Sugerencia { get; set; }       // que hacer
        // Mismas filas pero el contador de ids del 11 va atrasado: se arregla con el boton "Alinear contadores".
        public bool PuedeAlinear { get; set; }
    }

    public class EstadoReplicacionModel
    {
        public EstadoReplicacionModel()
        {
            Errores = new List<ErrorReplicacionModel>();
            Comparativo = new List<ComparativoTablaModel>();
            Historial = new List<HistorialReplicacionModel>();
        }

        public bool Configurada { get; set; }
        public ResumenReplicacionModel Resumen { get; set; }
        public List<ErrorReplicacionModel> Errores { get; set; }
        public List<HistorialReplicacionModel> Historial { get; set; }
        public List<ComparativoTablaModel> Comparativo { get; set; }
        // Problemas de lectura (script 10 sin correr, 11 inaccesible...) para mostrarlos en vez de tronar la pantalla.
        public string ErrorMaestro { get; set; }
        public string ErrorCopia { get; set; }
        public string UltimoErrorLocal { get; set; }
        public DateTime? FechaUltimoErrorLocal { get; set; }

        // Resumen general: "ok" | "info" | "aviso" | "error", con su explicacion y lo que sigue.
        public string ClaseGeneral { get; set; }
        public string TituloGeneral { get; set; }
        public string ExplicacionGeneral { get; set; }
        public List<string> PasosSugeridos { get; set; }

        // Hay tablas con las mismas filas y el contador del 11 atrasado, y no hay operaciones por aplicar (si las hubiera, alinear
        // desfasaria los ids: primero deben aplicarse).
        public bool PuedeAlinearContadores { get; set; }

        public bool Sincronizada
        {
            get
            {
                return Configurada && ErrorMaestro == null && ErrorCopia == null && Resumen != null
                       && Resumen.Pendientes == 0 && Resumen.Divergentes == 0 && Comparativo.Count > 0 && Comparativo.TrueForAll(c => c.Coincide);
            }
        }
    }
}
