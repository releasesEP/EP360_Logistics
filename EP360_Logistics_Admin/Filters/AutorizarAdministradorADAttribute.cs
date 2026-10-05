using System.Web.Mvc;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Filters
{
    // Solo entra quien esta autenticado con Windows Y pertenece al grupo de AD de administradores
    // (AD_GrupoAdmins, por defecto ep360admins). Se registra como filtro global (FilterConfig).
    public class AutorizarAdministradorADAttribute : AuthorizeAttribute
    {
        private readonly ActiveDirectoryService _servicioAD = new ActiveDirectoryService();

        protected override bool AuthorizeCore(System.Web.HttpContextBase httpContext)
        {
            if (httpContext?.User?.Identity == null || !httpContext.User.Identity.IsAuthenticated) return false;
            return _servicioAD.PerteneceAlGrupoAdministradores(httpContext.User.Identity.Name);
        }

        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            // Sin sesion de Windows: 401 para que IIS negocie. Con sesion pero fuera del grupo: 403 con pantalla propia.
            if (!filterContext.HttpContext.User.Identity.IsAuthenticated)
            {
                base.HandleUnauthorizedRequest(filterContext);
                return;
            }

            filterContext.Result = new ViewResult { ViewName = "~/Views/Shared/SinAcceso.cshtml" };
            filterContext.HttpContext.Response.StatusCode = 403;
            filterContext.HttpContext.Response.TrySkipIisCustomErrors = true;
        }
    }
}
