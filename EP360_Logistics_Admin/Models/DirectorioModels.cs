using System;
using System.ComponentModel.DataAnnotations;

namespace EP360_Logistics_Admin.Models
{
    public class CatalogoItemModel
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public string Pais { get; set; }
    }

    public class GrupoCuentaModel
    {
        public int IdGrupoCuenta { get; set; }
        public string Folio { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(180, ErrorMessage = "Máximo 180 caracteres.")]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; }

        public bool Activo { get; set; } = true;
        public int TotalCuentas { get; set; }
    }

    public class CuentaModel
    {
        public int IdCuenta { get; set; }
        public string Folio { get; set; }

        [Required(ErrorMessage = "El nombre comercial es obligatorio.")]
        [StringLength(180, ErrorMessage = "Máximo 180 caracteres.")]
        [Display(Name = "Nombre comercial")]
        public string NombreComercial { get; set; }

        [StringLength(60)]
        [Display(Name = "Código de cuenta")]
        public string CodigoCuenta { get; set; }

        [StringLength(180)]
        [Display(Name = "Razón social")]
        public string RazonSocial { get; set; }

        [StringLength(20)]
        [Display(Name = "RFC")]
        public string Rfc { get; set; }

        [Display(Name = "Grupo")] public int? IdGrupoCuenta { get; set; }
        public string GrupoCuenta { get; set; }
        [Display(Name = "Ciudad")] public int? IdCiudad { get; set; }
        public string Ciudad { get; set; }
        public string Pais { get; set; }
        [Display(Name = "Sucursal")] public int? IdSucursal { get; set; }
        public string Sucursal { get; set; }
        public bool Activo { get; set; } = true;
    }

    public class PersonaModel
    {
        public int IdPersona { get; set; }

        [Required(ErrorMessage = "Elige el tipo.")]
        [Display(Name = "Tipo")]
        public string TipoPersona { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(150, ErrorMessage = "Máximo 150 caracteres.")]
        [Display(Name = "Nombre completo")]
        public string NombreCompleto { get; set; }

        [StringLength(200)]
        [EmailAddress(ErrorMessage = "Correo no válido.")]
        [Display(Name = "Correo")]
        public string Correo { get; set; }

        [StringLength(40)]
        [Display(Name = "Teléfono")]
        public string Telefono { get; set; }

        [StringLength(20)]
        [Display(Name = "Extensión")]
        public string Extension { get; set; }

        [StringLength(120)]
        [Display(Name = "Puesto")]
        public string Puesto { get; set; }

        [Display(Name = "Departamento")] public int? IdDepartamento { get; set; }
        public string Departamento { get; set; }
        [Display(Name = "Sucursal")] public int? IdSucursal { get; set; }
        public string Sucursal { get; set; }

        public string SamAccountName { get; set; }
        public string UserPrincipalName { get; set; }
        public bool? HabilitadoAD { get; set; }
        public DateTime? FechaUltimaSincronizacion { get; set; }
        public bool Activo { get; set; } = true;
        public DateTime? FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public bool EsAD { get { return TipoPersona == "AD"; } }
    }

    public class PersonaCuentaModel
    {
        public int IdPersonaCuenta { get; set; }
        public int IdPersona { get; set; }
        public string NombrePersona { get; set; }
        public string Correo { get; set; }
        public string TipoPersona { get; set; }
        public string Telefono { get; set; }
        public int IdCuenta { get; set; }
        public string Cuenta { get; set; }
        public string GrupoCuenta { get; set; }
        public string Categoria { get; set; }
        public string PuestoEnCuenta { get; set; }
    }
}

namespace EP360_Logistics_Admin.Models
{
    // Estado de la credencial de un usuario externo (seg.CredencialExterna). Nunca incluye el hash.
    public class CredencialEstadoModel
    {
        public string Estado { get; set; }
        public bool TienePassword { get; set; }
        public System.DateTime? FechaCambioPassword { get; set; }
        public int IntentosFallidos { get; set; }
        public System.DateTime? BloqueadoHasta { get; set; }
    }
}
