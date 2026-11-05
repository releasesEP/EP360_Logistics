using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OfficeOpenXml;
using OfficeOpenXml.ConditionalFormatting;
using OfficeOpenXml.Style;

namespace EP360_Logistics_Admin.Helpers
{
    // Excel con el MISMO formato que los reportes de EP360_Traceability (Helpers/ExcelReporteHelper.cs de ese repo):
    //   - hoja "Resumen" primero (el archivo abre en el resumen): banda navy con el wordmark EP360, subtitulo, tarjetas KPI
    //     (caja con borde, etiqueta chica, numero grande) y tablas de conteo con barras de datos nativas;
    //   - hoja "Detalle": encabezado azul con linea navy, filas alternas en azul claro, autofiltro, encabezado congelado,
    //     fila de total con doble linea y formato por TIPO real (fechas y numeros se ordenan y filtran como tales).
    // Lo unico propio de Logistics: el "360" del wordmark y el nombre del portal van en el guinda claro de la marca.
    public static class ExcelEP360Helper
    {
        private static readonly Color ColorNavyTitulo = ColorTranslator.FromHtml("#0B1B3D");
        private static readonly Color ColorAzulEncabezado = ColorTranslator.FromHtml("#1D6FD1");
        private static readonly Color ColorGuindaSobreNavy = ColorTranslator.FromHtml("#E07A8E");
        private static readonly Color ColorSubtituloFondo = ColorTranslator.FromHtml("#EEF2FF");
        private static readonly Color ColorSubtituloTexto = ColorTranslator.FromHtml("#8B909C");
        private static readonly Color BordeGris = ColorTranslator.FromHtml("#D9D9D9");
        private static readonly Color BordeTarjeta = ColorTranslator.FromHtml("#C9CDD4");
        private static readonly Color BandaClara = ColorTranslator.FromHtml("#EAF1FB");
        private static readonly Color ColorTextoPrincipal = ColorTranslator.FromHtml("#1F2430");

        // Mismos pares fondo/texto que las pastillas de estatus de Traceability.
        private static readonly Dictionary<string, (Color Fondo, Color Texto)> ColoresEstatus = new Dictionary<string, (Color, Color)>
        {
            ["gris"] = (ColorTranslator.FromHtml("#EEEEEC"), ColorTranslator.FromHtml("#55564F")),
            ["azul"] = (ColorTranslator.FromHtml("#E6F1FB"), ColorTranslator.FromHtml("#0C447C")),
            ["navy"] = (ColorTranslator.FromHtml("#E3E8F2"), ColorTranslator.FromHtml("#0B1B3D")),
            ["rojo"] = (ColorTranslator.FromHtml("#FCEBEB"), ColorTranslator.FromHtml("#A32D2D")),
        };

        public class Tarjeta
        {
            public string Etiqueta { get; set; }
            public object Valor { get; set; }
            public string Nota { get; set; }
            public string Color { get; set; }   // gris | azul | navy | rojo (opcional)
        }

        public class TablaConteo
        {
            public string Titulo { get; set; }
            public List<KeyValuePair<string, int>> Renglones { get; set; } = new List<KeyValuePair<string, int>>();
        }

