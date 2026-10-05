using System;
using System.Configuration;
using System.Data.SqlClient;

namespace EP360_Logistics_Admin.DAL.Infraestructura
{
    public static class ConexionBD
    {
        // En produccion la cadena puede venir de la variable de entorno de MAQUINA
        // DB_CONNECTION_STRING (asi no queda en texto plano en un archivo). En desarrollo
        // esa variable no existe y se usa ConnectionStrings.config.
        public static SqlConnection ObtenerConexion()
        {
            string cadenaConexion = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING", EnvironmentVariableTarget.Machine);
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                cadenaConexion = ConfigurationManager.ConnectionStrings["CadenaSQL"].ConnectionString;
            }
            return new SqlConnection(cadenaConexion);
        }
    }
}
