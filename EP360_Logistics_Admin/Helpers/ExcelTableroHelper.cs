using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Xml;
using OfficeOpenXml;
using OfficeOpenXml.ConditionalFormatting;
using OfficeOpenXml.Drawing;
using OfficeOpenXml.Drawing.Chart;
using OfficeOpenXml.Style;
using OfficeOpenXml.Table;

namespace EP360_Logistics_Admin.Helpers
{
    // Copia de EP360_Traceability/Helpers/ExcelTableroHelper.cs (2026-11-05): las piezas de diseno de los Excel
    // nuevos de Traceability (banda, tarjetas, secciones, tablas, graficas nativas y la hoja oculta "Datos"), para
    // que los Excel de Logistics salgan iguales. Lo unico propio de Logistics: el "360" y la linea bajo la banda
    // van en el guinda de la marca (MarcaPortal); graficas, encabezados y tarjetas conservan los azules.
    // Se quitaron las piezas que dependian de modelos de Traceability (focos de Recibo USA, creados por periodo).
    internal static class ExcelTableroHelper
    {
        // Paleta del portal (Site.css, bloque 17.2 en modo claro) en la escala de azules de los
        // reportes (2026-11-02): Ok = azul claro (va bien), Warn = azul marca (hay que verlo),
        // Lento = azul oscuro (--estatus-azul-fg) y Crit = rojo SOLO para lo realmente crítico
        // (en riesgo, +21 días). Mismos valores que --rep2-* en Site.css.
        internal static readonly Color Navy = ColorTranslator.FromHtml("#0B1B3D");
        internal static readonly Color Marca = ColorTranslator.FromHtml("#1D6FD1");
        internal static readonly Color MarcaClara = ColorTranslator.FromHtml("#DCE4F5");
        internal static readonly Color MarcaSuave = ColorTranslator.FromHtml("#E6F1FB");
        internal static readonly Color Muted = ColorTranslator.FromHtml("#5B6270");
        internal static readonly Color Linea = ColorTranslator.FromHtml("#D9DCE1");
        internal static readonly Color Rejilla = ColorTranslator.FromHtml("#EEEEEC");
        internal static readonly Color Hundido = ColorTranslator.FromHtml("#F4F5F7");
        internal static readonly Color Ok = ColorTranslator.FromHtml("#A9C8EE");
        internal static readonly Color Warn = ColorTranslator.FromHtml("#1D6FD1");
        internal static readonly Color Lento = ColorTranslator.FromHtml("#0C447C");
        internal static readonly Color Crit = ColorTranslator.FromHtml("#A32D2D");
        internal static readonly Color SinTipo = ColorTranslator.FromHtml("#8B909C");
        // Guinda de EP360 Logistics: "360" del wordmark y linea bajo la banda.
        internal static readonly Color MarcaPortal = ColorTranslator.FromHtml("#8C1C33");
        internal static readonly Color MarcaPortalClara = ColorTranslator.FromHtml("#E07A8E");
        internal static readonly Color[] Categorias =
        {
            ColorTranslator.FromHtml("#0C447C"), ColorTranslator.FromHtml("#83B0E6"), ColorTranslator.FromHtml("#1D6FD1"),
            ColorTranslator.FromHtml("#0369A1"), ColorTranslator.FromHtml("#92ABC4"), ColorTranslator.FromHtml("#81B4D0")
        };

        // Pastel de Sí/Falta y semáforo del detalle, también en azules: "Falta" no es crítico.
        internal static readonly Color PastelMalFondo = ColorTranslator.FromHtml("#DCE4F5");
        internal static readonly Color PastelMalTexto = ColorTranslator.FromHtml("#0C447C");
        internal static readonly Color PastelBienFondo = ColorTranslator.FromHtml("#E6F1FB");
        internal static readonly Color PastelBienTexto = ColorTranslator.FromHtml("#1D6FD1");
        internal static readonly Color PastelRojoFondo = ColorTranslator.FromHtml("#FCEBEB");
        internal static readonly Color PastelRojoTexto = ColorTranslator.FromHtml("#A32D2D");
        internal static readonly Color EscalaBaja = ColorTranslator.FromHtml("#F4F8FD");
        internal static readonly Color EscalaMedia = ColorTranslator.FromHtml("#CFE1F6");
        internal static readonly Color EscalaAlta = ColorTranslator.FromHtml("#8DB6EA");

