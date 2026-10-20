using IdermaClinica.Models;
using IdermaClinica.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IdermaClinica.Pages;

public sealed partial class HistorialPage : Page
{
    private readonly PedidoService _pedido = PedidoService.Instance;

    public HistorialPage()
    {
        InitializeComponent();
        ListaHistorial.ItemsSource = _pedido.Historial;
        _pedido.PedidoActualizado += Pedido_Actualizado;
        Loaded += HistorialPage_Loaded;
        Unloaded += HistorialPage_Unloaded;
        ActualizarVacio();
    }

    private void HistorialPage_Loaded(object sender, RoutedEventArgs e)
    {
        ListaHistorial.ItemsSource = null;
        ListaHistorial.ItemsSource = _pedido.Historial;
        ActualizarVacio();
    }

    private void HistorialPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _pedido.PedidoActualizado -= Pedido_Actualizado;
    }

    private void Pedido_Actualizado(object? sender, EventArgs e)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => Pedido_Actualizado(sender, e));
            return;
        }

        ListaHistorial.ItemsSource = null;
        ListaHistorial.ItemsSource = _pedido.Historial;
        ActualizarVacio();
    }

    private async void Repetir_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button boton || boton.DataContext is not PedidoHistorial pedido)
        {
            return;
        }

        if (_pedido.Items.Count > 0)
        {
            var dialogo = new ContentDialog
            {
                Title = "Repetir pedido",
                Content = "Se reemplazará la lista actual con este pedido. Si falta inventario, se pedirá solo lo disponible.",
                PrimaryButtonText = "Repetir",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            if (await dialogo.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        var resultado = _pedido.Repetir(pedido);
        _pedido.AvisoPendiente = resultado;
        App.MainAppWindow?.IrAPedidos();
    }

    private void Anclar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button boton || boton.DataContext is not PedidoHistorial pedido)
        {
            return;
        }

        var resultado = _pedido.Anclar(pedido);
        Mostrar(resultado);
    }

    private async void Atender_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button boton || boton.DataContext is not PedidoHistorial pedido)
        {
            return;
        }

        await _pedido.MarcarEstadoAsync(pedido, EstadoPedido.Atendido);
        MostrarAviso($"El pedido {pedido.Folio} quedó como Atendido.", InfoBarSeverity.Success);
    }

    private void ActualizarVacio()
    {
        var vacio = _pedido.Historial.Count == 0;
        EstadoVacio.Visibility = vacio ? Visibility.Visible : Visibility.Collapsed;
        ListaHistorial.Visibility = vacio ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Mostrar(ResultadoPedido resultado)
    {
        var severidad = resultado.Exito
            ? (resultado.EsAdvertencia ? InfoBarSeverity.Warning : InfoBarSeverity.Success)
            : InfoBarSeverity.Warning;
        MostrarAviso(resultado.Mensaje, severidad);
    }

    private void MostrarAviso(string mensaje, InfoBarSeverity severidad)
    {
        AvisoHistorial.Title = severidad == InfoBarSeverity.Success ? "Listo" : "Inventario";
        AvisoHistorial.Message = mensaje;
        AvisoHistorial.Severity = severidad;
        AvisoHistorial.IsOpen = true;
    }
}
