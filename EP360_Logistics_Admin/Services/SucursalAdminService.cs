using System.Collections.Generic;
using System.Linq;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    public class SucursalAdminService
    {
        private readonly SucursalDAL _dal = new SucursalDAL();

        public List<SucursalAdminModel> Listar() { return _dal.Listar(); }

        public SucursalAdminModel ObtenerPorId(int id) { return _dal.Listar().FirstOrDefault(s => s.IdSucursal == id); }

        public int Crear(SucursalAdminModel m) { return _dal.Insertar(m.Nombre.Trim()); }

        public void Actualizar(SucursalAdminModel m) { _dal.Actualizar(m.IdSucursal, m.Nombre.Trim()); }

        public void Desactivar(int id) { _dal.Desactivar(id); }

        public void Reactivar(int id) { _dal.Reactivar(id); }
    }
}