        internal const string Fuente = "Arial";

        // Rejilla del tablero: columna A y N de margen, B..M = 12 columnas de contenido.
        internal const int ColIni = 2;
        internal const int ColFin = 13;
        internal const double AnchoColumna = 10.3;
        // Medidas en pixeles para las gráficas (una columna de 10.3 ~ 77 px, un renglón ~ 20 px).
        // Con holgura: EPPlus calcula los pixeles con otra métrica que Excel y a 456 px la gráfica
        // de media página se encimaba con la tabla de al lado.
        internal const int PxAncho = 880;
        internal const int PxMitad = 420;
        internal const int PxRenglon = 20;

        internal enum Tono { Ok, Warn, Lento, Crit }

        // ==================================================================
        // Piezas de diseño (las mismas del PDF)
        // ==================================================================
        // Banda azul marino de 2 renglones + línea azul de marca. Devuelve la fila siguiente.
        internal static int EscribirBanda(ExcelWorksheet hoja, string titulo, int ultimaColumna, string reporte = null)
        {
            var banda = hoja.Cells[1, 1, 2, ultimaColumna];
            banda.Style.Fill.PatternType = ExcelFillStyle.Solid;
            banda.Style.Fill.BackgroundColor.SetColor(Navy);
            hoja.Row(1).Height = 32;
            hoja.Row(2).Height = 16;
            hoja.Row(3).Height = 3;
            var linea = hoja.Cells[3, 1, 3, ultimaColumna];
            linea.Style.Fill.PatternType = ExcelFillStyle.Solid;
            linea.Style.Fill.BackgroundColor.SetColor(MarcaPortal);

            var marca = hoja.Cells[1, 2];
            marca.Style.VerticalAlignment = ExcelVerticalAlignment.Bottom;
            var ep = marca.RichText.Add("EP");
            ep.Bold = true; ep.Size = 20; ep.Color = Color.White; ep.FontName = Fuente;
            var tres = marca.RichText.Add("360");
            tres.Bold = true; tres.Size = 20; tres.Color = MarcaPortalClara; tres.FontName = Fuente;
            var portal = marca.RichText.Add("  Logistics");
            portal.Bold = false; portal.Size = 11; portal.Color = MarcaPortalClara; portal.FontName = Fuente;

            var sub = hoja.Cells[2, 2];
            sub.Value = (reporte ?? "Directorio global").ToUpperInvariant();
            sub.Style.Font.Size = 7;
            sub.Style.Font.Bold = true;
            sub.Style.Font.Color.SetColor(MarcaClara);
            sub.Style.VerticalAlignment = ExcelVerticalAlignment.Top;

            var t = hoja.Cells[1, ultimaColumna - 4, 2, ultimaColumna - 1];
            t.Merge = true;
            t.Value = titulo;
            t.Style.Font.Size = 12;
            t.Style.Font.Bold = true;
            t.Style.Font.Color.SetColor(Color.White);
            t.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            t.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            return 5;
        }

        // Título de sección con barra azul a la izquierda, nota en mayúsculas a la derecha y línea
        // abajo, como DibujarSeccion del PDF. Devuelve la fila donde empieza el contenido.
        internal static int EscribirSeccion(ExcelWorksheet hoja, int fila, int colIni, int colFin, string titulo, string nota)
        {
            var celda = hoja.Cells[fila, colIni];
            celda.Value = "  " + titulo;
            celda.Style.Font.Size = 11;
            celda.Style.Font.Bold = true;
            celda.Style.Font.Color.SetColor(Navy);
            celda.Style.Border.Left.Style = ExcelBorderStyle.Thick;
            celda.Style.Border.Left.Color.SetColor(Marca);
            if (!string.IsNullOrEmpty(nota))
            {
                var n = hoja.Cells[fila, colFin];
                n.Value = nota.ToUpperInvariant();
                n.Style.Font.Size = 7;
                n.Style.Font.Bold = true;
                n.Style.Font.Color.SetColor(Muted);
                n.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            }
            var linea = hoja.Cells[fila, colIni, fila, colFin];
            linea.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            linea.Style.Border.Bottom.Color.SetColor(Linea);
            linea.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            hoja.Row(fila).Height = 22;
            return fila + 2;
        }

