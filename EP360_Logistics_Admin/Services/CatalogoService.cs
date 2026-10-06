using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    // Listas desplegables de catalogos (solo activos).
    public class CatalogoService
    {
        private readonly CatalogoDAL _dal = new CatalogoDAL();
        private readonly GrupoCuentaDAL _grupoDal = new GrupoCuentaDAL();
        private readonly CuentaDAL _cuentaDal = new CuentaDAL();
        private readonly TransportistaDAL _transportistaDal = new TransportistaDAL();

        public List<SelectListItem> Departamentos(int? seleccionado = null)
        {
            return _dal.Departamentos().Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Nombre, Selected = x.Id == seleccionado }).ToList();
        }

        public List<SelectListItem> Sucursales(int? seleccionado = null)
        {
            return _dal.Sucursales().Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Nombre, Selected = x.Id == seleccionado }).ToList();
        }

        public List<SelectListItem> Ciudades(int? seleccionado = null)
        {
            return _dal.Ciudades().Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Nombre + " (" + x.Pais + ")", Selected = x.Id == seleccionado }).ToList();
        }

        public List<SelectListItem> Grupos(int? seleccionado = null)
        {
            return _grupoDal.Listar(false).Select(x => new SelectListItem { Value = x.IdGrupoCuenta.ToString(), Text = x.Nombre, Selected = x.IdGrupoCuenta == seleccionado }).ToList();
        }

        public List<SelectListItem> Cuentas(int? seleccionado = null)
        {
            return _cuentaDal.Listar(false, null, null).Select(x => new SelectListItem { Value = x.IdCuenta.ToString(), Text = x.NombreComercial, Selected = x.IdCuenta == seleccionado }).ToList();
        }

        public List<SelectListItem> Transportistas(int? seleccionado = null)
        {
            return _transportistaDal.Listar(false, null).Select(x => new SelectListItem { Value = x.IdTransportista.ToString(), Text = x.Nombre, Selected = x.IdTransportista == seleccionado }).ToList();
        }

        public List<SelectListItem> TiposPropiedadCaja(string seleccionado = null)
        {
            return new[] { CajaModel.Cliente, CajaModel.DeTransportista, CajaModel.EP }
                .Select(x => new SelectListItem { Value = x, Text = x == CajaModel.EP ? "EP (propia)" : x, Selected = x == seleccionado }).ToList();
        }
    }
}
