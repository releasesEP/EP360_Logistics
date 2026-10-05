using System;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    public class DirectorioService
    {
        private readonly EstadoBaseDAL _estadoBaseDAL = new EstadoBaseDAL();

        // Nunca lanza: si la base no esta lista, la pantalla de inicio debe poder mostrar el motivo.
        public EstadoBaseModel ObtenerEstadoBase()
        {
            try
            {
                return _estadoBaseDAL.Obtener();
            }
            catch (Exception ex)
            {
                return new EstadoBaseModel { Error = ex.Message };
            }
        }
    }
}
