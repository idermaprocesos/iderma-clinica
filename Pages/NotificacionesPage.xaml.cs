using IdermaClinica.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IdermaClinica.Pages;

public sealed partial class NotificacionesPage : Page
{
    private readonly NotificacionesLocales _avisos = NotificacionesLocales.Instance;

    public NotificacionesPage()
    {
        InitializeComponent();
        ListaNotificaciones.ItemsSource = _avisos.Items;
        _avisos.Cambiado += OnCambiado;
        Loaded += (_, _) => Refrescar();
        Unloaded += (_, _) => _avisos.Cambiado -= OnCambiado;
    }

    private void OnCambiado(object? sender, EventArgs e) => Refrescar();

    private void Refrescar()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(Refrescar);
            return;
        }

        ListaNotificaciones.ItemsSource = null;
        ListaNotificaciones.ItemsSource = _avisos.Items;
        var total = _avisos.Items.Count;
        var sinLeer = _avisos.Items.Count(n => !n.Leida);
        TextoVacio.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResumenTexto.Text = total == 0
            ? "Respuestas de logística guardadas en este equipo."
            : sinLeer == 0
                ? $"{total} notificaciones en este equipo."
                : $"{total} notificaciones · {sinLeer} sin leer.";
    }

    private async void MarcarLeidas_Click(object sender, RoutedEventArgs e)
    {
        await _avisos.MarcarTodasLeidasAsync();
        Aviso.Title = "Notificaciones";
        Aviso.Message = "Se marcaron como leídas.";
        Aviso.Severity = InfoBarSeverity.Success;
        Aviso.IsOpen = true;
        Refrescar();
    }
}
