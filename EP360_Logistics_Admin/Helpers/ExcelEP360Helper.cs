using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using OfficeOpenXml.Table;
using static EP360_Logistics_Admin.Helpers.ExcelTableroHelper;

namespace EP360_Logistics_Admin.Helpers
{
    // Excel de Logistics con el formato NUEVO de los reportes de EP360_Traceability (ExcelModuloHelper / Recibo USA,
    // 2026-11-02), pedido 2026-11-05 ("los excel no estan como los nuevos de traceability"):
    //   - hoja "Tablero" sin cuadricula: banda navy con el wordmark, tarjetas con franja de color, secciones con barra
    //     a la izquierda y graficas nativas de barras (sus numeros viven en la hoja oculta "Datos");
    //   - hoja "Detalle" como TABLA de Excel: banda, encabezado navy con linea de marca, renglones alternos que siguen
    //     alternando al filtrar, pastillas en la columna de estado, fila de totales, panel congelado e impresion carta.
    // Las piezas vienen de ExcelTableroHelper (copia del de Traceability).
    public static class ExcelEP360Helper
    {
        private const string FormatoFecha = "dd/mm/yyyy hh:mm";
        private const string FormatoSoloFecha = "dd/mm/yyyy";

        public class Tarjeta
        {
            public string Etiqueta { get; set; }
            public object Valor { get; set; }
            public string Nota { get; set; }
            public string Color { get; set; }   // "rojo" = alerta (franja y cifra en rojo); cualquier otro = normal
        }

        public class TablaConteo
        {
            public string Titulo { get; set; }
            public List<KeyValuePair<string, int>> Renglones { get; set; } = new List<KeyValuePair<string, int>>();
        }

        // colorPorFila: tono de la pastilla en la columna indiceColumnaColor ("azul"/"navy" = bien, "gris" = apagado,
        // "rojo" = critico, null = sin pastilla).
        public static byte[] Generar(string titulo, string subtitulo, List<Tarjeta> tarjetas, List<TablaConteo> tablas,
            IList<string> encabezados, IList<object[]> filas, int indiceColumnaColor = -1, Func<object[], string> colorPorFila = null)
        {
            using (var paquete = new ExcelPackage())
            {
                var libro = paquete.Workbook;
                var tablero = libro.Worksheets.Add("Tablero");
                var detalle = libro.Worksheets.Add("Detalle");
                var datos = new HojaDatos(libro.Worksheets.Add("Datos"));

                EscribirTablero(tablero, datos, titulo, subtitulo, tarjetas, tablas, filas.Count);
                EscribirDetalle(detalle, titulo, subtitulo, encabezados, filas, indiceColumnaColor, colorPorFila);

                datos.Hoja.Hidden = eWorkSheetHidden.Hidden;
                libro.Properties.Title = titulo;
                libro.Properties.Company = "EP Logistics";
                return paquete.GetAsByteArray();
            }
        }

        private static void EscribirTablero(ExcelWorksheet hoja, HojaDatos datos, string titulo, string subtitulo,
            List<Tarjeta> tarjetas, List<TablaConteo> tablas, int registros)
        {
            hoja.View.ShowGridLines = false;
            hoja.Cells.Style.Font.Name = Fuente;
            hoja.Cells.Style.Font.Size = 9;
            hoja.Cells.Style.Font.Color.SetColor(Navy);
            hoja.Column(1).Width = 2.5;
            for (int c = ColIni; c <= ColFin; c++) hoja.Column(c).Width = AnchoColumna;
            hoja.Column(ColFin + 1).Width = 2.5;

            int f = EscribirBanda(hoja, "Tablero", ColFin + 1, titulo);
            hoja.Cells[f, ColIni].Value = subtitulo;
            hoja.Cells[f, ColIni].Style.Font.Size = 8;
            hoja.Cells[f, ColIni].Style.Font.Color.SetColor(Muted);
            f += 2;

            var lista = tarjetas != null && tarjetas.Count > 0
                ? tarjetas
                : new List<Tarjeta> { new Tarjeta { Etiqueta = "Registros", Valor = registros } };
            f = EscribirTarjetas(hoja, f, lista.Select(t => new ExcelTableroHelper.Tarjeta(t.Etiqueta, t.Valor, t.Nota ?? "", t.Color == "rojo")).ToList());

            // Tablas de conteo como graficas de barras horizontales, de dos en dos (como "Quien los crea" y "Clientes").
            var conDatos = (tablas ?? new List<TablaConteo>()).Where(t => t.Renglones.Count > 0).ToList();
            for (int i = 0; i < conDatos.Count; i += 2)
            {
                var izquierda = conDatos[i];
                var derecha = i + 1 < conDatos.Count ? conDatos[i + 1] : null;
                int colDer = ColIni + 6;
                EscribirSeccion(hoja, f, ColIni, derecha == null ? ColFin : ColIni + 5, izquierda.Titulo, Nota(izquierda));
                if (derecha != null) EscribirSeccion(hoja, f, colDer, ColFin, derecha.Titulo, Nota(derecha));
                f += 2;
                int finIzq = Grafica(hoja, datos, "g" + i, f, ColIni, derecha == null ? PxAncho : PxMitad, izquierda);
                int finDer = derecha == null ? f : Grafica(hoja, datos, "g" + (i + 1), f, colDer, PxMitad, derecha);
                f = Math.Max(finIzq, finDer) + 1;
            }

            hoja.PrinterSettings.Orientation = eOrientation.Portrait;
            hoja.PrinterSettings.PaperSize = ePaperSize.Letter;
            hoja.PrinterSettings.FitToPage = true;
            hoja.PrinterSettings.FitToWidth = 1;
            hoja.PrinterSettings.FitToHeight = 0;
            hoja.PrinterSettings.HorizontalCentered = true;
        }