        // colorPorFila: color de estatus (gris/azul/navy/rojo) que se pinta en la columna indiceColumnaColor de cada fila.
        public static byte[] Generar(string titulo, string subtitulo, List<Tarjeta> tarjetas, List<TablaConteo> tablas,
            IList<string> encabezados, IList<object[]> filas, int indiceColumnaColor = -1, Func<object[], string> colorPorFila = null)
        {
            using (var paquete = new ExcelPackage())
            {
                // ---- Resumen ----
                var resumen = paquete.Workbook.Worksheets.Add("Resumen");
                var listaTarjetas = tarjetas != null && tarjetas.Count > 0
                    ? tarjetas
                    : new List<Tarjeta> { new Tarjeta { Etiqueta = "Registros", Valor = filas.Count } };
                // La banda cubre todas las tarjetas (3 columnas cada una, la ultima sin columna de aire).
                EscribirEncabezado(resumen, titulo, subtitulo, Math.Max(8, listaTarjetas.Count * 3 - 1));

                const int filaTarjetas = 4;
                for (int t = 0; t < listaTarjetas.Count; t++)
                    EscribirTarjeta(resumen, filaTarjetas, t * 3 + 1, listaTarjetas[t]);
                resumen.Row(filaTarjetas + 1).Height = 26;

                const int filaTablas = 8;
                if (tablas != null)
                    for (int t = 0; t < tablas.Count; t++)
                        EscribirTablaConteo(resumen, filaTablas, t * 3 + 1, tablas[t]);

                int columnasResumen = Math.Max(listaTarjetas.Count, tablas == null ? 0 : tablas.Count) * 3;
                for (int c = 1; c <= Math.Max(columnasResumen, 6); c++)
                    resumen.Column(c).Width = c % 3 == 1 ? 30 : c % 3 == 2 ? 12 : 3;
                ConfigurarImpresion(resumen);

                // ---- Detalle ----
                var hoja = paquete.Workbook.Worksheets.Add("Detalle");
                EscribirEncabezado(hoja, titulo, subtitulo, Math.Max(encabezados.Count, 2));

                const int filaEncabezados = 4;
                EscribirFilaEncabezadoTabla(hoja, filaEncabezados, 1, encabezados);
                hoja.Row(filaEncabezados).Height = 22;

                for (int f = 0; f < filas.Count; f++)
                {
                    bool filaPar = f % 2 == 0;
                    string color = colorPorFila == null ? null : colorPorFila(filas[f]);
                    for (int c = 0; c < encabezados.Count; c++)
                    {
                        var celda = hoja.Cells[filaEncabezados + 1 + f, c + 1];
                        object valor = c < filas[f].Length ? filas[f][c] : null;
                        celda.Value = valor is bool ? ((bool)valor ? "Sí" : "No") : valor;
                        AplicarFormatoNumerico(celda, valor);
                        AplicarBorde(celda);

                        if (c == indiceColumnaColor && color != null && ColoresEstatus.ContainsKey(color))
                        {
                            var colores = ColoresEstatus[color];
                            celda.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            celda.Style.Fill.BackgroundColor.SetColor(colores.Fondo);
                            celda.Style.Font.Color.SetColor(colores.Texto);
                            celda.Style.Font.Bold = true;
                            celda.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        }
                        else if (filaPar)
                        {
                            celda.Style.Fill.PatternType = ExcelFillStyle.Solid;
                            celda.Style.Fill.BackgroundColor.SetColor(BandaClara);
                        }
                    }
                }

                // Total: etiqueta a la izquierda y conteo en la ULTIMA columna, fuera del autofiltro.
                int filaTotal = filaEncabezados + 1 + filas.Count;
                var banda = hoja.Cells[filaTotal, 1, filaTotal, encabezados.Count];
                banda.Style.Font.Bold = true;
                banda.Style.Fill.PatternType = ExcelFillStyle.Solid;
                banda.Style.Fill.BackgroundColor.SetColor(ColorSubtituloFondo);
                banda.Style.Border.Top.Style = ExcelBorderStyle.Double;
                banda.Style.Border.Top.Color.SetColor(ColorNavyTitulo);
                hoja.Cells[filaTotal, 1].Value = "Total";
                var total = hoja.Cells[filaTotal, encabezados.Count];
                total.Value = filas.Count;
                total.Style.Numberformat.Format = "#,##0";
                total.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;

                if (filas.Count > 0)
                    hoja.Cells[filaEncabezados, 1, filaEncabezados + filas.Count, encabezados.Count].AutoFilter = true;

                hoja.Cells[filaEncabezados, 1, filaTotal, encabezados.Count].AutoFitColumns(10, 48);
                hoja.View.FreezePanes(filaEncabezados + 1, 1);
                ConfigurarImpresion(hoja);
                hoja.PrinterSettings.RepeatRows = hoja.Cells[filaEncabezados + ":" + filaEncabezados];

                return paquete.GetAsByteArray();
            }
        }