        internal static void EtiquetaMini(ExcelWorksheet hoja, int fila, int col, string texto)
        {
            var c = hoja.Cells[fila, col];
            c.Value = texto.ToUpperInvariant();
            c.Style.Font.Size = 7;
            c.Style.Font.Bold = true;
            c.Style.Font.Color.SetColor(Muted);
        }

        internal class Tarjeta
        {
            public string Etiqueta;
            public object Valor;
            public string Nota;
            public bool Alerta;

            public Tarjeta(string etiqueta, object valor, string nota, bool alerta)
            {
                Etiqueta = etiqueta; Valor = valor; Nota = nota; Alerta = alerta;
            }
        }

        // Tarjetas de 3 columnas, 4 por renglón: franja de color arriba, etiqueta, cifra grande y
        // nota, sobre fondo claro. El borde blanco grueso de los lados hace de separación.
        internal static int EscribirTarjetas(ExcelWorksheet hoja, int fila, List<Tarjeta> tarjetas)
        {
            for (int i = 0; i < tarjetas.Count; i++)
            {
                var t = tarjetas[i];
                int f = fila + (i / 4) * 4;
                int c = ColIni + (i % 4) * 3;
                var color = t.Alerta ? Crit : Marca;

                var etiqueta = Fusionar(hoja, f, c, c + 2);
                etiqueta.Value = "  " + t.Etiqueta.ToUpperInvariant();
                etiqueta.Style.Font.Size = 7;
                etiqueta.Style.Font.Bold = true;
                etiqueta.Style.Font.Color.SetColor(Muted);
                etiqueta.Style.VerticalAlignment = ExcelVerticalAlignment.Bottom;
                etiqueta.Style.Border.Top.Style = ExcelBorderStyle.Thick;
                etiqueta.Style.Border.Top.Color.SetColor(color);

                var cifra = Fusionar(hoja, f + 1, c, c + 2);
                cifra.Value = t.Valor;
                cifra.Style.Font.Size = 20;
                cifra.Style.Font.Bold = true;
                cifra.Style.Font.Color.SetColor(t.Alerta ? Crit : Navy);
                cifra.Style.Indent = 1;
                cifra.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
                cifra.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                if (t.Valor is decimal) cifra.Style.Numberformat.Format = FormatoCantidad((decimal)t.Valor);

                var nota = Fusionar(hoja, f + 2, c, c + 2);
                nota.Value = "  " + t.Nota;
                nota.Style.Font.Size = 7;
                nota.Style.Font.Color.SetColor(Muted);
                nota.Style.VerticalAlignment = ExcelVerticalAlignment.Top;

                var caja = hoja.Cells[f, c, f + 2, c + 2];
                caja.Style.Fill.PatternType = ExcelFillStyle.Solid;
                caja.Style.Fill.BackgroundColor.SetColor(Hundido);
                for (int k = 0; k < 3; k++)
                {
                    hoja.Cells[f + k, c].Style.Border.Left.Style = ExcelBorderStyle.Thick;
                    hoja.Cells[f + k, c].Style.Border.Left.Color.SetColor(Color.White);
                    hoja.Cells[f + k, c + 2].Style.Border.Right.Style = ExcelBorderStyle.Thick;
                    hoja.Cells[f + k, c + 2].Style.Border.Right.Color.SetColor(Color.White);
                }
                hoja.Row(f).Height = 18;
                hoja.Row(f + 1).Height = 30;
                hoja.Row(f + 2).Height = 15;
                hoja.Row(f + 3).Height = 8;
            }
            return fila + ((tarjetas.Count + 3) / 4) * 4 + 1;
        }

