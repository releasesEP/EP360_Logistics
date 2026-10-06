using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace EP360_Logistics_Admin.Helpers
{
    public static class ListadoHelpers
    {
        // URL de la pagina actual con los mismos filtros de la query string y los cambios indicados.
        // Un valor null o "" quita el parametro. Cualquier cambio que no sea la pagina regresa a la 1,
        // asi que cambiar un filtro nunca deja al usuario en una pagina que ya no existe.
        public static string ConFiltros(this UrlHelper url, object cambios)
        {
            var request = url.RequestContext.HttpContext.Request;
            var ruta = url.RequestContext.RouteData.Values;
            var valores = new RouteValueDictionary();
            foreach (string clave in request.QueryString.AllKeys.Where(k => k != null))
                valores[clave] = request.QueryString[clave];

            // El constructor que recibe object lee propiedades (objeto anonimo), no las entradas de un diccionario.
            var diccionario = cambios as IDictionary<string, object>;
            var aplicar = diccionario != null ? new RouteValueDictionary(diccionario) : new RouteValueDictionary(cambios);
            if (!aplicar.ContainsKey("pagina")) valores.Remove("pagina");
            valores.Remove("formato");
            foreach (var c in aplicar) valores[c.Key] = c.Value;
            foreach (var vacia in valores.Where(v => v.Value == null || v.Value as string == "").Select(v => v.Key).ToList())
                valores.Remove(vacia);
            if (ruta.ContainsKey("id")) valores["id"] = ruta["id"];

            return url.Action((string)ruta["action"], (string)ruta["controller"], valores);
        }

        // Encabezado de columna que ordena al darle clic (asc -> desc -> asc).
        public static MvcHtmlString ColumnaOrden(this HtmlHelper html, string texto, string campo, string clase = null)
        {
            var url = new UrlHelper(html.ViewContext.RequestContext);
            string actual = html.ViewContext.HttpContext.Request.QueryString["orden"] ?? "";
            bool asc = actual == campo, desc = actual == campo + "_desc";
            string siguiente = asc ? campo + "_desc" : campo;
            string icono = asc ? "fa-sort-up" : desc ? "fa-sort-down" : "fa-sort";
            return MvcHtmlString.Create(string.Format(
                "<th class=\"col-orden{0}{1}\"><a href=\"{2}\">{3}<i class=\"fas {4}\"></i></a></th>",
                asc || desc ? " activa" : "", clase == null ? "" : " " + clase,
                HttpUtility.HtmlAttributeEncode(url.ConFiltros(new { orden = siguiente })),
                HttpUtility.HtmlEncode(texto), icono));
        }

        // Ordena una lista segun ?orden=campo o campo_desc. Las claves desconocidas usan el orden por omision.
        public static IEnumerable<T> Ordenar<T>(IEnumerable<T> lista, string orden, string porOmision, IDictionary<string, Func<T, object>> campos)
        {
            orden = string.IsNullOrEmpty(orden) ? porOmision : orden;
            bool desc = orden.EndsWith("_desc");
            string campo = desc ? orden.Substring(0, orden.Length - 5) : orden;
            Func<T, object> clave;
            if (!campos.TryGetValue(campo, out clave)) clave = campos[porOmision];
            return desc ? lista.OrderByDescending(clave) : lista.OrderBy(clave);
        }

        // CSV en UTF-8 con BOM para que Excel abra bien los acentos; todos los valores van entre comillas.
        public static FileContentResult Csv<T>(string nombreArchivo, IEnumerable<T> filas, params (string Titulo, Func<T, object> Valor)[] columnas)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", columnas.Select(c => Citar(c.Titulo))));
            foreach (var f in filas)
                sb.AppendLine(string.Join(",", columnas.Select(c => Citar(Formato(c.Valor(f))))));
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return new FileContentResult(bytes, "text/csv") { FileDownloadName = nombreArchivo + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv" };
        }

        private static string Formato(object v)
        {
            if (v == null) return "";
            if (v is DateTime) return ((DateTime)v).ToString("yyyy-MM-dd");
            if (v is bool) return (bool)v ? "Sí" : "No";
            return Convert.ToString(v);
        }

        private static string Citar(string s) { return "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; }
    }
}
