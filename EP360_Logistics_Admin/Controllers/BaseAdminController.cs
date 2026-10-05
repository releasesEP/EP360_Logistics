using System;
using System.Data.SqlClient;
using System.Web.Mvc;

namespace EP360_Logistics_Admin.Controllers
{
    public abstract class BaseAdminController : Controller
    {
        // Ejecuta una accion contra la BD. Los errores de negocio de los SPs (THROW 50001..50099) se
        // muestran tal cual al administrador; cualquier otro error de BD se resume sin detalles internos.
        protected bool Ejecutar(Action accion, string mensajeExito)
        {
            try
            {
                accion();
                if (!string.IsNullOrEmpty(mensajeExito)) TempData["Exito"] = mensajeExito;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                // Validaciones de la capa de servicio (ej. contrasena muy corta): el mensaje ya es para el usuario.
                TempData["Error"] = ex.Message;
                return false;
            }
            catch (SqlException ex)
            {
                TempData["Error"] = ex.Number >= 50000 && ex.Number < 51000
                    ? ex.Message
                    : "No se pudo completar la operación en la base de datos (error " + ex.Number + ").";
                return false;
            }
        }

        protected bool EjecutarConId(Func<int> accion, string mensajeExito, out int id)
        {
            int resultado = 0;
            bool ok = Ejecutar(() => { resultado = accion(); }, mensajeExito);
            id = resultado;
            return ok;
        }
    }
}
