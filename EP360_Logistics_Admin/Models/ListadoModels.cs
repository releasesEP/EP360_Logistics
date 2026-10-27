using System;
using System.Collections.Generic;
using System.Linq;

namespace EP360_Logistics_Admin.Models
{
    // Datos de paginacion que necesita la vista _Paginacion, sin importar el tipo de fila.
    public interface IPaginado
    {
        int Pagina { get; }
        int TamPagina { get; }
        int Total { get; }
        int TotalPaginas { get; }
        int Desde { get; }
        int Hasta { get; }
    }

    // Los SPs devuelven la lista completa ya filtrada por estado/busqueda; la pagina se corta aqui.
    // Los catalogos de esta app son de cientos o pocos miles de filas, asi que no vale la pena
    // paginar en SQL.
    public class Paginado<T> : IPaginado
    {
        public static readonly int[] Tamanos = { 10, 25, 50, 100 };

        public List<T> Items { get; private set; }
        public int Pagina { get; private set; }
        public int TamPagina { get; private set; }
        public int Total { get; private set; }
        public int TotalPaginas { get { return Math.Max(1, (int)Math.Ceiling(Total / (double)TamPagina)); } }
        public int Desde { get { return Total == 0 ? 0 : (Pagina - 1) * TamPagina + 1; } }
        public int Hasta { get { return Math.Min(Pagina * TamPagina, Total); } }

        public Paginado(IEnumerable<T> todos, int pagina, int tamPagina)
        {
            var lista = todos as IList<T> ?? todos.ToList();
            TamPagina = Tamanos.Contains(tamPagina) ? tamPagina : 25;
            Total = lista.Count;
            Pagina = Math.Min(Math.Max(1, pagina), TotalPaginas);
            Items = lista.Skip((Pagina - 1) * TamPagina).Take(TamPagina).ToList();
        }
    }

    // Una pestana de estado del encabezado de filtros (Activos / Inactivos / Todos ...).
    public class PestanaFiltro
    {
        public string Clave { get; set; }
        public string Texto { get; set; }
        public string Icono { get; set; }
        public int Conteo { get; set; }
        public string Color { get; set; }
    }

    // Un filtro aplicado, con su "x" para quitarlo.
    public class ChipFiltro
    {
        public string Parametro { get; set; }
        public string Texto { get; set; }
        public string Icono { get; set; }
    }

    // Encabezado completo de un listado: pestanas, chips y total. Lo pinta _FiltrosListado.
    public class EncabezadoListado
    {
        public string ParametroPestana { get; set; } = "estado";
        public string PestanaActiva { get; set; }
        public List<PestanaFiltro> Pestanas { get; set; } = new List<PestanaFiltro>();
        public List<ChipFiltro> Chips { get; set; } = new List<ChipFiltro>();
        public int Total { get; set; }
        public string Sustantivo { get; set; }
    }

    public static class Estados
    {
        public const string Activos = "activos", Inactivos = "inactivos", Todos = "todos";

        public static string Normalizar(string estado)
        {
            return estado == Inactivos || estado == Todos ? estado : Activos;
        }

        public static IEnumerable<T> Filtrar<T>(IEnumerable<T> lista, string estado, Func<T, bool> activo)
        {
            if (estado == Inactivos) return lista.Where(x => !activo(x));
            if (estado == Todos) return lista;
            return lista.Where(activo);
        }

        public static List<PestanaFiltro> Pestanas<T>(IList<T> lista, Func<T, bool> activo, string femenino = "os")
        {
            return new List<PestanaFiltro>
            {
                new PestanaFiltro { Clave = Activos, Texto = "Activ" + femenino, Icono = "fa-circle-check", Conteo = lista.Count(activo), Color = "verde" },
                new PestanaFiltro { Clave = Inactivos, Texto = "Inactiv" + femenino, Icono = "fa-circle-pause", Conteo = lista.Count(x => !activo(x)), Color = "gris" },
                new PestanaFiltro { Clave = Todos, Texto = "Tod" + femenino, Icono = "fa-list", Conteo = lista.Count }
            };
        }
    }

    // Busqueda sin acentos ni mayusculas (la BD es CI_AS: aqui se imita para el filtro en memoria).
    public static class TextoUtil
    {
        public static bool Contiene(string buscar, params string[] campos)
        {
            if (string.IsNullOrWhiteSpace(buscar)) return true;
            var b = Plano(buscar);
            return campos.Any(c => c != null && Plano(c).Contains(b));
        }

        public static string Plano(string s)
        {
            var d = s.Trim().ToUpperInvariant().Normalize(System.Text.NormalizationForm.FormD);
            return new string(d.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        }

        public static string Iniciales(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre)) return "?";
            return string.Concat(nombre.Split(new[] { ' ', '.', '-' }, StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
        }
    }
}
