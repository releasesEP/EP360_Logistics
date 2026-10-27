using System.Collections.Generic;
using System.Data;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;
using static EP360_Logistics_Admin.DAL.Infraestructura.AccesoSP;

namespace EP360_Logistics_Admin.DAL
{
    public class TransportistaDAL
    {
        private static TransportistaModel Mapa(IDataRecord r)
        {
            return new TransportistaModel
            {
                IdTransportista = r.Entero("idTransportista"),
                Folio = r.Texto("folio"),
                Nombre = r.Texto("nombre"),
                RazonSocial = r.Texto("razonSocial"),
                Rfc = r.Texto("rfc"),
                IdCuenta = r.EnteroNulo("idCuenta"),
                Cuenta = r.Texto("cuenta"),
                Activo = r.Booleano("activo")
            };
        }

        public List<TransportistaModel> Listar(bool incluirInactivos, string buscar)
        {
            return Lista("flota.sp_ObtenerTransportistas", r =>
            {
                var t = Mapa(r);
                t.TotalChoferes = r.Entero("choferes");
                t.TotalTractores = r.Entero("tractores");
                t.TotalCajas = r.Entero("cajas");
                return t;
            },
            P("@incluirInactivos", incluirInactivos),
            P("@buscar", string.IsNullOrWhiteSpace(buscar) ? null : buscar.Trim()));
        }

        public TransportistaModel ObtenerPorId(int id)
        {
            return Uno("flota.sp_ObtenerTransportistaPorId", Mapa, P("@idTransportista", id));
        }

        public int Insertar(TransportistaModel m)
        {
            return Escalar("flota.sp_InsertarTransportista",
                P("@nombre", m.Nombre), P("@razonSocial", m.RazonSocial), P("@rfc", m.Rfc), P("@idCuenta", m.IdCuenta));
        }

        public void Actualizar(TransportistaModel m)
        {
            Ejecutar("flota.sp_ActualizarTransportista",
                P("@idTransportista", m.IdTransportista), P("@nombre", m.Nombre),
                P("@razonSocial", m.RazonSocial), P("@rfc", m.Rfc), P("@idCuenta", m.IdCuenta));
        }

        public void Desactivar(int id) { Ejecutar("flota.sp_DesactivarTransportista", P("@idTransportista", id)); }
        public void Reactivar(int id) { Ejecutar("flota.sp_ReactivarTransportista", P("@idTransportista", id)); }
    }
}
