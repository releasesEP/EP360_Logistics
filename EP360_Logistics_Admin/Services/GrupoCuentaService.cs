using System.Collections.Generic;
using System.Linq;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    public class GrupoCuentaService
    {
        private readonly GrupoCuentaDAL _dal = new GrupoCuentaDAL();
        private readonly CuentaDAL _cuentaDal = new CuentaDAL();
        private readonly CajaDAL _cajaDal = new CajaDAL();
        private readonly PersonaDAL _personaDal = new PersonaDAL();

        public List<GrupoCuentaModel> Listar(bool incluirInactivos) { return _dal.Listar(incluirInactivos); }
        public GrupoCuentaModel ObtenerPorId(int id) { return _dal.ObtenerPorId(id); }
        public int Crear(GrupoCuentaModel m) { return _dal.Insertar(m.Nombre.Trim()); }
        public void Actualizar(GrupoCuentaModel m) { _dal.Actualizar(m.IdGrupoCuenta, m.Nombre.Trim()); }
        public void Desactivar(int id) { _dal.Desactivar(id); }
        public void Reactivar(int id) { _dal.Reactivar(id); }

        // Cajas activas ligadas directo a cada grupo (una sola llamada para todo el listado).
        public Dictionary<int, int> CajasPorGrupo()
        {
            return _cajaDal.Listar(false, CajaModel.Cliente, null, null, null, null)
                .Where(k => k.IdGrupoCuenta.HasValue)
                .GroupBy(k => k.IdGrupoCuenta.Value)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        public List<CuentaModel> CuentasDelGrupo(int idGrupoCuenta) { return _cuentaDal.Listar(true, idGrupoCuenta, null); }

        // Cajas activas del grupo: las ligadas al grupo y las de cada una de sus cuentas.
        public List<CajaModel> CajasDelGrupo(int idGrupoCuenta) { return _cajaDal.Listar(false, null, null, idGrupoCuenta, null, null); }

        public List<CuentaModel> CuentasSinGrupo()
        {
            return _cuentaDal.Listar(false, null, null).Where(c => !c.IdGrupoCuenta.HasValue).OrderBy(c => c.NombreComercial).ToList();
        }

        // Contactos distintos entre las cuentas activas del grupo (los grupos tienen pocas cuentas).
        public int ContactosDelGrupo(IEnumerable<CuentaModel> cuentas)
        {
            return cuentas.Where(c => c.Activo).SelectMany(c => _personaDal.PersonasDeCuenta(c.IdCuenta)).Select(p => p.IdPersona).Distinct().Count();
        }
    }
}