        // Focos de cualquier reporte: (etiqueta, valor, esRiesgo). Solo "en riesgo" va en rojo.
        // Caben 6 en el ancho del tablero.
        internal static int EscribirFocos(ExcelWorksheet hoja, int fila, int abiertos, List<Tuple<string, int, bool>> focos)
        {
            for (int i = 0; i < focos.Count && i < 6; i++)
            {
                int c = ColIni + i * 2;
                int valor = focos[i].Item2;
                int pct = Pct(valor, abiertos);
                var color = valor == 0 ? Ok : (focos[i].Item3 ? Crit : (pct >= 30 ? Lento : Warn));

                var numero = hoja.Cells[fila, c];
                numero.Value = valor;
                numero.Style.Font.Size = 18;
                numero.Style.Font.Bold = true;
                numero.Style.Font.Color.SetColor(color == Ok ? Warn : color);
                numero.Style.Indent = 1;
                numero.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
                var porcentaje = hoja.Cells[fila, c + 1];
                porcentaje.Value = pct + "%";
                porcentaje.Style.Font.Size = 7;
                porcentaje.Style.Font.Color.SetColor(Muted);
                porcentaje.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                porcentaje.Style.VerticalAlignment = ExcelVerticalAlignment.Top;
                var etiqueta = Fusionar(hoja, fila + 1, c, c + 1);
                etiqueta.Value = "  " + focos[i].Item1;
                etiqueta.Style.Font.Size = 7;
                etiqueta.Style.Font.Color.SetColor(Muted);

                var caja = hoja.Cells[fila, c, fila + 1, c + 1];
                caja.Style.Fill.PatternType = ExcelFillStyle.Solid;
                caja.Style.Fill.BackgroundColor.SetColor(Hundido);
                for (int k = 0; k < 2; k++)
                {
                    hoja.Cells[fila + k, c].Style.Border.Left.Style = ExcelBorderStyle.Thick;
                    hoja.Cells[fila + k, c].Style.Border.Left.Color.SetColor(color);
                    hoja.Cells[fila + k, c + 1].Style.Border.Right.Style = ExcelBorderStyle.Thick;
                    hoja.Cells[fila + k, c + 1].Style.Border.Right.Color.SetColor(Color.White);
                }
            }
            hoja.Row(fila).Height = 28;
            hoja.Row(fila + 1).Height = 16;
            return fila + 3;
        }

        // Tabla como DibujarTablaSimple del PDF: encabezado azul de marca en mayúsculas, renglones
        // alternos azul muy claro, primera columna en negritas, números a la derecha. fusionar =
        // columnas que se juntan con la de su derecha (su encabezado siguiente va vacío).
        internal static int EscribirTablaPdf(ExcelWorksheet hoja, int fila, int colIni, string[] encabezados, List<object[]> filas,
            Func<int, int, Color?> colorCelda, Dictionary<int, string> formatos, int[] fusionar = null)
        {
            var juntar = new HashSet<int>(fusionar ?? new int[0]);
            int colFin = colIni + encabezados.Length - 1;

            for (int c = 0; c < encabezados.Length; c++)
            {
                var celda = hoja.Cells[fila, colIni + c];
                celda.Value = string.IsNullOrEmpty(encabezados[c]) ? null : encabezados[c].ToUpperInvariant();
                // El encabezado se alinea como sus valores: texto a la izquierda, números a la derecha.
                var primero = filas.Count > 0 && c < filas[0].Length ? filas[0][c] : null;
                bool numerico = primero is int || primero is decimal || primero is double;
                celda.Style.HorizontalAlignment = numerico ? ExcelHorizontalAlignment.Right : ExcelHorizontalAlignment.Left;
            }
            var enc = hoja.Cells[fila, colIni, fila, colFin];
            enc.Style.Fill.PatternType = ExcelFillStyle.Solid;
            enc.Style.Fill.BackgroundColor.SetColor(Marca);
            enc.Style.Font.Color.SetColor(Color.White);
            enc.Style.Font.Bold = true;
            enc.Style.Font.Size = 7;
            enc.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            enc.Style.WrapText = true;
            hoja.Row(fila).Height = 22;
            fila++;

            for (int f = 0; f < filas.Count; f++)
            {
                var renglon = hoja.Cells[fila, colIni, fila, colFin];
                if (f % 2 == 1)
                {
                    renglon.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    renglon.Style.Fill.BackgroundColor.SetColor(MarcaSuave);
                }
                renglon.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                for (int c = 0; c < filas[f].Length; c++)
                {
                    var celda = hoja.Cells[fila, colIni + c];
                    celda.Value = filas[f][c];
                    bool numero = filas[f][c] is int || filas[f][c] is decimal || filas[f][c] is double;
                    celda.Style.HorizontalAlignment = numero ? ExcelHorizontalAlignment.Right : ExcelHorizontalAlignment.Left;
                    string formato;
                    if (formatos != null && formatos.TryGetValue(c, out formato)) celda.Style.Numberformat.Format = formato;
                    else if (filas[f][c] is decimal) celda.Style.Numberformat.Format = FormatoCantidad((decimal)filas[f][c]);
                    var color = colorCelda != null ? colorCelda(f, c) : null;
                    if (c == 0 || color.HasValue) celda.Style.Font.Bold = true;
                    if (color.HasValue) celda.Style.Font.Color.SetColor(color.Value);
                }
                foreach (var c in juntar) Fusionar(hoja, fila, colIni + c, colIni + c + 1);
                hoja.Row(fila).Height = 17;
                fila++;
            }
            var abajo = hoja.Cells[fila - 1, colIni, fila - 1, colFin];
            abajo.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            abajo.Style.Border.Bottom.Color.SetColor(Linea);
            return fila + 1;
        }

