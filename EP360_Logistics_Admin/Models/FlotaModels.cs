using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace EP360_Logistics_Admin.Models
{
    public class TransportistaModel
    {
        public int IdTransportista { get; set; }
        public string Folio { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(120, ErrorMessage = "Máximo 120 caracteres.")]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; }

        [StringLength(180)]
        [Display(Name = "Razón social")]
        public string RazonSocial { get; set; }

        [StringLength(20)]
        [Display(Name = "RFC")]
        public string Rfc { get; set; }

        [Display(Name = "Cuenta del directorio")] public int? IdCuenta { get; set; }
        public string Cuenta { get; set; }

        public int TotalChoferes { get; set; }
        public int TotalTractores { get; set; }
        public int TotalCajas { get; set; }
        public bool Activo { get; set; } = true;
    }

    public class TractorModel
    {
        public int IdTractor { get; set; }

        [Required(ErrorMessage = "Elige el transportista.")]
        [Display(Name = "Transportista")]
        public int? IdTransportista { get; set; }
        public string Transportista { get; set; }

        [Required(ErrorMessage = "El número económico es obligatorio.")]
        [StringLength(30, ErrorMessage = "Máximo 30 caracteres.")]
        [Display(Name = "Económico")]
        public string Economico { get; set; }

        [StringLength(20)]
        [Display(Name = "Placa")]
        public string Placa { get; set; }

        [StringLength(20)]
        [Display(Name = "VIN")]
        public string Vin { get; set; }

        [Range(1950, 2100, ErrorMessage = "Año no válido.")]
        [Display(Name = "Año")]
        public int? Anio { get; set; }

        [StringLength(60)]
        [Display(Name = "Marca")]
        public string Marca { get; set; }

        public bool Activo { get; set; } = true;
    }

    public class ChoferModel
    {
        public int IdChofer { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(150, ErrorMessage = "Máximo 150 caracteres.")]
        [Display(Name = "Nombre completo")]
        public string NombreCompleto { get; set; }

        [StringLength(30)]
        [Display(Name = "Licencia")]
        public string Licencia { get; set; }

        [StringLength(40)]
        [Display(Name = "Teléfono")]
        public string Telefono { get; set; }

        // Solo al crear: primer transportista con el que se liga.
        [Display(Name = "Transportista")] public int? IdTransportista { get; set; }

        public string Transportistas { get; set; }

        // El SP devuelve los transportistas activos ya unidos con ", " (STRING_AGG).
        public System.Collections.Generic.List<string> ListaTransportistas
        {
            get { return (Transportistas ?? "").Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries).ToList(); }
        }

        public bool Activo { get; set; } = true;
        public DateTime? FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }

    public class ChoferTransportistaModel
    {
        public int IdChoferTransportista { get; set; }
        public int IdTransportista { get; set; }
        public string Transportista { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public bool Activo { get; set; }
    }

    public class CajaModel
    {
        public const string Cliente = "Cliente", DeTransportista = "Transportista", EP = "EP";

        public int IdCaja { get; set; }

        [Required(ErrorMessage = "El número de caja es obligatorio.")]
        [StringLength(30, ErrorMessage = "Máximo 30 caracteres.")]
        [Display(Name = "Número de caja")]
        public string NumeroCaja { get; set; }

        [Required(ErrorMessage = "Elige de quién es la caja.")]
        [Display(Name = "Propiedad")]
        public string TipoPropiedad { get; set; } = Cliente;

        [Display(Name = "Cuenta")] public int? IdCuenta { get; set; }
        public string Cuenta { get; set; }
        [Display(Name = "Grupo de cuentas")] public int? IdGrupoCuenta { get; set; }
        public string GrupoCuenta { get; set; }
        [Display(Name = "Transportista")] public int? IdTransportista { get; set; }
        public string Transportista { get; set; }

        [StringLength(20)]
        [Display(Name = "Placa")]
        public string Placa { get; set; }

        [StringLength(20)]
        [Display(Name = "VIN")]
        public string Vin { get; set; }

        [Range(1950, 2100, ErrorMessage = "Año no válido.")]
        [Display(Name = "Año")]
        public int? Anio { get; set; }

        [StringLength(60)]
        [Display(Name = "Marca")]
        public string Marca { get; set; }

        [Range(1, 255, ErrorMessage = "Pies no válidos.")]
        [Display(Name = "Pies")]
        public int? Pies { get; set; }

        public bool Activo { get; set; } = true;
        public DateTime? FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public string Resumen
        {
            get
            {
                var partes = new[] { Anio.HasValue ? Anio.Value.ToString() : null, Marca, Pies.HasValue ? Pies.Value + " ft" : null };
                return string.Join(" · ", partes.Where(p => !string.IsNullOrEmpty(p)));
            }
        }

        public string Propietario
        {
            get
            {
                if (TipoPropiedad == EP) return "EP LOGISTICS";
                return GrupoCuenta ?? Cuenta ?? Transportista;
            }
        }
    }
}
