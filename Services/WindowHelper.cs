using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace IdermaClinica.Services;

public static class WindowHelper
{
    public static void InicializarSelector(object selector)
    {
        var ventana = App.MainAppWindow ?? throw new InvalidOperationException("La ventana principal no está lista.");
        var hwnd = WindowNative.GetWindowHandle(ventana);
        InitializeWithWindow.Initialize(selector, hwnd);
    }
}
