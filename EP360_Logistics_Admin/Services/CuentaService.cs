using System.Collections.Generic;
using System.Linq;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    public class CuentaService
    {
        private readonly CuentaDAL _dal = new CuentaDAL();
        private readonly PersonaDAL _personaDal = new PersonaDAL();
        private readonly CajaDAL _cajaDal = new CajaDAL();

        public List<CuentaModel> Listar(bool incluirInactivos, int? idGrupoCuenta, string buscar)
        {
            return _dal.Listar(incluirInactivos, idGrupoCuenta, buscar);
        }

        public CuentaModel ObtenerPorId(int id) { return _dal.ObtenerPorId(id); }
        public List<PersonaCuentaModel> PersonasDeCuenta(int idCuenta) { return _personaDal.PersonasDeCuenta(idCuenta); }

        // Cajas activas que le tocan a la cuenta: las ligadas a ella y las de su grupo.
        public List<CajaModel> CajasDeCuenta(CuentaModel cuenta)
        {
            var cajas = _cajaDal.Listar(false, null, cuenta.IdCuenta, null, null, null);
            if (cuenta.IdGrupoCuenta.HasValue)
                cajas.AddRange(_cajaDal.Listar(false, null, null, cuenta.IdGrupoCuenta, null, null).Where(k => k.IdGrupoCuenta == cuenta.IdGrupoCuenta));
            return cajas.GroupBy(k => k.IdCaja).Select(g => g.First()).OrderBy(k => k.NumeroCaja).ToList();
        }

        public int Crear(CuentaModel m) { Normalizar(m); return _dal.Insertar(m); }
        public void Actualizar(CuentaModel m) { Normalizar(m); _dal.Actualizar(m); }
        public void Desactivar(int id) { _dal.Desactivar(id); }
        public void Reactivar(int id) { _dal.Reactivar(id); }

        public void AsignarGrupo(int idCuenta, int idGrupoCuenta) { _dal.AsignarGrupo(idCuenta, idGrupoCuenta); }
        public void QuitarGrupo(int idCuenta) { _dal.AsignarGrupo(idCuenta, null); }

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
