using System.Collections.Generic;
using System.Data;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;
using static EP360_Logistics_Admin.DAL.Infraestructura.AccesoSP;

namespace EP360_Logistics_Admin.DAL
{
    public class ChoferDAL
    {
        private static ChoferModel Mapa(IDataRecord r)
        {
            return new ChoferModel
            {
                IdChofer = r.Entero("idChofer"),
                NombreCompleto = r.Texto("nombreCompleto"),
                Licencia = r.Texto("licencia"),
                Telefono = r.Texto("telefono"),
                Activo = r.Booleano("activo")
            };
        }

        public List<ChoferModel> Listar(bool incluirInactivos, int? idTransportista, string buscar)
        {
            return Lista("flota.sp_ObtenerChoferes", r =>
            {
                var c = Mapa(r);
                c.Transportistas = r.Texto("transportistas");
                return c;
            },
            P("@incluirInactivos", incluirInactivos),
            P("@idTransportista", idTransportista),
            P("@buscar", string.IsNullOrWhiteSpace(buscar) ? null : buscar.Trim()));
        }

        public ChoferModel ObtenerPorId(int id)
        {
            return Uno("flota.sp_ObtenerChoferPorId", r =>
            {
                var c = Mapa(r);
                c.FechaCreacion = r.Fecha("fechaCreacion");
                c.FechaModificacion = r.Fecha("fechaModificacion");
                return c;
            }, P("@idChofer", id));
        }

        public int Insertar(ChoferModel m)
        {
            return Escalar("flota.sp_InsertarChofer",
                P("@nombreCompleto", m.NombreCompleto), P("@idTransportista", m.IdTransportista),
                P("@licencia", m.Licencia), P("@telefono", m.Telefono));
        }

        public void Actualizar(ChoferModel m)
        {
            Ejecutar("flota.sp_ActualizarChofer",
                P("@idChofer", m.IdChofer), P("@nombreCompleto", m.NombreCompleto),
                P("@licencia", m.Licencia), P("@telefono", m.Telefono));
        }

        public void Desactivar(int id) { Ejecutar("flota.sp_DesactivarChofer", P("@idChofer", id)); }
        public void Reactivar(int id) { Ejecutar("flota.sp_ReactivarChofer", P("@idChofer", id)); }

        public List<ChoferTransportistaModel> TransportistasDeChofer(int idChofer)
        {
            return Lista("flota.sp_ObtenerTransportistasDeChofer", r => new ChoferTransportistaModel
            {
                IdChoferTransportista = r.Entero("idChoferTransportista"),
                IdTransportista = r.Entero("idTransportista"),
                Transportista = r.Texto("transportista"),
                FechaInicio = r.Fecha("fechaInicio"),
                FechaFin = r.Fecha("fechaFin"),
                Activo = r.Booleano("activo")
            }, P("@idChofer", idChofer), P("@incluirHistoria", true));
        }

        public void Vincular(int idChofer, int idTransportista)
        {
            Ejecutar("flota.sp_VincularChoferTransportista", P("@idChofer", idChofer), P("@idTransportista", idTransportista));
        }

        public void Desvincular(int idChofer, int idTransportista)
        {
            Ejecutar("flota.sp_DesvincularChoferTransportista", P("@idChofer", idChofer), P("@idTransportista", idTransportista));
        }
    }
}
