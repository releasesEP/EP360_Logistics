using System.Collections.Generic;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    public class CuentaService
    {
        private readonly CuentaDAL _dal = new CuentaDAL();
        private readonly PersonaDAL _personaDal = new PersonaDAL();

        public List<CuentaModel> Listar(bool incluirInactivos, int? idGrupoCuenta, string buscar)
        {
            return _dal.Listar(incluirInactivos, idGrupoCuenta, buscar);
        }

        public CuentaModel ObtenerPorId(int id) { return _dal.ObtenerPorId(id); }
        public List<PersonaCuentaModel> PersonasDeCuenta(int idCuenta) { return _personaDal.PersonasDeCuenta(idCuenta); }

        public int Crear(CuentaModel m) { Normalizar(m); return _dal.Insertar(m); }
        public void Actualizar(CuentaModel m) { Normalizar(m); _dal.Actualizar(m); }
        public void Desactivar(int id) { _dal.Desactivar(id); }
        public void Reactivar(int id) { _dal.Reactivar(id); }

        private static void Normalizar(CuentaModel m)
        {
            m.NombreComercial = m.NombreComercial.Trim();
            m.CodigoCuenta = Vacio(m.CodigoCuenta);
            m.RazonSocial = Vacio(m.RazonSocial);
            m.Rfc = Vacio(m.Rfc);
        }

        private static string Vacio(string s) { return string.IsNullOrWhiteSpace(s) ? null : s.Trim(); }
    }
}
