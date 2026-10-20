using IdermaClinica.Services;
using Microsoft.UI.Xaml;

namespace IdermaClinica;

public partial class App : Application
{
    public static MainWindow? MainAppWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await CatalogoService.Instance.CargarAsync();
        await EnfermeraService.Instance.CargarAsync();
        await PedidoService.Instance.CargarAsync();
        await NotificacionesLocales.Instance.CargarAsync();
        MainAppWindow = new MainWindow();
        MainAppWindow.Activate();
        AvisoWindowsRespuesta.Preparar(MainAppWindow.DispatcherQueue);
        CatalogoService.Instance.IniciarEnVivo(MainAppWindow.DispatcherQueue);
        SolicitudesFirebase.IniciarEscucha(MainAppWindow.DispatcherQueue);
    }
}
