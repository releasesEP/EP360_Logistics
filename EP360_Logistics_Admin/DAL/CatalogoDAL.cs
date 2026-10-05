using System.Collections.Generic;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;
using static EP360_Logistics_Admin.DAL.Infraestructura.AccesoSP;

namespace EP360_Logistics_Admin.DAL
{
    public class CatalogoDAL
    {
        public List<CatalogoItemModel> Departamentos()
        {
            return Lista("dir.sp_ObtenerDepartamentos",
                r => new CatalogoItemModel { Id = r.Entero("idDepartamento"), Nombre = r.Texto("nombre") },
                P("@incluirInactivos", false));
        }

        public List<CatalogoItemModel> Sucursales()
        {
            return Lista("dir.sp_ObtenerSucursales",
                r => new CatalogoItemModel { Id = r.Entero("idSucursal"), Nombre = r.Texto("nombre") },
                P("@incluirInactivos", false));
        }

        public List<CatalogoItemModel> Ciudades()
        {
            return Lista("dir.sp_ObtenerCiudades",
                r => new CatalogoItemModel { Id = r.Entero("idCiudad"), Nombre = r.Texto("nombre"), Pais = r.Texto("pais") },
                P("@incluirInactivos", false));
        }
    }
}
