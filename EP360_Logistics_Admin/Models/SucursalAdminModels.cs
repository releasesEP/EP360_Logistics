using System;
using System.ComponentModel.DataAnnotations;

namespace EP360_Logistics_Admin.Models
{
    // Sucursal del directorio global (dir.Sucursal) con cuantas personas y cuentas activas la tienen asignada.
    public class SucursalAdminModel
    {
        public int IdSucursal { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(100, ErrorMessage = "Máximo 100 caracteres.")]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; }

        public bool Activo { get; set; } = true;
        public DateTime? FechaCreacion { get; set; }
        public int Personas { get; set; }
        public int Cuentas { get; set; }
    }
}
