using System;
using System.Linq;
using System.Web.Mvc;
using EP360_Logistics_Admin.Models;
using EP360_Logistics_Admin.Services;

namespace EP360_Logistics_Admin.Controllers
{
    // Accesos a portales: quien entra a EP360, Balance, Help Desk y la Intranet. Cada portal decide despues los permisos.
    public class AccesosController : BaseAdminController
    {
        private readonly AccesoPortalService _servicio = new AccesoPortalService();

        public ActionResult Index(string buscar, string tipo, int? idPortal, string acceso, int pagina = 1, int tam = 25)
        {
            var portales = _servicio.Portales();
            var todas = _servicio.Personas();
            var filtradas = AccesoPortalService.Filtrar(todas, buscar, tipo, idPortal, acceso);

            ViewBag.Portales = portales;
            ViewBag.Buscar = buscar;
            ViewBag.Tipo = tipo;
            ViewBag.IdPortal = idPortal;
            ViewBag.Acceso = acceso;
            ViewBag.TotalPersonas = todas.Count;
            ViewBag.SinAcceso = todas.Count(p => p.Portales.Count == 0);
            ViewBag.ConteoPorPortal = portales.ToDictionary(p => p.IdPortal, p => todas.Count(x => x.Portales.Contains(p.IdPortal)));
            ViewBag.Filtradas = filtradas.Count;

            // Cuantas personas recibiria el acceso masivo (las filtradas que aun no lo tienen y pueden tenerlo).
            var portalMasivo = idPortal.HasValue ? portales.FirstOrDefault(p => p.IdPortal == idPortal.Value) : null;
            ViewBag.PortalMasivo = portalMasivo;
            ViewBag.ElegiblesMasivo = portalMasivo == null ? 0
                : filtradas.Count(p => !p.Portales.Contains(portalMasivo.IdPortal) && AccesoPortalService.PuedeEntrar(p, portalMasivo));

            return View(new Paginado<AccesoPersonaModel>(filtradas, pagina, tam));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Otorgar(int idPersona, int idPortal, string returnUrl)
        {
            string usuario = User.Identity.Name;
            Ejecutar(() => _servicio.Otorgar(idPersona, idPortal, usuario), "Acceso otorgado.");
            return Volver(returnUrl);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Revocar(int idPersona, int idPortal, string returnUrl)
        {
            string usuario = User.Identity.Name;
            Ejecutar(() => _servicio.Revocar(idPersona, idPortal, usuario), "Acceso retirado.");
            return Volver(returnUrl);
        }

        // Da el portal a TODAS las personas que se ven con el filtro actual (por ejemplo: todos los usuarios de AD, Help Desk).
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult OtorgarAFiltrados(int idPortal, string buscar, string tipo, string acceso, string returnUrl)
        {
            string usuario = User.Identity.Name;
            int otorgados = 0;
            Ejecutar(() =>
            {
                var portal = _servicio.Portales().FirstOrDefault(p => p.IdPortal == idPortal);
                if (portal == null) throw new InvalidOperationException("El portal no existe.");
                var filtradas = AccesoPortalService.Filtrar(_servicio.Personas(), buscar, tipo, idPortal, acceso);
                otorgados = _servicio.OtorgarALista(filtradas, portal, usuario);
            }, null);

            TempData["Exito"] = otorgados == 0
                ? "No había personas a las que dar acceso con ese filtro."
                : "Se dio acceso a " + otorgados + (otorgados == 1 ? " persona." : " personas.");
            return Volver(returnUrl);
        }

        private ActionResult Volver(string returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
            return RedirectToAction("Index");
        }
    }
}
