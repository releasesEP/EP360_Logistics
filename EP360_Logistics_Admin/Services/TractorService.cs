using System.Collections.Generic;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    public class TractorService
    {
        private readonly TractorDAL _dal = new TractorDAL();

        public List<TractorModel> Listar(bool incluirInactivos, int? idTransportista, string buscar)
        {
            return _dal.Listar(incluirInactivos, idTransportista, buscar);
        }

        public TractorModel ObtenerPorId(int id) { return _dal.ObtenerPorId(id); }

        public int Crear(TractorModel m) { Normalizar(m); return _dal.Insertar(m); }
        public void Actualizar(TractorModel m) { Normalizar(m); _dal.Actualizar(m); }
        public void Desactivar(int id) { _dal.Desactivar(id); }
        public void Reactivar(int id) { _dal.Reactivar(id); }

        private static void Normalizar(TractorModel m)
        {
            m.Economico = m.Economico.Trim();
            m.Placa = Vacio(m.Placa);
            m.Vin = Vacio(m.Vin);
            m.Marca = Vacio(m.Marca);
        }

        private static string Vacio(string s) { return string.IsNullOrWhiteSpace(s) ? null : s.Trim(); }
    }
}
