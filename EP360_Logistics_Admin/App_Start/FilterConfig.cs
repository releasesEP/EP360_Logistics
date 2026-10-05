using System.Web.Mvc;
using EP360_Logistics_Admin.Filters;

namespace EP360_Logistics_Admin
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new AutorizarAdministradorADAttribute());
        }
    }
}
