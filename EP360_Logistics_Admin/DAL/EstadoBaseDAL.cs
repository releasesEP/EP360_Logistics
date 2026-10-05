using System.Data;
using System.Data.SqlClient;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.DAL
{
    public class EstadoBaseDAL
    {
        // Solo stored procedures: dir.sp_ObtenerEstadoBase (script Database/01_CrearBaseYEsquemas.sql).
        public EstadoBaseModel Obtener()
        {
            var estado = new EstadoBaseModel();

            using (var conexion = ConexionBD.ObtenerConexion())
            using (var comando = new SqlCommand("dir.sp_ObtenerEstadoBase", conexion))
            {
                comando.CommandType = CommandType.StoredProcedure;
                conexion.Open();

                using (var lector = comando.ExecuteReader())
                {
                    if (lector.Read())
                    {
                        estado.BaseDatos = lector.GetString(lector.GetOrdinal("baseDatos"));
                        estado.Servidor = lector.GetString(lector.GetOrdinal("servidor"));
                        estado.Edicion = lector.GetString(lector.GetOrdinal("edicion"));
                        estado.FechaServidor = lector.GetDateTime(lector.GetOrdinal("fechaServidor"));
                        estado.TablasDir = lector.GetInt32(lector.GetOrdinal("tablasDir"));
                        estado.TablasSeg = lector.GetInt32(lector.GetOrdinal("tablasSeg"));
                        estado.TablasSync = lector.GetInt32(lector.GetOrdinal("tablasSync"));
                        estado.PortalesActivos = lector.GetInt32(lector.GetOrdinal("portalesActivos"));
                    }

                    if (lector.NextResult())
                    {
                        while (lector.Read())
                        {
                            estado.Portales.Add(new PortalModel
                            {
                                IdPortal = lector.GetInt32(lector.GetOrdinal("idPortal")),
                                Clave = lector.GetString(lector.GetOrdinal("clave")),
                                Nombre = lector.GetString(lector.GetOrdinal("nombre"))
                            });
                        }
                    }
                }
            }

            return estado;
        }
    }
}
