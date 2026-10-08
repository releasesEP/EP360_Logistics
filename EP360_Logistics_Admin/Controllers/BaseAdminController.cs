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

        // Formularios cortos: si la peticion viene del modal (fetch con X-Requested-With) se devuelve solo el
        // contenido del modal; si alguien abre la URL directo, la pagina completa de siempre.
        protected ActionResult VistaFormulario(string vista, string vistaModal, object modelo)
        {
            if (!Request.IsAjaxRequest()) return View(vista, modelo);
            // El error de la BD se muestra dentro del modal, no en el aviso de la siguiente pagina.
            if (TempData["Error"] != null) { ViewBag.ErrorModal = TempData["Error"]; TempData.Remove("Error"); }
            return PartialView(vistaModal, modelo);
        }

        // Guardado correcto: el modal recibe a donde ir (el aviso de exito queda en TempData para esa pagina).
        protected ActionResult ExitoFormulario(string url)
        {
            if (Request.IsAjaxRequest()) return Json(new { ok = true, url });
            return Redirect(url);
        }

        // Despues de una accion desde un listado, regresa a la misma pagina con los mismos filtros.
        // Solo se aceptan URLs locales (evita redirecciones abiertas).
        protected ActionResult Volver(string returnUrl, ActionResult porOmision)
        {
            return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : porOmision;
        }
    }
}
