using System.Collections.Generic;
using System.Data;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;
using static EP360_Logistics_Admin.DAL.Infraestructura.AccesoSP;

namespace EP360_Logistics_Admin.DAL
{
    // Catalogo de sucursales de la global (Database/13_Sucursales.sql). Las escrituras pasan por AccesoSP, asi tambien se repiten en el 11.
    public class SucursalDAL
    {
        private static SucursalAdminModel Mapa(IDataRecord r)
        {
            return new SucursalAdminModel
            {
                IdSucursal = r.Entero("idSucursal"),
                Nombre = r.Texto("nombre"),
                Activo = r.Booleano("activo"),
                FechaCreacion = r.Fecha("fechaCreacion"),
                Personas = r.Entero("personas"),
                Cuentas = r.Entero("cuentas")
            };
        }

        public List<SucursalAdminModel> Listar() { return Lista("dir.sp_ObtenerSucursalesConUso", Mapa, P("@incluirInactivos", true)); }

        public int Insertar(string nombre) { return Escalar("dir.sp_InsertarSucursal", P("@nombre", nombre)); }

        public void Actualizar(int id, string nombre) { Ejecutar("dir.sp_ActualizarSucursal", P("@idSucursal", id), P("@nombre", nombre)); }

        public void Desactivar(int id) { Ejecutar("dir.sp_DesactivarSucursal", P("@idSucursal", id)); }

        public void Reactivar(int id) { Ejecutar("dir.sp_ReactivarSucursal", P("@idSucursal", id)); }

        // Final de cada sincronizacion con AD (Database/14_SucursalesSinUso.sql): desactiva las que ya no tienen personas ni cuentas
        // y reactiva las que ella misma desactivo si volvieron a tener personas. Sin resultado: el Replicador compara resultados 60 vs 11.
        public void Reconciliar() { Ejecutar("dir.sp_ReconciliarSucursales"); }
    }
}
