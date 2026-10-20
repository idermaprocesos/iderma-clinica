using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using IdermaClinica.Models;

namespace IdermaClinica.Services;

public static class ExcelImportService
{
    public static IReadOnlyList<Producto> Importar(string ruta)
    {
        using var libro = new XLWorkbook(ruta);
        var hoja = libro.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("El archivo de Excel no contiene hojas.");

        var primera = hoja.FirstRowUsed()
            ?? throw new InvalidOperationException("El archivo de Excel está vacío.");
        var ultima = hoja.LastRowUsed()
            ?? throw new InvalidOperationException("El archivo de Excel está vacío.");

        var encabezados = new Dictionary<string, int>();
        var ultimaColumna = hoja.LastColumnUsed()?.ColumnNumber() ?? 4;
        for (var columna = 1; columna <= ultimaColumna; columna++)
        {
            var texto = Normalizar(TextoTalCual(primera.Cell(columna)));
            if (!string.IsNullOrWhiteSpace(texto) && !encabezados.ContainsKey(texto))
            {
                encabezados[texto] = columna;
            }
        }

        var colCodigo = BuscarColumna(encabezados, "codigo");
        var colArea = BuscarColumna(encabezados, "area");
        var colProducto = BuscarColumna(encabezados, "producto");
        var colCantidad = BuscarColumna(encabezados, "cantidad");

        var tieneEncabezados = colCodigo > 0 || colArea > 0 || colProducto > 0 || colCantidad > 0;
        if (!tieneEncabezados)
        {
            colCodigo = 1;
            colArea = 2;
            colProducto = 3;
            colCantidad = 4;
        }
        else
        {
            if (colCodigo < 1) colCodigo = 1;
            if (colArea < 1) colArea = 2;
            if (colProducto < 1) colProducto = 3;
            if (colCantidad < 1) colCantidad = 4;
        }

        var filaInicio = tieneEncabezados ? primera.RowNumber() + 1 : primera.RowNumber();
        var productos = new List<Producto>();

        for (var fila = filaInicio; fila <= ultima.RowNumber(); fila++)
        {
            var codigo = TextoTalCual(hoja.Cell(fila, colCodigo));
            var area = TextoTalCual(hoja.Cell(fila, colArea));
            var producto = TextoTalCual(hoja.Cell(fila, colProducto));
            var cantidad = EnteroTalCual(hoja.Cell(fila, colCantidad));

            if (string.IsNullOrWhiteSpace(codigo) &&
                string.IsNullOrWhiteSpace(area) &&
                string.IsNullOrWhiteSpace(producto))
            {
                continue;
            }

            productos.Add(new Producto
            {
                Codigo = codigo,
                Area = area,
                Nombre = producto,
                Cantidad = cantidad
            });
        }

        if (productos.Count == 0)
        {
            throw new InvalidOperationException(
                "No se encontraron filas con datos. El Excel debe tener las columnas CÓDIGO, ÁREA, PRODUCTO y CANTIDAD.");
        }

        return productos;
    }

    public static void GuardarPlantilla(string ruta)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("Catalogo");

        hoja.Cell(1, 1).Value = "CÓDIGO";
        hoja.Cell(1, 2).Value = "ÁREA";
        hoja.Cell(1, 3).Value = "PRODUCTO";
        hoja.Cell(1, 4).Value = "CANTIDAD";

        var ejemplos = new (string Codigo, string Area, string Producto, int Cantidad)[]
        {
            ("MIN-050", "Tratamiento", "Minoxidil 5% 60 ml", 12),
            ("SHA-ANT", "Cuidado capilar", "Shampoo anticaída 250 ml", 8),
            ("SER-BIO", "Tratamiento", "Suero biotina y zinc", 6),
            ("AMP-PRP", "Procedimiento", "Ampolletas regeneradoras x10", 4),
            ("LOC-FNS", "Tratamiento", "Loción fortalecedora nocturna", 10)
        };

        for (var i = 0; i < ejemplos.Length; i++)
        {
            var fila = i + 2;
            hoja.Cell(fila, 1).Value = ejemplos[i].Codigo;
            hoja.Cell(fila, 2).Value = ejemplos[i].Area;
            hoja.Cell(fila, 3).Value = ejemplos[i].Producto;
            hoja.Cell(fila, 4).Value = ejemplos[i].Cantidad;
        }

        var encabezado = hoja.Range(1, 1, 1, 4);
        encabezado.Style.Font.Bold = true;
        encabezado.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F766E");
        encabezado.Style.Font.FontColor = XLColor.White;
        hoja.Columns().AdjustToContents();
        hoja.SheetView.FreezeRows(1);

        libro.SaveAs(ruta);
    }

    private static int BuscarColumna(Dictionary<string, int> encabezados, params string[] nombres)
    {
        foreach (var nombre in nombres)
        {
            if (encabezados.TryGetValue(nombre, out var columna))
            {
                return columna;
            }
        }

        return -1;
    }

    private static string TextoTalCual(IXLCell celda)
    {
        if (celda.IsEmpty())
        {
            return string.Empty;
        }

        if (celda.DataType == XLDataType.Text)
        {
            return celda.GetString().Trim();
        }

        return celda.GetFormattedString().Trim();
    }

    private static int EnteroTalCual(IXLCell celda)
    {
        if (celda.IsEmpty())
        {
            return 0;
        }

        if (celda.TryGetValue(out double numero) && !double.IsNaN(numero) && !double.IsInfinity(numero))
        {
            return Convert.ToInt32(Math.Round(numero, MidpointRounding.AwayFromZero));
        }

        var texto = TextoTalCual(celda);
        if (int.TryParse(texto, NumberStyles.Integer, CultureInfo.GetCultureInfo("es-MX"), out var enteroMx))
        {
            return enteroMx;
        }

        if (int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var enteroInv))
        {
            return enteroInv;
        }

        if (decimal.TryParse(texto, NumberStyles.Any, CultureInfo.GetCultureInfo("es-MX"), out var decimalMx))
        {
            return Convert.ToInt32(Math.Round(decimalMx, MidpointRounding.AwayFromZero));
        }

        return 0;
    }

    private static string Normalizar(string valor)
    {
        var form = valor.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(form.Length);
        foreach (var c in form)
        {
            var categoria = CharUnicodeInfo.GetUnicodeCategory(c);
            if (categoria != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
