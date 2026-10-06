using System.Collections.Generic;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    public class TransportistaService
    {
        private readonly TransportistaDAL _dal = new TransportistaDAL();

        public List<TransportistaModel> Listar(bool incluirInactivos, string buscar) { return _dal.Listar(incluirInactivos, buscar); }
        public TransportistaModel ObtenerPorId(int id) { return _dal.ObtenerPorId(id); }

        public int Crear(TransportistaModel m) { Normalizar(m); return _dal.Insertar(m); }
        public void Actualizar(TransportistaModel m) { Normalizar(m); _dal.Actualizar(m); }
        public void Desactivar(int id) { _dal.Desactivar(id); }
        public void Reactivar(int id) { _dal.Reactivar(id); }

        private static void Normalizar(TransportistaModel m)
        {
            m.Nombre = m.Nombre.Trim();
            m.RazonSocial = Vacio(m.RazonSocial);
            m.Rfc = Vacio(m.Rfc);
        }

        private static string Vacio(string s) { return string.IsNullOrWhiteSpace(s) ? null : s.Trim(); }
    }
}
