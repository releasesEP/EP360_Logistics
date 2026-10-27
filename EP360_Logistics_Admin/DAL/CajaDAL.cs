using System.Collections.Generic;
using System.Data;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;
using static EP360_Logistics_Admin.DAL.Infraestructura.AccesoSP;

namespace EP360_Logistics_Admin.DAL
{
    public class CajaDAL
    {
        private static CajaModel Mapa(IDataRecord r)
        {
            return new CajaModel
            {
                IdCaja = r.Entero("idCaja"),
                NumeroCaja = r.Texto("numeroCaja"),
                TipoPropiedad = r.Texto("tipoPropiedad"),
                IdCuenta = r.EnteroNulo("idCuenta"),
                Cuenta = r.Texto("cuenta"),
                IdGrupoCuenta = r.EnteroNulo("idGrupoCuenta"),
                GrupoCuenta = r.Texto("grupoCuenta"),
                IdTransportista = r.EnteroNulo("idTransportista"),
                Transportista = r.Texto("transportista"),
                Placa = r.Texto("placa"),
                Vin = r.Texto("vin"),
                Anio = r.EnteroNulo("anio"),
                Marca = r.Texto("marca"),
                Pies = r.EnteroNulo("pies"),
                Activo = r.Booleano("activo")
            };
        }

        public List<CajaModel> Listar(bool incluirInactivos, string tipoPropiedad, int? idCuenta, int? idGrupoCuenta, int? idTransportista, string buscar)
        {
            return Lista("flota.sp_ObtenerCajas", Mapa,
                P("@incluirInactivos", incluirInactivos),
                P("@tipoPropiedad", string.IsNullOrWhiteSpace(tipoPropiedad) ? null : tipoPropiedad),
                P("@idCuenta", idCuenta),
                P("@idGrupoCuenta", idGrupoCuenta),
                P("@idTransportista", idTransportista),
                P("@buscar", string.IsNullOrWhiteSpace(buscar) ? null : buscar.Trim()));
        }

        public CajaModel ObtenerPorId(int id)
        {
            return Uno("flota.sp_ObtenerCajaPorId", r =>
            {
                var k = Mapa(r);
                k.FechaCreacion = r.Fecha("fechaCreacion");
                k.FechaModificacion = r.Fecha("fechaModificacion");
                return k;
            }, P("@idCaja", id));
        }

        public int Insertar(CajaModel m)
        {
            return Escalar("flota.sp_InsertarCaja",
                P("@numeroCaja", m.NumeroCaja), P("@tipoPropiedad", m.TipoPropiedad),
                P("@idCuenta", m.IdCuenta), P("@idGrupoCuenta", m.IdGrupoCuenta), P("@idTransportista", m.IdTransportista),
                P("@placa", m.Placa), P("@vin", m.Vin), P("@anio", m.Anio), P("@marca", m.Marca), P("@pies", m.Pies));
        }

        public void Actualizar(CajaModel m)
        {
            Ejecutar("flota.sp_ActualizarCaja",
                P("@idCaja", m.IdCaja), P("@numeroCaja", m.NumeroCaja), P("@tipoPropiedad", m.TipoPropiedad),
                P("@idCuenta", m.IdCuenta), P("@idGrupoCuenta", m.IdGrupoCuenta), P("@idTransportista", m.IdTransportista),
                P("@placa", m.Placa), P("@vin", m.Vin), P("@anio", m.Anio), P("@marca", m.Marca), P("@pies", m.Pies));
        }

        public void Desactivar(int id) { Ejecutar("flota.sp_DesactivarCaja", P("@idCaja", id)); }
        public void Reactivar(int id) { Ejecutar("flota.sp_ReactivarCaja", P("@idCaja", id)); }
    }
}
