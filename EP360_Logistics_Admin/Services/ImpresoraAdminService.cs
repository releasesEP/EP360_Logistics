using System.Collections.Generic;
using System.Linq;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    public class ImpresoraAdminService
    {
        private readonly ImpresoraDAL _dal = new ImpresoraDAL();

        public List<ImpresoraAdminModel> Listar() { return _dal.Listar(); }

        public ImpresoraAdminModel ObtenerPorId(int id) { return _dal.Listar().FirstOrDefault(i => i.IdImpresora == id); }

        public int Guardar(ImpresoraAdminModel m)
        {
            return _dal.Guardar(m.IdImpresora == 0 ? (int?)null : m.IdImpresora, m.Nombre.Trim(), m.RutaCompartida.Trim(), m.IdSucursal);
        }

        public void Desactivar(int id) { _dal.Desactivar(id); }

        public void Reactivar(int id) { _dal.Reactivar(id); }
    }
}