        internal static ExcelRange Fusionar(ExcelWorksheet hoja, int fila, int colIni, int colFin)
        {
            var rango = hoja.Cells[fila, colIni, fila, colFin];
            rango.Merge = true;
            return rango;
        }

        // ==================================================================
        // Gráficas nativas
        // ==================================================================
        // Barras de severidad (horizontales o verticales). colorUnico pinta todo de un color.
        internal static int GraficaSeveridad(ExcelWorksheet hoja, HojaDatos datos, string nombre, int fila, int col, int ancho, int alto,
            bool horizontal, List<string> categorias, List<double> valores, List<Tono> tonos, string formato,
            double? maximo = null, Color? colorUnico = null)
        {
            var filas = new List<object[]>();
            for (int i = 0; i < categorias.Count; i++)
            {
                filas.Add(new object[]
                {
                    categorias[i],
                    tonos[i] == Tono.Ok ? (object)valores[i] : null,
                    tonos[i] == Tono.Warn ? (object)valores[i] : null,
                    tonos[i] == Tono.Lento ? (object)valores[i] : null,
                    tonos[i] == Tono.Crit ? (object)valores[i] : null
                });
            }
            // El ";;;" esconde ceros y vacíos: sin él, las dos series de otro color pintan su
            // etiqueta "0" encima de la barra de cada categoría.
            var bloque = datos.Agregar(nombre, new[] { "", "ok", "warn", "lento", "crit" }, filas, formato + ";;;");

            var grafica = (ExcelBarChart)hoja.Drawings.AddChart(nombre, horizontal ? eChartType.BarStacked : eChartType.ColumnStacked);
            var colores = colorUnico.HasValue ? new[] { colorUnico.Value, colorUnico.Value, colorUnico.Value, colorUnico.Value } : new[] { Ok, Warn, Lento, Crit };
            for (int k = 0; k < 4; k++)
            {
                if (!tonos.Any(t => (int)t == k)) continue;
                var s = (ExcelBarChartSerie)grafica.Series.Add(bloque.Columna(k + 1), bloque.Columna(0));
                s.Header = bloque.NombreColumna(k + 1);
                Rellenar(s, colores[k]);
                s.DataLabel.ShowValue = true;
                s.DataLabel.Position = eLabelPosition.InEnd;
                s.DataLabel.Font.Size = 8;
                s.DataLabel.Font.Bold = true;
                // Sobre el azul claro la etiqueta blanca no se lee.
                s.DataLabel.Font.Color = colores[k] == Ok ? Navy : Color.White;
            }
            grafica.GapWidth = horizontal ? 45 : 80;
            EstiloGrafica(grafica, leyenda: false);
            grafica.YAxis.MinValue = 0;
            if (maximo.HasValue) grafica.YAxis.MaxValue = maximo.Value;
            if (horizontal)
            {
                // Categorías de arriba hacia abajo en el mismo orden que la tabla / el PDF; el eje de
                // valores se queda abajo cruzando en la última categoría.
                grafica.XAxis.Orientation = eAxisOrientation.MaxMin;
                grafica.YAxis.Crosses = eCrosses.Max;
            }
            return Ubicar(grafica, fila, col, ancho, alto);
        }