        private static string Nota(TablaConteo t)
        {
            int total = t.Renglones.Sum(r => r.Value);
            return total.ToString("N0") + (t.Renglones.Count > 12 ? " · los 12 principales" : "");
        }

        private static int Grafica(ExcelWorksheet hoja, HojaDatos datos, string nombre, int fila, int col, int ancho, TablaConteo t)
        {
            var renglones = t.Renglones.Take(12).ToList();
            // Con cifras chicas (1, 2, 3) Excel repite etiquetas en el eje ("0 0 1 1 1"); con un tope minimo de 5 no.
            double? maximo = renglones.Max(r => r.Value) < 5 ? 5 : (double?)null;
            return GraficaSeveridad(hoja, datos, nombre, fila, col, ancho, 40 + renglones.Count * 26, true,
                renglones.Select(r => r.Key).ToList(), renglones.Select(r => (double)r.Value).ToList(),
                renglones.Select(r => Tono.Warn).ToList(), "0", maximo, Warn);
        }

        private static void EscribirDetalle(ExcelWorksheet hoja, string titulo, string subtitulo, IList<string> encabezados,
            IList<object[]> filas, int indiceColumnaColor, Func<object[], string> colorPorFila)
        {
            int columnas = encabezados.Count;
            hoja.Cells.Style.Font.Name = Fuente;
            hoja.Cells.Style.Font.Size = 9;

            for (int c = 0; c < columnas; c++)
            {
                var primero = filas.Count > 0 ? filas[0][c] : null;
                double ancho = primero is DateTime ? 16 : (EsNumero(primero) ? 11 : (primero is bool ? 9 : 24));
                if (filas.Count > 0 && !(primero is DateTime) && !EsNumero(primero))
                {
                    int largo = filas.Take(300).Max(f => Convert.ToString(f[c] ?? "").Length);
                    ancho = Math.Max(10, Math.Min(46, largo * 0.95 + 2));
                }
                hoja.Column(c + 1).Width = Math.Max(ancho, Math.Min(30, encabezados[c].Length + 4));
            }
            int colBanda = Math.Max(columnas, 4);

            // Banda de 2 renglones a todo el ancho + linea de marca.
            var banda = hoja.Cells[1, 1, 2, colBanda];
            banda.Style.Fill.PatternType = ExcelFillStyle.Solid;
            banda.Style.Fill.BackgroundColor.SetColor(Navy);
            hoja.Row(1).Height = 32;
            hoja.Row(2).Height = 16;
            hoja.Row(3).Height = 3;
            var lineaMarca = hoja.Cells[3, 1, 3, colBanda];
            lineaMarca.Style.Fill.PatternType = ExcelFillStyle.Solid;
            lineaMarca.Style.Fill.BackgroundColor.SetColor(MarcaPortal);
            var marca = hoja.Cells[1, 1];
            marca.Style.VerticalAlignment = ExcelVerticalAlignment.Bottom;
            var ep = marca.RichText.Add("EP"); ep.Bold = true; ep.Size = 20; ep.Color = Color.White; ep.FontName = Fuente;
            var tres = marca.RichText.Add("360"); tres.Bold = true; tres.Size = 20; tres.Color = MarcaPortalClara; tres.FontName = Fuente;
            var nombre = marca.RichText.Add("    Detalle · " + titulo); nombre.Bold = true; nombre.Size = 12; nombre.Color = Color.White; nombre.FontName = Fuente;
            hoja.Cells[2, 1].Value = filas.Count.ToString("N0") + " registros   ·   " + subtitulo;
            hoja.Cells[2, 1].Style.Font.Size = 7;
            hoja.Cells[2, 1].Style.Font.Color.SetColor(MarcaClara);
            hoja.Cells[2, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Top;

            int encabezado = 5;
            // Una tabla de Excel no admite encabezados repetidos ni vacios.
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < columnas; c++)
            {
                string texto = string.IsNullOrWhiteSpace(encabezados[c]) ? "Columna " + (c + 1) : encabezados[c];
                string unico = texto; int n = 2;
                while (!usados.Add(unico)) unico = texto + " " + n++;
                hoja.Cells[encabezado, c + 1].Value = unico;
                // El encabezado se alinea como sus valores: numeros a la derecha.
                var muestraEnc = filas.Select(x => c < x.Length ? x[c] : null).FirstOrDefault(v => v != null);
                if (EsNumero(muestraEnc)) hoja.Cells[encabezado, c + 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            }
            var enc = hoja.Cells[encabezado, 1, encabezado, columnas];
            hoja.Row(encabezado).Height = 30;
            enc.Style.WrapText = true;
            enc.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            enc.Style.Fill.PatternType = ExcelFillStyle.Solid;
            enc.Style.Fill.BackgroundColor.SetColor(Navy);
            enc.Style.Font.Color.SetColor(Color.White);
            enc.Style.Font.Bold = true;
            enc.Style.Border.Bottom.Style = ExcelBorderStyle.Thick;
            enc.Style.Border.Bottom.Color.SetColor(Marca);

            if (filas.Count == 0)
            {
                hoja.Cells[encabezado + 1, 1].Value = "Sin resultados con esos filtros.";
                return;
            }

            int f = encabezado + 1;
            foreach (var fila in filas)
            {
                string tono = colorPorFila == null || indiceColumnaColor < 0 ? null : colorPorFila(fila);
                for (int c = 0; c < columnas && c < fila.Length; c++)
                {
                    var x = hoja.Cells[f, c + 1];
                    object valor = fila[c];
                    x.Value = valor is bool ? ((bool)valor ? "Sí" : "No") : valor;
                    if (valor is DateTime) x.Style.Numberformat.Format = ((DateTime)valor).TimeOfDay == TimeSpan.Zero ? FormatoSoloFecha : FormatoFecha;
                    else if (valor is decimal) x.Style.Numberformat.Format = FormatoCantidad((decimal)valor);
                    else if (EsNumero(valor)) x.Style.Numberformat.Format = "#,##0";
                    if (EsNumero(valor)) x.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    if (c == 0) x.Style.Font.Bold = true;
                    if (c == indiceColumnaColor && tono != null) Pastilla(x, tono);
                }
                f++;
            }

            int ultima = f - 1;
            var tabla = hoja.Tables.Add(hoja.Cells[encabezado, 1, ultima, columnas], "Detalle");
            tabla.TableStyle = TableStyles.None;
            tabla.ShowRowStripes = false;
            tabla.ShowTotal = true;
            var cuerpo = hoja.Cells[encabezado + 1, 1, ultima, columnas];
            cuerpo.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            cuerpo.Style.Border.Bottom.Color.SetColor(Linea);
            cuerpo.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            var muestra = filas[0];
            for (int c = 0; c < columnas; c++)
            {
                if (c == 0) tabla.Columns[c].TotalsRowLabel = "Total";
                else if (EsNumero(filas.Select(x => c < x.Length ? x[c] : null).FirstOrDefault(v => v != null)))
                {
                    tabla.Columns[c].TotalsRowFunction = RowFunctions.Sum;
                    hoja.Cells[ultima + 1, c + 1].Style.Numberformat.Format = "#,##0";
                }
                else if (c == columnas - 1)
                {
                    // Conteo de registros visibles (respeta los filtros) en la ultima columna.
                    tabla.Columns[c].TotalsRowFunction = RowFunctions.Count;
                }
                // Renglones alternos con formato condicional: siguen alternando al filtrar. Las pastillas tienen su relleno.
                if (c != indiceColumnaColor)
                {
                    var rayas = hoja.ConditionalFormatting.AddExpression(new ExcelAddress(encabezado + 1, c + 1, ultima, c + 1));
                    rayas.Formula = "MOD(SUBTOTAL(103,$A$" + (encabezado + 1) + ":$A" + (encabezado + 1) + "),2)=0";
                    rayas.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    rayas.Style.Fill.BackgroundColor.Color = MarcaSuave;
                }
            }
            var totales = hoja.Cells[ultima + 1, 1, ultima + 1, columnas];
            totales.Style.Font.Bold = true;
            totales.Style.Fill.PatternType = ExcelFillStyle.Solid;
            totales.Style.Fill.BackgroundColor.SetColor(Hundido);
            totales.Style.Font.Color.SetColor(Navy);
            totales.Style.Border.Top.Style = ExcelBorderStyle.Medium;
            totales.Style.Border.Top.Color.SetColor(Navy);

            hoja.View.FreezePanes(encabezado + 1, 2);
            hoja.PrinterSettings.RepeatRows = hoja.Cells[encabezado + ":" + encabezado];
            hoja.PrinterSettings.Orientation = eOrientation.Landscape;
            hoja.PrinterSettings.PaperSize = ePaperSize.Letter;
            hoja.PrinterSettings.FitToPage = true;
            hoja.PrinterSettings.FitToWidth = 1;
            hoja.PrinterSettings.FitToHeight = 0;
        }

        private static void Pastilla(ExcelRange celda, string tono)
        {
            if (tono == "rojo")
            {
                celda.Style.Fill.PatternType = ExcelFillStyle.Solid;
                celda.Style.Fill.BackgroundColor.SetColor(PastelRojoFondo);
                celda.Style.Font.Color.SetColor(PastelRojoTexto);
                celda.Style.Font.Bold = true;
                celda.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }
            else
            {
                PintarPastel(celda, tono == "azul" || tono == "navy");
            }
        }

        private static bool EsNumero(object v) { return v is int || v is long || v is decimal || v is double; }
    }
}