        private static void ConfigurarImpresion(ExcelWorksheet hoja)
        {
            hoja.PrinterSettings.Orientation = eOrientation.Landscape;
            hoja.PrinterSettings.FitToPage = true;
            hoja.PrinterSettings.FitToWidth = 1;
            hoja.PrinterSettings.FitToHeight = 0;
            hoja.PrinterSettings.ShowGridLines = false;
        }

        private static void AplicarFormatoNumerico(ExcelRange celda, object valor)
        {
            if (valor is DateTime) celda.Style.Numberformat.Format = ((DateTime)valor).TimeOfDay == TimeSpan.Zero ? "dd/mm/yyyy" : "dd/mm/yyyy hh:mm";
            else if (valor is decimal || valor is double) celda.Style.Numberformat.Format = "#,##0.00";
            else if (valor is int || valor is long) celda.Style.Numberformat.Format = "#,##0";
        }

        private static void AplicarBorde(ExcelRange celda)
        {
            celda.Style.Border.Top.Style = celda.Style.Border.Bottom.Style =
                celda.Style.Border.Left.Style = celda.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            celda.Style.Border.Top.Color.SetColor(BordeGris);
            celda.Style.Border.Bottom.Color.SetColor(BordeGris);
            celda.Style.Border.Left.Color.SetColor(BordeGris);
            celda.Style.Border.Right.Color.SetColor(BordeGris);
        }

        // Banda navy con el wordmark "EP360 | Logistics" y el nombre del reporte en texto enriquecido.
        private static void EscribirEncabezado(ExcelWorksheet hoja, string titulo, string subtitulo, int ultimaColumna)
        {
            hoja.Row(1).Height = 30;
            hoja.Cells[1, 1, 1, ultimaColumna].Merge = true;
            var celda = hoja.Cells[1, 1];
            celda.IsRichText = true;
            var ep = celda.RichText.Add("  EP");
            ep.Bold = true; ep.Size = 16; ep.Color = Color.White;
            var tres60 = celda.RichText.Add("360");
            tres60.Bold = true; tres60.Size = 16; tres60.Color = ColorGuindaSobreNavy;
            var portal = celda.RichText.Add("  Logistics");
            portal.Bold = false; portal.Size = 12; portal.Color = ColorGuindaSobreNavy;
            var nombre = celda.RichText.Add("     " + titulo);
            nombre.Bold = true; nombre.Size = 14; nombre.Color = Color.White;
            celda.Style.Fill.PatternType = ExcelFillStyle.Solid;
            celda.Style.Fill.BackgroundColor.SetColor(ColorNavyTitulo);
            celda.Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            hoja.Cells[2, 1, 2, ultimaColumna].Merge = true;
            var sub = hoja.Cells[2, 1];
            sub.Value = "  " + subtitulo;
            sub.Style.Font.Size = 10;
            sub.Style.Font.Color.SetColor(ColorSubtituloTexto);
            sub.Style.Fill.PatternType = ExcelFillStyle.Solid;
            sub.Style.Fill.BackgroundColor.SetColor(ColorSubtituloFondo);
            hoja.Row(3).Height = 7;
        }

