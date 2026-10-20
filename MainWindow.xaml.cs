using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using IdermaClinica.Pages;
using IdermaClinica.Services;
using Windows.Graphics;

namespace IdermaClinica;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Iderma.Shell.BotonLauncher.Colocar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("img/logo-clinica.ico");
        AppWindow.Resize(new SizeInt32(1400, 900));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        NotificacionesLocales.Instance.Cambiado += (_, _) =>
            DispatcherQueue.TryEnqueue(ActualizarBadgeNotificaciones);
        ActualizarBadgeNotificaciones();
    }

    public void IrAHistorial() => Seleccionar("historial");

    public void IrANotificaciones() => Seleccionar("notificaciones");

    public void IrAPedidos() => Seleccionar("pedidos");

    private void Seleccionar(string tag)
    {
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag as string == tag)
            {
                NavView.SelectedItem = item;
                break;
            }
        }
    }

    private void ActualizarBadgeNotificaciones()
    {
        var sinLeer = NotificacionesLocales.Instance.Items.Count(n => !n.Leida);
        ItemNotificaciones.InfoBadge = sinLeer > 0
            ? new InfoBadge { Value = sinLeer }
            : null;
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (NavFrame.CanGoBack)
        {
            NavFrame.GoBack();
        }
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        if (NavFrame.Content is null)
        {
            NavFrame.Navigate(typeof(PedidosPage));
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "pedidos":
                    NavFrame.Navigate(typeof(PedidosPage));
                    break;
                case "historial":
                    NavFrame.Navigate(typeof(HistorialPage));
                    break;
                case "notificaciones":
                    NavFrame.Navigate(typeof(NotificacionesPage));
                    break;
                case "config":
                    NavFrame.Navigate(typeof(SettingsPage));
                    break;
                case "about":
                    NavFrame.Navigate(typeof(AboutPage));
                    break;
            }
        }
    }
}
