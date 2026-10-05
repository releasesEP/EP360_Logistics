using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.DAL;

namespace EP360_Logistics_Admin.Services
{
    // Listas desplegables de catalogos (solo activos).
    public class CatalogoService
    {
        private readonly CatalogoDAL _dal = new CatalogoDAL();
        private readonly GrupoCuentaDAL _grupoDal = new GrupoCuentaDAL();

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
    }
}
