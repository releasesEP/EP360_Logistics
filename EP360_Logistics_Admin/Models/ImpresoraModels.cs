using System;
using System.ComponentModel.DataAnnotations;

namespace EP360_Logistics_Admin.Models
{
    // Impresora compartida de documentos del directorio global (dir.ImpresoraCompartida). Cada portal decide si la ocupa.
    public class ImpresoraAdminModel
    {
        public int IdImpresora { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(100, ErrorMessage = "Máximo 100 caracteres.")]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; }

        [Required(ErrorMessage = "La ruta compartida es obligatoria.")]
        [StringLength(260, ErrorMessage = "Máximo 260 caracteres.")]
        [Display(Name = "Ruta compartida")]
        public string RutaCompartida { get; set; }

        [Display(Name = "Sucursal")]
        public int? IdSucursal { get; set; }

        public string Sucursal { get; set; }
        public bool Activo { get; set; } = true;
        public DateTime? FechaCreacion { get; set; }
    }
}
