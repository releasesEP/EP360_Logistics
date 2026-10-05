using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace EP360_Logistics_Admin.DAL.Infraestructura
{
    // Helper minimo para llamar stored procedures (el proyecto solo usa SPs).
    public static class AccesoSP
    {
        public static SqlParameter P(string nombre, object valor)
        {
            return new SqlParameter(nombre, valor ?? DBNull.Value);
        }

        private static SqlCommand Preparar(SqlConnection conexion, string sp, SqlParameter[] parametros)
        {
            var comando = new SqlCommand(sp, conexion) { CommandType = CommandType.StoredProcedure };
            if (parametros != null) comando.Parameters.AddRange(parametros);
            return comando;
        }

        public static List<T> Lista<T>(string sp, Func<IDataRecord, T> mapa, params SqlParameter[] parametros)
        {
            var resultado = new List<T>();
            using (var conexion = ConexionBD.ObtenerConexion())
            using (var comando = Preparar(conexion, sp, parametros))
            {
                conexion.Open();
                using (var lector = comando.ExecuteReader())
                {
                    while (lector.Read()) resultado.Add(mapa(lector));
                }
            }
            return resultado;
        }

        public static T Uno<T>(string sp, Func<IDataRecord, T> mapa, params SqlParameter[] parametros) where T : class
        {
            return Lista(sp, mapa, parametros).FirstOrDefault();
        }

        // Para SPs que devuelven una fila con el id generado (primera columna).
        public static int Escalar(string sp, params SqlParameter[] parametros)
        {
            using (var conexion = ConexionBD.ObtenerConexion())
            using (var comando = Preparar(conexion, sp, parametros))
            {
                conexion.Open();
                return Convert.ToInt32(comando.ExecuteScalar());
            }
        }

        public static void Ejecutar(string sp, params SqlParameter[] parametros)
        {
            using (var conexion = ConexionBD.ObtenerConexion())
            using (var comando = Preparar(conexion, sp, parametros))
            {
                conexion.Open();
                comando.ExecuteNonQuery();
            }
        }
    }

    public static class LectorExtensiones
    {
        public static string Texto(this IDataRecord r, string columna)
        {
            int i = r.GetOrdinal(columna);
            return r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));
        }
        public static int Entero(this IDataRecord r, string columna)
        {
            return Convert.ToInt32(r.GetValue(r.GetOrdinal(columna)));
        }
        public static int? EnteroNulo(this IDataRecord r, string columna)
        {
            int i = r.GetOrdinal(columna);
            return r.IsDBNull(i) ? (int?)null : Convert.ToInt32(r.GetValue(i));
        }
        public static bool Booleano(this IDataRecord r, string columna)
        {
            return Convert.ToBoolean(r.GetValue(r.GetOrdinal(columna)));
        }
        public static bool? BooleanoNulo(this IDataRecord r, string columna)
        {
            int i = r.GetOrdinal(columna);
            return r.IsDBNull(i) ? (bool?)null : Convert.ToBoolean(r.GetValue(i));
        }
        public static DateTime? Fecha(this IDataRecord r, string columna)
        {
            int i = r.GetOrdinal(columna);
            return r.IsDBNull(i) ? (DateTime?)null : Convert.ToDateTime(r.GetValue(i));
        }
    }
}
