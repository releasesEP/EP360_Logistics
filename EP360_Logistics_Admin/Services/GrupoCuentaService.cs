using System.Collections.Generic;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    public class GrupoCuentaService
    {
        private readonly GrupoCuentaDAL _dal = new GrupoCuentaDAL();

        public List<GrupoCuentaModel> Listar(bool incluirInactivos) { return _dal.Listar(incluirInactivos); }
        public GrupoCuentaModel ObtenerPorId(int id) { return _dal.ObtenerPorId(id); }
        public int Crear(GrupoCuentaModel m) { return _dal.Insertar(m.Nombre.Trim()); }
        public void Actualizar(GrupoCuentaModel m) { _dal.Actualizar(m.IdGrupoCuenta, m.Nombre.Trim()); }
        public void Desactivar(int id) { _dal.Desactivar(id); }
        public void Reactivar(int id) { _dal.Reactivar(id); }
    }
}
