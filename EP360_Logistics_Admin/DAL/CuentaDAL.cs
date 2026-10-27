using System.Collections.Generic;
using System.Data;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;
using static EP360_Logistics_Admin.DAL.Infraestructura.AccesoSP;

namespace EP360_Logistics_Admin.DAL
{
    public class CuentaDAL
    {
        private static CuentaModel Mapa(IDataRecord r)
        {
            return new CuentaModel
            {
                IdCuenta = r.Entero("idCuenta"),
                Folio = r.Texto("folio"),
                CodigoCuenta = r.Texto("codigoCuenta"),
                NombreComercial = r.Texto("nombreComercial"),
                RazonSocial = r.Texto("razonSocial"),
                Rfc = r.Texto("rfc"),
                IdGrupoCuenta = r.EnteroNulo("idGrupoCuenta"),
                GrupoCuenta = r.Texto("grupoCuenta"),
                IdCiudad = r.EnteroNulo("idCiudad"),
                Ciudad = r.Texto("ciudad"),
                Pais = r.Texto("pais"),
                IdSucursal = r.EnteroNulo("idSucursal"),
                Sucursal = r.Texto("sucursal"),
                Activo = r.Booleano("activo")
            };
        }

        public List<CuentaModel> Listar(bool incluirInactivos, int? idGrupoCuenta, string buscar)
        {
            return Lista("dir.sp_ObtenerCuentas", Mapa,
                P("@incluirInactivos", incluirInactivos),
                P("@idGrupoCuenta", idGrupoCuenta),
                P("@buscar", string.IsNullOrWhiteSpace(buscar) ? null : buscar.Trim()));
        }

        public CuentaModel ObtenerPorId(int id)
        {
            return Uno("dir.sp_ObtenerCuentaPorId", Mapa, P("@idCuenta", id));
        }

        public int Insertar(CuentaModel m)
        {
            return Escalar("dir.sp_InsertarCuenta",
                P("@nombreComercial", m.NombreComercial), P("@codigoCuenta", m.CodigoCuenta),
                P("@razonSocial", m.RazonSocial), P("@rfc", m.Rfc),
                P("@idGrupoCuenta", m.IdGrupoCuenta), P("@idCiudad", m.IdCiudad), P("@idSucursal", m.IdSucursal));
        }

        public void Actualizar(CuentaModel m)
        {
            Ejecutar("dir.sp_ActualizarCuenta",
                P("@idCuenta", m.IdCuenta), P("@nombreComercial", m.NombreComercial), P("@codigoCuenta", m.CodigoCuenta),
                P("@razonSocial", m.RazonSocial), P("@rfc", m.Rfc),
                P("@idGrupoCuenta", m.IdGrupoCuenta), P("@idCiudad", m.IdCiudad), P("@idSucursal", m.IdSucursal));
        }

        // idGrupoCuenta NULL deja la cuenta sin grupo.
        public void AsignarGrupo(int idCuenta, int? idGrupoCuenta)
        {
            Ejecutar("dir.sp_AsignarGrupoCuenta", P("@idCuenta", idCuenta), P("@idGrupoCuenta", idGrupoCuenta));
        }

        public void Desactivar(int id) { Ejecutar("dir.sp_DesactivarCuenta", P("@idCuenta", id)); }
        public void Reactivar(int id) { Ejecutar("dir.sp_ReactivarCuenta", P("@idCuenta", id)); }
    }
}
