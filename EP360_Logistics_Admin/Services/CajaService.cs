using System.Collections.Generic;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    public class CajaService
    {
        private readonly CajaDAL _dal = new CajaDAL();

        public List<CajaModel> Listar(bool incluirInactivos, string tipoPropiedad, int? idGrupoCuenta, int? idTransportista, string buscar)
        {
            return _dal.Listar(incluirInactivos, tipoPropiedad, null, idGrupoCuenta, idTransportista, buscar);
        }

        // Todas (activas e inactivas): los listados filtran, ordenan y paginan en memoria.
        public List<CajaModel> ListarTodas() { return _dal.Listar(true, null, null, null, null, null); }

        public List<CajaModel> DeTransportista(int idTransportista) { return _dal.Listar(false, null, null, null, idTransportista, null); }
        public CajaModel ObtenerPorId(int id) { return _dal.ObtenerPorId(id); }

        public int Crear(CajaModel m) { Normalizar(m); return _dal.Insertar(m); }
        public void Actualizar(CajaModel m) { Normalizar(m); _dal.Actualizar(m); }
        public void Desactivar(int id) { _dal.Desactivar(id); }
        public void Reactivar(int id) { _dal.Reactivar(id); }

        // El formulario manda todos los campos; se limpian los que no aplican al tipo de propiedad
        // para que el SP no rechace una caja de transportista o de EP que trae cuenta/grupo.
        private static void Normalizar(CajaModel m)
        {
            m.NumeroCaja = m.NumeroCaja.Trim();
            m.Placa = Vacio(m.Placa);
            m.Vin = Vacio(m.Vin);
            m.Marca = Vacio(m.Marca);
            if (m.TipoPropiedad != CajaModel.Cliente)
            {
                m.IdCuenta = null;
                m.IdGrupoCuenta = null;
            }
        }

        private static string Vacio(string s) { return string.IsNullOrWhiteSpace(s) ? null : s.Trim(); }
    }
}
