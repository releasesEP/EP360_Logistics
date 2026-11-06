using System;
using System.Collections.Generic;
using System.Linq;
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

        // Excel con el formato de los reportes de Traceability (ver ExcelEP360Helper). Recibe lo mismo que recibia el CSV:
        // nombre de archivo, filas ya filtradas y columnas (titulo, valor). Las fechas y numeros viajan con su tipo real.
        // Si hay una columna "Activo/Activa" (bool) se usa para las tarjetas y se pinta como estado; las columnas de catalogo
        // (Tipo, Departamento, Sucursal, Grupo, Transportista) dan las tablas de conteo del resumen.
        public static FileContentResult Exportar<T>(string nombreArchivo, IEnumerable<T> filas, params (string Titulo, Func<T, object> Valor)[] columnas)
        {
            var lista = filas.ToList();
            var encabezados = columnas.Select(c => c.Titulo).ToList();
            var datos = lista.Select(f => columnas.Select(c => c.Valor(f)).ToArray()).ToList();
            string titulo = char.ToUpper(nombreArchivo[0]) + nombreArchivo.Substring(1);

            int iActivo = encabezados.FindIndex(t => t.StartsWith("Activ", StringComparison.OrdinalIgnoreCase));
            var tarjetas = new List<ExcelEP360Helper.Tarjeta> { new ExcelEP360Helper.Tarjeta { Etiqueta = "Registros", Valor = datos.Count, Nota = "con los filtros de la pantalla" } };
            if (iActivo >= 0)
            {
                int activos = datos.Count(d => d[iActivo] is bool && (bool)d[iActivo]);
                tarjetas.Add(new ExcelEP360Helper.Tarjeta { Etiqueta = "Activos", Valor = activos, Color = "azul" });
                tarjetas.Add(new ExcelEP360Helper.Tarjeta { Etiqueta = "Inactivos", Valor = datos.Count - activos, Color = "gris" });
            }

            var tablas = new List<ExcelEP360Helper.TablaConteo>();
            foreach (string catalogo in new[] { "Tipo", "Departamento", "Sucursal", "Grupo", "Transportista" })
            {
                int i = encabezados.FindIndex(t => t.Equals(catalogo, StringComparison.OrdinalIgnoreCase));
                if (i < 0 || tablas.Count == 3) continue;
                tablas.Add(new ExcelEP360Helper.TablaConteo
                {
                    Titulo = "Por " + catalogo.ToLower(),
                    Renglones = datos.GroupBy(d => Convert.ToString(d[i]) is string v && v != "" ? v : "(sin dato)")
                                     .Select(g => new KeyValuePair<string, int>(g.Key, g.Count()))
                                     .OrderByDescending(k => k.Value).Take(15).ToList()
                });
            }

            byte[] bytes = ExcelEP360Helper.Generar(titulo, "Generado el " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"), tarjetas, tablas,
                encabezados, datos, iActivo, d => d[iActivo] is bool ? ((bool)d[iActivo] ? "azul" : "gris") : null);
            return new FileContentResult(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            {
                FileDownloadName = nombreArchivo + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx"
            };
        }
    }
}
