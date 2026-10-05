using System.Collections.Generic;
using System.Data;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;
using static EP360_Logistics_Admin.DAL.Infraestructura.AccesoSP;

namespace EP360_Logistics_Admin.DAL
{
    public class GrupoCuentaDAL
    {
        private static GrupoCuentaModel Mapa(IDataRecord r)
        {
            return new GrupoCuentaModel
            {
                IdGrupoCuenta = r.Entero("idGrupoCuenta"),
                Folio = r.Texto("folio"),
                Nombre = r.Texto("nombre"),
                Activo = r.Booleano("activo"),
                TotalCuentas = r.Entero("totalCuentas")
            };
        }

        public List<GrupoCuentaModel> Listar(bool incluirInactivos)
        {
            return Lista("dir.sp_ObtenerGruposCuenta", Mapa, P("@incluirInactivos", incluirInactivos));
        }

        public GrupoCuentaModel ObtenerPorId(int id)
        {
            return Uno("dir.sp_ObtenerGrupoCuentaPorId", Mapa, P("@idGrupoCuenta", id));
        }

        public int Insertar(string nombre)
        {
            return Escalar("dir.sp_InsertarGrupoCuenta", P("@nombre", nombre));
        }

        public void Actualizar(int id, string nombre)
        {
            Ejecutar("dir.sp_ActualizarGrupoCuenta", P("@idGrupoCuenta", id), P("@nombre", nombre));
        }

        public void Desactivar(int id) { Ejecutar("dir.sp_DesactivarGrupoCuenta", P("@idGrupoCuenta", id)); }
        public void Reactivar(int id) { Ejecutar("dir.sp_ReactivarGrupoCuenta", P("@idGrupoCuenta", id)); }
    }
}