        internal static int GraficaDona(ExcelWorksheet hoja, HojaDatos datos, string nombre, int fila, int col, int ancho, int alto,
            List<string> categorias, List<object> valores, List<Color> colores, string serieNombre = "Cantidad")
        {
            var bloque = datos.Agregar(nombre, new[] { "", serieNombre },
                categorias.Select((c, i) => new object[] { c, valores[i] }).ToList());
            var grafica = (ExcelDoughnutChart)hoja.Drawings.AddChart(nombre, eChartType.Doughnut);
            var s = grafica.Series.Add(bloque.Columna(1), bloque.Columna(0));
            s.HeaderAddress = bloque.Encabezado(1);
            grafica.VaryColors = true;
            grafica.DataLabel.ShowPercent = true;
            grafica.DataLabel.Font.Size = 8;
            grafica.DataLabel.Font.Bold = true;
            grafica.DataLabel.Font.Color = Color.White;
            grafica.Legend.Remove();
            grafica.Border.Fill.Style = eFillStyle.NoFill;
            ColorearPuntos(grafica.ChartXml, colores);
            TamanoHueco(grafica.ChartXml, 58);
            QuitarTituloAutomatico(grafica.ChartXml);
            return Ubicar(grafica, fila, col, ancho, alto);
        }

        internal static void Rellenar(ExcelChartSerie serie, Color color)
        {
            serie.Fill.Style = eFillStyle.SolidFill;
            serie.Fill.Color = color;
            serie.Border.Fill.Style = eFillStyle.NoFill;
        }

        // Estilo común: sin borde, ejes en gris, rejilla muy clara, letra chica, como el PDF.
        internal static void EstiloGrafica(ExcelChart grafica, bool leyenda)
        {
            grafica.Border.Fill.Style = eFillStyle.NoFill;
            if (leyenda)
            {
                grafica.Legend.Position = eLegendPosition.Top;
                grafica.Legend.Font.Size = 8;
                grafica.Legend.Font.Color = Muted;
            }
            else
            {
                grafica.Legend.Remove();
            }
            foreach (var eje in new[] { grafica.XAxis, grafica.YAxis })
            {
                eje.Font.Size = 8;
                eje.Font.Color = Muted;
                eje.MajorTickMark = eAxisTickMark.None;
                eje.MinorTickMark = eAxisTickMark.None;
            }
            // Solo el eje de categorías lleva línea; el de valores se ve por la rejilla. (Poner
            // color y luego NoFill deja los dos rellenos en el XML y Excel rechaza el archivo.)
            grafica.XAxis.Border.Fill.Style = eFillStyle.SolidFill;
            grafica.XAxis.Border.Fill.Color = Linea;
            grafica.YAxis.MajorGridlines.Fill.Style = eFillStyle.SolidFill;
            grafica.YAxis.MajorGridlines.Fill.Color = Rejilla;
            grafica.YAxis.Border.Fill.Style = eFillStyle.NoFill;
            QuitarTituloAutomatico(grafica.ChartXml);
        }

        // EPPlus 4.5 agrega el <c:spPr> de una serie al FINAL, después de <c:cat>/<c:val>, cuando
        // el relleno se pone después de crearla; Excel valida el orden y declara el archivo dañado.
        // Aquí se reacomodan los hijos de cada serie en el orden del esquema (CT_BarSer, CT_LineSer
        // y CT_PieSer comparten esta secuencia).
        internal static readonly string[] OrdenSerie =
        {
            "idx", "order", "tx", "spPr", "invertIfNegative", "pictureOptions", "marker", "explosion",
            "dPt", "dLbls", "trendline", "errBars", "cat", "val", "smooth", "shape", "extLst"
        };

        internal static void OrdenarSeries(XmlDocument xml)
        {
            foreach (XmlNode serie in xml.GetElementsByTagName("ser", NsC))
            {
                var hijos = serie.ChildNodes.OfType<XmlElement>().ToList();
                var ordenados = hijos
                    .Select((h, i) => new { Nodo = h, Pos = Array.IndexOf(OrdenSerie, h.LocalName), I = i })
                    .OrderBy(x => x.Pos < 0 ? int.MaxValue : x.Pos).ThenBy(x => x.I)
                    .Select(x => x.Nodo).ToList();
                foreach (var h in hijos) serie.RemoveChild(h);
                foreach (var h in ordenados) serie.AppendChild(h);
            }
        }

