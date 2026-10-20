using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace IdermaClinica;

public static class InventarioColores
{
    private static readonly SolidColorBrush Normal = new(Color.FromArgb(0, 0, 0, 0));

    public static SolidColorBrush De(int cantidad)
    {
        if (cantidad <= 0)
        {
            return Oscuro() ? Crear("#543C40") : Crear("#E8D8DA");
        }

        if (cantidad < 5)
        {
            return Oscuro() ? Crear("#524C36") : Crear("#E8E2CF");
        }

        return Normal;
    }

    private static bool Oscuro()
    {
        if (App.MainAppWindow?.Content is FrameworkElement raiz)
        {
            return raiz.ActualTheme == ElementTheme.Dark;
        }

        return Application.Current?.RequestedTheme != ApplicationTheme.Light;
    }

    private static SolidColorBrush Crear(string hex)
    {
        hex = hex.TrimStart('#');
        var r = Convert.ToByte(hex[..2], 16);
        var g = Convert.ToByte(hex[2..4], 16);
        var b = Convert.ToByte(hex[4..6], 16);
        return new SolidColorBrush(Color.FromArgb(255, r, g, b));
    }
}
