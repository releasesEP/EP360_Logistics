using System.Web.Mvc;
using System.Web.Routing;

namespace EP360_Logistics_Admin
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);

            // Copia en el servidor 11: reintenta cada minuto lo que no se pudo repetir alla (solo si esta configurada).
            DAL.Infraestructura.Replicador.IniciarReintentos();

            // Sincronizacion con AD todos los dias a las 03:00 (ver Services/SincronizacionProgramada).
            Services.SincronizacionProgramada.Iniciar();
        }
    }
}
