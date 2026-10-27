using System.Collections.Generic;
using System.Data;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;
using static EP360_Logistics_Admin.DAL.Infraestructura.AccesoSP;

namespace EP360_Logistics_Admin.DAL
{
    public class TractorDAL
    {
        private static TractorModel Mapa(IDataRecord r)
        {
            return new TractorModel
            {
                IdTractor = r.Entero("idTractor"),
                Economico = r.Texto("economico"),
                IdTransportista = r.Entero("idTransportista"),
                Transportista = r.Texto("transportista"),
                Placa = r.Texto("placa"),
                Vin = r.Texto("vin"),
                Anio = r.EnteroNulo("anio"),
                Marca = r.Texto("marca"),
                Activo = r.Booleano("activo")
            };
        }

        public List<TractorModel> Listar(bool incluirInactivos, int? idTransportista, string buscar)
        {
            return Lista("flota.sp_ObtenerTractores", Mapa,
                P("@incluirInactivos", incluirInactivos),
                P("@idTransportista", idTransportista),
                P("@buscar", string.IsNullOrWhiteSpace(buscar) ? null : buscar.Trim()));
        }

        public TractorModel ObtenerPorId(int id) { return Uno("flota.sp_ObtenerTractorPorId", Mapa, P("@idTractor", id)); }

        public int Insertar(TractorModel m)
        {
            return Escalar("flota.sp_InsertarTractor",
                P("@idTransportista", m.IdTransportista), P("@economico", m.Economico), P("@placa", m.Placa),
                P("@vin", m.Vin), P("@anio", m.Anio), P("@marca", m.Marca));
        }

        public void Actualizar(TractorModel m)
        {
            Ejecutar("flota.sp_ActualizarTractor",
                P("@idTractor", m.IdTractor), P("@idTransportista", m.IdTransportista), P("@economico", m.Economico),
                P("@placa", m.Placa), P("@vin", m.Vin), P("@anio", m.Anio), P("@marca", m.Marca));
        }

        public void Desactivar(int id) { Ejecutar("flota.sp_DesactivarTractor", P("@idTractor", id)); }
        public void Reactivar(int id) { Ejecutar("flota.sp_ReactivarTractor", P("@idTractor", id)); }
    }
}
