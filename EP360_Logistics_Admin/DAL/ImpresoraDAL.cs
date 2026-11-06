using System.Collections.Generic;
using System.Data;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;
using static EP360_Logistics_Admin.DAL.Infraestructura.AccesoSP;

namespace EP360_Logistics_Admin.DAL
{
    // Catalogo global de impresoras compartidas (Database/17_ImpresorasCompartidas.sql). Las escrituras pasan por AccesoSP: tambien se repiten en el 11.
    public class ImpresoraDAL
    {
        private static ImpresoraAdminModel Mapa(IDataRecord r)
        {
            return new ImpresoraAdminModel
            {
                IdImpresora = r.Entero("idImpresora"),
                Nombre = r.Texto("nombre"),
                RutaCompartida = r.Texto("rutaCompartida"),
                IdSucursal = r.EnteroNulo("idSucursal"),
                Sucursal = r.Texto("sucursal"),
                Activo = r.Booleano("activo"),
                FechaCreacion = r.Fecha("fechaCreacion")
            };
        }

        public List<ImpresoraAdminModel> Listar() { return Lista("dir.sp_ObtenerImpresoras", Mapa, P("@incluirInactivas", true)); }

        public int Guardar(int? id, string nombre, string ruta, int? idSucursal)
        {
            return Escalar("dir.sp_GuardarImpresora", P("@idImpresora", id), P("@nombre", nombre), P("@rutaCompartida", ruta), P("@idSucursal", idSucursal));
        }

        public void Desactivar(int id) { Ejecutar("dir.sp_DesactivarImpresora", P("@idImpresora", id)); }

        public void Reactivar(int id) { Ejecutar("dir.sp_ReactivarImpresora", P("@idImpresora", id)); }
    }
}
