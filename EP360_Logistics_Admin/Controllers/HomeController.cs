using System.Web.Mvc;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    public class HomeController : Controller
    {
        private readonly DirectorioService _directorioService = new DirectorioService();

        public ActionResult Index()
        {
            return View(_directorioService.ObtenerEstadoBase());
        }
    }
}
