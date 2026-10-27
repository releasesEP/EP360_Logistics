using System;
using System.Linq;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    // Indicadores del tablero de inicio. Lee las mismas listas que las pantallas (son de cientos o
    // pocos miles de filas), asi que los numeros siempre cuadran con lo que se ve al dar clic.
    public class InicioService
    {
        private readonly DirectorioService _directorio = new DirectorioService();

        public InicioModel Obtener()
        {
            var m = new InicioModel { Estado = _directorio.ObtenerEstadoBase() };
            if (!m.Estado.Conectado) return m;

            try
            {
                var personas = new PersonaDAL().ListarTodas().Where(p => p.Activo).ToList();
                m.PersonasActivas = personas.Count;
                m.UsuariosAD = personas.Count(p => p.TipoPersona == "AD");
                m.UsuariosExternos = personas.Count(p => p.TipoPersona == "Externo");
                m.Contactos = personas.Count(p => p.TipoPersona == "Contacto");
                m.PersonasPorSucursal = personas.GroupBy(p => p.Sucursal ?? "Sin sucursal")
                    .Select(g => new BarraModel { Etiqueta = g.Key, Valor = g.Count(), Id = g.First().IdSucursal })
                    .OrderByDescending(b => b.Valor).Take(6).ToList();

                var cuentas = new CuentaDAL().Listar(false, null, null);
                m.CuentasActivas = cuentas.Count;
                m.CuentasSinGrupo = cuentas.Count(c => c.IdGrupoCuenta == null);
                m.GruposActivos = new GrupoCuentaDAL().Listar(false).Count;

                var cajas = new CajaDAL().Listar(false, null, null, null, null, null);
                m.CajasActivas = cajas.Count;
                m.CajasCliente = cajas.Count(k => k.TipoPropiedad == CajaModel.Cliente);
                m.CajasTransportista = cajas.Count(k => k.TipoPropiedad == CajaModel.DeTransportista);
                m.CajasEP = cajas.Count(k => k.TipoPropiedad == CajaModel.EP);
                m.CajasPorGrupo = cajas.Where(k => k.IdGrupoCuenta != null || k.IdCuenta != null)
                    .GroupBy(k => k.GrupoCuenta ?? k.Cuenta)
                    .Select(g => new BarraModel { Etiqueta = g.Key, Valor = g.Count(), Id = g.First().IdGrupoCuenta })
                    .OrderByDescending(b => b.Valor).Take(6).ToList();

                var transportistas = new TransportistaDAL().Listar(false, null);
                m.TransportistasActivos = transportistas.Count;
                m.TransportistasPorChoferes = transportistas.OrderByDescending(t => t.TotalChoferes).Take(6)
                    .Select(t => new BarraModel { Etiqueta = t.Nombre, Valor = t.TotalChoferes, Id = t.IdTransportista }).ToList();

                m.TractoresActivos = new TractorDAL().Listar(false, null, null).Count;
                var choferes = new ChoferDAL().Listar(false, null, null);
                m.ChoferesActivos = choferes.Count;
                m.ChoferesVariosTransportistas = choferes.Count(c => c.Transportistas != null && c.Transportistas.Contains(","));

                m.UltimaSincronizacion = new SincronizacionADService().Historial(1).FirstOrDefault();
            }
            catch (Exception ex)
            {
                m.ErrorIndicadores = ex.Message;
            }
            return m;
        }
    }
}