        // Excel pone solo un título a las gráficas de una serie si no se le dice lo contrario; los
        // títulos ya van en la sección de la hoja.
        internal const string NsC = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        internal const string NsA = "http://schemas.openxmlformats.org/drawingml/2006/main";

        internal static void QuitarTituloAutomatico(XmlDocument xml)
        {
            var ns = new XmlNamespaceManager(xml.NameTable);
            ns.AddNamespace("c", NsC);
            var chart = xml.SelectSingleNode("//c:chart", ns);
            if (chart == null) return;
            var existente = chart.SelectSingleNode("c:autoTitleDeleted", ns) as XmlElement;
            if (existente == null)
            {
                existente = xml.CreateElement("c", "autoTitleDeleted", NsC);
                var titulo = chart.SelectSingleNode("c:title", ns);
                if (titulo != null) chart.InsertAfter(existente, titulo);
                else chart.PrependChild(existente);
            }
            existente.SetAttribute("val", "1");
        }

        // Un color por rebanada de la dona (EPPlus 4.5 no expone los puntos): se agregan los
        // <c:dPt> a mano, antes de <c:dLbls>/<c:cat> como pide el esquema.
        internal static void ColorearPuntos(XmlDocument xml, List<Color> colores)
        {
            var ns = new XmlNamespaceManager(xml.NameTable);
            ns.AddNamespace("c", NsC);
            var serie = xml.SelectSingleNode("//c:ser", ns);
            if (serie == null) return;
            var ancla = serie.SelectSingleNode("c:dLbls", ns) ?? serie.SelectSingleNode("c:cat", ns);
            for (int i = 0; i < colores.Count; i++)
            {
                var dPt = xml.CreateElement("c", "dPt", NsC);
                var idx = xml.CreateElement("c", "idx", NsC); idx.SetAttribute("val", i.ToString()); dPt.AppendChild(idx);
                var b3 = xml.CreateElement("c", "bubble3D", NsC); b3.SetAttribute("val", "0"); dPt.AppendChild(b3);
                var spPr = xml.CreateElement("c", "spPr", NsC);
                spPr.AppendChild(Relleno(xml, colores[i]));
                var ln = xml.CreateElement("a", "ln", NsA); ln.SetAttribute("w", "19050");
                ln.AppendChild(Relleno(xml, Color.White));
                spPr.AppendChild(ln);
                dPt.AppendChild(spPr);
                serie.InsertBefore(dPt, ancla);
            }
        }

        // HoleSize es de solo lectura en EPPlus 4.5: se escribe directo en <c:holeSize>.
        internal static void TamanoHueco(XmlDocument xml, int porcentaje)
        {
            var ns = new XmlNamespaceManager(xml.NameTable);
            ns.AddNamespace("c", NsC);
            var nodo = xml.SelectSingleNode("//c:doughnutChart/c:holeSize", ns) as XmlElement;
            if (nodo != null) nodo.SetAttribute("val", porcentaje.ToString());
        }

        internal static XmlElement Relleno(XmlDocument xml, Color color)
        {
            var solid = xml.CreateElement("a", "solidFill", NsA);
            var rgb = xml.CreateElement("a", "srgbClr", NsA);
            rgb.SetAttribute("val", (color.ToArgb() & 0xFFFFFF).ToString("X6"));
            solid.AppendChild(rgb);
            return solid;
        }

        internal static int Ubicar(ExcelChart grafica, int fila, int col, int ancho, int alto)
        {
            OrdenarSeries(grafica.ChartXml);
            grafica.SetPosition(fila - 1, 0, col - 1, 0);
            grafica.SetSize(ancho, alto);
            return fila + (int)Math.Ceiling(alto / (double)PxRenglon) + 1;
        }

        // Hoja oculta con los números de las gráficas: un bloque de columnas por gráfica.
        internal class HojaDatos
        {
            public readonly ExcelWorksheet Hoja;
            private int _columna = 1;

