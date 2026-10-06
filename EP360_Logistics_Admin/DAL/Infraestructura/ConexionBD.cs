using System;
using System.Configuration;
using System.Data.SqlClient;

namespace EP360_Logistics_Admin.DAL.Infraestructura
{
    public static class ConexionBD
    {
        // En produccion la cadena puede venir de la variable de entorno de MAQUINA
        // EP360LOGISTICS_CONNECTION_STRING (asi no queda en texto plano en un archivo). El nombre es PROPIO a proposito: esta app vive en el
        // mismo servidor que el portal EP360, que ya usa DB_CONNECTION_STRING para SU base; compartir el nombre haria que una leyera la de la otra. En desarrollo
        // esa variable no existe y se usa ConnectionStrings.config.
        public static SqlConnection ObtenerConexion()
        {
            string cadenaConexion = Environment.GetEnvironmentVariable("EP360LOGISTICS_CONNECTION_STRING", EnvironmentVariableTarget.Machine);
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                cadenaConexion = ConfigurationManager.ConnectionStrings["CadenaSQL"].ConnectionString;
            }
            return new SqlConnection(cadenaConexion);
        }

        // Copia de la global en el servidor 11 (EP360LOGISTICS_CONNECTION_STRING_REPLICA de maquina, o "CadenaSQLReplica" en
        // ConnectionStrings.config). Si no esta configurada, la app escribe solo en el 60 como antes.
        private static string CadenaReplica()
        {
            string cadena = Environment.GetEnvironmentVariable("EP360LOGISTICS_CONNECTION_STRING_REPLICA", EnvironmentVariableTarget.Machine);
            if (string.IsNullOrWhiteSpace(cadena))
            {
                var configurada = ConfigurationManager.ConnectionStrings["CadenaSQLReplica"];
                cadena = configurada == null ? null : configurada.ConnectionString;
            }
            return cadena;
        }

        public static bool ReplicaConfigurada
        {
            get { return !string.IsNullOrWhiteSpace(CadenaReplica()); }
        }

        // Timeout de conexion corto: si el 11 esta caido no se debe congelar cada escritura esperando 15 segundos.
        public static SqlConnection ObtenerConexionReplica()
        {
            string cadena = CadenaReplica();
            if (string.IsNullOrWhiteSpace(cadena))
            {
                throw new InvalidOperationException("La copia en el servidor 11 no esta configurada.");
            }
            var constructor = new SqlConnectionStringBuilder(cadena);
            if (constructor.ConnectTimeout > 5) constructor.ConnectTimeout = 5;
            return new SqlConnection(constructor.ConnectionString);
        }
    }
}