        private static void EscribirFilaEncabezadoTabla(ExcelWorksheet hoja, int fila, int colInicio, IList<string> textos)
        {
            for (int i = 0; i < textos.Count; i++)
            {
                var celda = hoja.Cells[fila, colInicio + i];
                celda.Value = textos[i];
                celda.Style.Font.Bold = true;
                celda.Style.Font.Size = 9;
                celda.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                celda.Style.Font.Color.SetColor(Color.White);
                celda.Style.Fill.PatternType = ExcelFillStyle.Solid;
                celda.Style.Fill.BackgroundColor.SetColor(ColorAzulEncabezado);
                celda.Style.Border.Left.Style = celda.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                celda.Style.Border.Left.Color.SetColor(BordeGris);
                celda.Style.Border.Right.Color.SetColor(BordeGris);
                celda.Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
                celda.Style.Border.Bottom.Color.SetColor(ColorNavyTitulo);
            }
        }

        // Tarjeta KPI: 2 columnas x 3 filas (etiqueta / numero / nota) con una sola caja (BorderAround, no borde por celda).
        private static void EscribirTarjeta(ExcelWorksheet hoja, int fila, int col, Tarjeta t)
        {
            hoja.Cells[fila, col, fila, col + 1].Merge = true;
            var etiqueta = hoja.Cells[fila, col];
            etiqueta.Value = (t.Etiqueta ?? "").ToUpperInvariant();
            etiqueta.Style.Font.Bold = true;
            etiqueta.Style.Font.Size = 9;
            etiqueta.Style.Font.Color.SetColor(ColorSubtituloTexto);
            etiqueta.Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            hoja.Cells[fila + 1, col, fila + 1, col + 1].Merge = true;
            var valor = hoja.Cells[fila + 1, col];
            valor.Value = t.Valor;
            if (t.Valor is int || t.Valor is long) valor.Style.Numberformat.Format = "#,##0";
            valor.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
            valor.Style.Font.Bold = true;
            valor.Style.Font.Size = 20;
            valor.Style.Font.Color.SetColor(t.Color != null && ColoresEstatus.ContainsKey(t.Color) ? ColoresEstatus[t.Color].Texto : ColorTextoPrincipal);
            valor.Style.VerticalAlignment = ExcelVerticalAlignment.Center;

            hoja.Cells[fila + 2, col, fila + 2, col + 1].Merge = true;
            var nota = hoja.Cells[fila + 2, col];
            nota.Value = t.Nota ?? "";
            nota.Style.Font.Size = 9;
            nota.Style.Font.Color.SetColor(ColorSubtituloTexto);

            var caja = hoja.Cells[fila, col, fila + 2, col + 1];
            caja.Style.Border.BorderAround(ExcelBorderStyle.Thin, BordeTarjeta);
            caja.Style.Indent = 1;
        }

        // Tabla de conteo con barra de datos nativa (formato condicional, minimo anclado en 0).
        private static void EscribirTablaConteo(ExcelWorksheet hoja, int filaEncabezado, int col, TablaConteo tabla)
        {
            EscribirFilaEncabezadoTabla(hoja, filaEncabezado, col, new[] { tabla.Titulo, "Cantidad" });
            for (int i = 0; i < tabla.Renglones.Count; i++)
            {
                int fila = filaEncabezado + 1 + i;
                var etiqueta = hoja.Cells[fila, col];
                etiqueta.Value = tabla.Renglones[i].Key;
                AplicarBorde(etiqueta);
                if (i % 2 == 0)
                {
                    etiqueta.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    etiqueta.Style.Fill.BackgroundColor.SetColor(BandaClara);
                }
                var cantidad = hoja.Cells[fila, col + 1];
                cantidad.Value = tabla.Renglones[i].Value;
                cantidad.Style.Numberformat.Format = "#,##0";
                AplicarBorde(cantidad);
            }
            if (tabla.Renglones.Count > 0)
            {
                var rango = new ExcelAddress(filaEncabezado + 1, col + 1, filaEncabezado + tabla.Renglones.Count, col + 1);
                var barra = hoja.ConditionalFormatting.AddDatabar(rango, ColorAzulEncabezado);
                barra.LowValue.Type = eExcelConditionalFormattingValueObjectType.Num;
                barra.LowValue.Value = 0;
            }
        }
    }
}
