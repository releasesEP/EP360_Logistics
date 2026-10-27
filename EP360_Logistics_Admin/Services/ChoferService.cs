using System.Collections.Generic;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    public class ChoferService
    {
        private readonly ChoferDAL _dal = new ChoferDAL();

        public List<ChoferModel> Listar(bool incluirInactivos, int? idTransportista, string buscar)
        {
            return _dal.Listar(incluirInactivos, idTransportista, buscar);
        }

        public ChoferModel ObtenerPorId(int id) { return _dal.ObtenerPorId(id); }
        public List<ChoferTransportistaModel> TransportistasDeChofer(int idChofer) { return _dal.TransportistasDeChofer(idChofer); }

        public int Crear(ChoferModel m) { Normalizar(m); return _dal.Insertar(m); }
        public void Actualizar(ChoferModel m) { Normalizar(m); _dal.Actualizar(m); }
        public void Desactivar(int id) { _dal.Desactivar(id); }
        public void Reactivar(int id) { _dal.Reactivar(id); }

        public void Vincular(int idChofer, int idTransportista) { _dal.Vincular(idChofer, idTransportista); }
        public void Desvincular(int idChofer, int idTransportista) { _dal.Desvincular(idChofer, idTransportista); }

        private static void Normalizar(ChoferModel m)
        {
            m.NombreCompleto = m.NombreCompleto.Trim();
            m.Licencia = Vacio(m.Licencia);
            m.Telefono = Vacio(m.Telefono);
        }

        private static string Vacio(string s) { return string.IsNullOrWhiteSpace(s) ? null : s.Trim(); }
    }
}