            public HojaDatos(ExcelWorksheet hoja) { Hoja = hoja; }

            public Bloque Agregar(string nombre, string[] encabezados, List<object[]> filas, string formato = null)
            {
                int c0 = _columna;
                Hoja.Cells[1, c0].Value = nombre;
                for (int c = 0; c < encabezados.Length; c++) Hoja.Cells[2, c0 + c].Value = encabezados[c];
                for (int f = 0; f < filas.Count; f++)
                {
                    for (int c = 0; c < filas[f].Length; c++)
                    {
                        Hoja.Cells[3 + f, c0 + c].Value = filas[f][c];
                        if (formato != null && c > 0) Hoja.Cells[3 + f, c0 + c].Style.Numberformat.Format = formato;
                    }
                }
                _columna += encabezados.Length + 1;
                return new Bloque(Hoja, c0, filas.Count, encabezados);
            }
        }

        internal class Bloque
        {
            private readonly ExcelWorksheet _hoja;
            private readonly int _col0, _renglones;
            private readonly string[] _encabezados;

            public Bloque(ExcelWorksheet hoja, int col0, int renglones, string[] encabezados)
            {
                _hoja = hoja; _col0 = col0; _renglones = renglones; _encabezados = encabezados;
            }

            public string Columna(int k)
            {
                return _hoja.Cells[3, _col0 + k, 2 + Math.Max(_renglones, 1), _col0 + k].FullAddressAbsolute;
            }

            public ExcelAddressBase Encabezado(int k)
            {
                return _hoja.Cells[2, _col0 + k];
            }

            public string NombreColumna(int k)
            {
                return _encabezados[k];
            }
        }

        internal static void PintarPastel(ExcelRange celda, bool bien)
        {
            celda.Style.Fill.PatternType = ExcelFillStyle.Solid;
            celda.Style.Fill.BackgroundColor.SetColor(bien ? PastelBienFondo : PastelMalFondo);
            celda.Style.Font.Color.SetColor(bien ? PastelBienTexto : PastelMalTexto);
            celda.Style.Font.Bold = true;
            celda.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        }

        internal static void AgregarBarra(ExcelWorksheet hoja, ExcelAddress rango)
        {
            var barra = hoja.ConditionalFormatting.AddDatabar(rango, Marca);
            // Base 0 explícita: en automático la barra arranca desde el valor MÍNIMO y exagera la
            // diferencia real. Con 0 la longitud sí es proporcional al valor.
            barra.LowValue.Type = eExcelConditionalFormattingValueObjectType.Num;
            barra.LowValue.Value = 0;
        }

        internal static void AgregarSemaforo(ExcelWorksheet hoja, ExcelAddress rango)
        {
            var escala = hoja.ConditionalFormatting.AddThreeColorScale(rango);
            escala.LowValue.Color = EscalaBaja;
            escala.MiddleValue.Color = EscalaMedia;
            escala.HighValue.Color = EscalaAlta;
        }

        // ==================================================================
        // "#,##0.##" deja un punto colgando en los enteros (224.), así que se elige el formato
        // según el valor.
        internal static string FormatoCantidad(decimal valor)
        {
            return valor == Math.Truncate(valor) ? "#,##0" : "#,##0.##";
        }

        internal static Tono TonoHoras(double horas, double aviso, double critico)
        {
            // Lento y no Crit: tardar mucho en una etapa se ve en azul oscuro, el rojo es para el riesgo.
            return horas > critico ? Tono.Lento : (horas > aviso ? Tono.Warn : Tono.Ok);
        }

        internal static Color ColorTono(Tono tono)
        {
            return tono == Tono.Crit ? Crit : (tono == Tono.Lento ? Lento : (tono == Tono.Warn ? Warn : Ok));
        }

        internal static string EtiquetaCorte(DateTime inicio, string corte)
        {
            if (corte == "mes") return inicio.ToString("MMM yyyy");
            return inicio.ToString("d MMM");
        }

        internal static decimal Fraccion(int parte, int total)
        {
            return total > 0 ? Math.Round(parte / (decimal)total, 3) : 0m;
        }

        internal static int Pct(int parte, int total)
        {
            return total > 0 ? (int)Math.Round(parte * 100.0 / total) : 0;
        }
    }
}
