using System.Web.Mvc;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    public class HomeController : Controller
    {
        private readonly InicioService _inicioService = new InicioService();

        public ActionResult Index()
        {
            return View(_inicioService.Obtener());
        }
    }
}
