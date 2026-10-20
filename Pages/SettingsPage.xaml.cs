using IdermaClinica.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace IdermaClinica.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly CatalogoService _catalogo = CatalogoService.Instance;
    private readonly PedidoService _pedido = PedidoService.Instance;

    public SettingsPage()
    {
        InitializeComponent();
        ListaCatalogo.ItemsSource = _catalogo.Productos;
        _catalogo.CatalogoActualizado += (_, _) => ActualizarEstado();
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ActualizarEstado();
    }

    private async void ActualizarFirebase_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _catalogo.SincronizarFirebaseAsync();
            if (!string.IsNullOrWhiteSpace(_catalogo.ErrorNube))
            {
                MostrarError("No se pudo leer el inventario", _catalogo.ErrorNube);
                return;
            }

            AvisoImportacion.Severity = InfoBarSeverity.Success;
            AvisoImportacion.Title = "Inventario actualizado";
            AvisoImportacion.Message = $"Se leyeron {_catalogo.Productos.Count} productos de la colección de inventario.";
            AvisoImportacion.IsOpen = true;
        }
        catch (Exception ex)
        {
            MostrarError("No se pudo leer el inventario", ex.Message);
        }

        ActualizarEstado();
    }

    private async void ImportarExcel_Click(object sender, RoutedEventArgs e)
    {
        var selector = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };
        selector.FileTypeFilter.Add(".xlsx");
        WindowHelper.InicializarSelector(selector);

        var archivo = await selector.PickSingleFileAsync();
        if (archivo is null)
        {
            return;
        }

        if (_catalogo.TieneProductos)
        {
            var dialogo = new ContentDialog
            {
                Title = "Reemplazar inventario",
                Content = "El inventario actual (incluido el stock ya descontado por pedidos) se reemplazará por el Excel. ¿Continuar?",
                PrimaryButtonText = "Importar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialogo.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        try
        {
            await _catalogo.ImportarExcelAsync(archivo.Path, archivo.Name);
            AvisoImportacion.Severity = InfoBarSeverity.Success;
            AvisoImportacion.Title = "Inventario guardado";
            AvisoImportacion.Message = $"Se importaron {_catalogo.Productos.Count} productos desde {archivo.Name}. Quedan guardados en este equipo.";
            AvisoImportacion.IsOpen = true;
        }
        catch (Exception ex)
        {
            AvisoImportacion.Severity = InfoBarSeverity.Error;
            AvisoImportacion.Title = "No se pudo importar el Excel";
            AvisoImportacion.Message = ex.Message;
            AvisoImportacion.IsOpen = true;
        }

        ActualizarEstado();
    }

    private async void DescargarPlantilla_Click(object sender, RoutedEventArgs e)
    {
        var selector = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "Plantilla_catalogo_Iderma"
        };
        selector.FileTypeChoices.Add("Libro de Excel", [".xlsx"]);
        WindowHelper.InicializarSelector(selector);

        var archivo = await selector.PickSaveFileAsync();
        if (archivo is null)
        {
            return;
        }

        try
        {
            CachedFileManager.DeferUpdates(archivo);
            ExcelImportService.GuardarPlantilla(archivo.Path);
            await CachedFileManager.CompleteUpdatesAsync(archivo);

            AvisoImportacion.Severity = InfoBarSeverity.Success;
            AvisoImportacion.Title = "Plantilla lista";
            AvisoImportacion.Message = "Se guardó un Excel de ejemplo con CÓDIGO, ÁREA, PRODUCTO y CANTIDAD.";
            AvisoImportacion.IsOpen = true;
        }
        catch (Exception ex)
        {
            AvisoImportacion.Severity = InfoBarSeverity.Error;
            AvisoImportacion.Title = "No se pudo guardar la plantilla";
            AvisoImportacion.Message = ex.Message;
            AvisoImportacion.IsOpen = true;
        }
    }

    private void ActualizarEstado()
    {
        ResumenCatalogoTexto.Text = _catalogo.Resumen;
        var vacio = !_catalogo.TieneProductos;
        CatalogoVacioTexto.Visibility = vacio ? Visibility.Visible : Visibility.Collapsed;
        PanelCatalogo.Visibility = vacio ? Visibility.Collapsed : Visibility.Visible;
        ResumenHistorialTexto.Text = _pedido.Historial.Count == 0
            ? "Aún no hay pedidos en el historial."
            : $"{_pedido.Historial.Count} {(_pedido.Historial.Count == 1 ? "pedido" : "pedidos")} guardados en este equipo.";
    }

    private async void ExportarHistorial_Click(object sender, RoutedEventArgs e)
    {
        var selector = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"Historial_pedidos_Iderma_{DateTime.Now:yyyyMMdd}"
        };
        selector.FileTypeChoices.Add("Historial JSON", [".json"]);
        WindowHelper.InicializarSelector(selector);

        var archivo = await selector.PickSaveFileAsync();
        if (archivo is null)
        {
            return;
        }

        try
        {
            CachedFileManager.DeferUpdates(archivo);
            var resultado = await _pedido.ExportarHistorialAsync(archivo.Path);
            await CachedFileManager.CompleteUpdatesAsync(archivo);
            MostrarAviso(resultado);
        }
        catch (Exception ex)
        {
            MostrarError("No se pudo exportar el historial", ex.Message);
        }
    }

    private async void ImportarHistorial_Click(object sender, RoutedEventArgs e)
    {
        var selector = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };
        selector.FileTypeFilter.Add(".json");
        WindowHelper.InicializarSelector(selector);

        var archivo = await selector.PickSingleFileAsync();
        if (archivo is null)
        {
            return;
        }

        var dialogo = new ContentDialog
        {
            Title = "Importar historial",
            Content = "¿Reemplazar el historial actual o combinarlo con el archivo?",
            PrimaryButtonText = "Reemplazar",
            SecondaryButtonText = "Combinar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = XamlRoot
        };

        var decision = await dialogo.ShowAsync();
        if (decision is not ContentDialogResult.Primary and not ContentDialogResult.Secondary)
        {
            return;
        }

        try
        {
            var resultado = await _pedido.ImportarHistorialAsync(archivo.Path, reemplazar: decision == ContentDialogResult.Primary);
            MostrarAviso(resultado);
        }
        catch (Exception ex)
        {
            MostrarError("No se pudo importar el historial", ex.Message);
        }

        ActualizarEstado();
    }

    private void MostrarAviso(ResultadoPedido resultado)
    {
        AvisoImportacion.Severity = resultado.Exito
            ? (resultado.EsAdvertencia ? InfoBarSeverity.Warning : InfoBarSeverity.Success)
            : InfoBarSeverity.Error;
        AvisoImportacion.Title = resultado.Exito ? "Historial" : "No se pudo completar";
        AvisoImportacion.Message = resultado.Mensaje;
        AvisoImportacion.IsOpen = true;
        ActualizarEstado();
    }

    private void MostrarError(string titulo, string mensaje)
    {
        AvisoImportacion.Severity = InfoBarSeverity.Error;
        AvisoImportacion.Title = titulo;
        AvisoImportacion.Message = mensaje;
        AvisoImportacion.IsOpen = true;
    }
}
