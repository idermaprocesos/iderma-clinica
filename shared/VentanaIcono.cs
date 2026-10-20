using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Iderma.Shell;

/// <summary>
/// Aplica el .ico a la ventana y a la barra de tareas (WinUI unpackaged a veces ignora solo AppWindow.SetIcon).
/// </summary>
public static class VentanaIcono
{
    private const uint WmSetIcon = 0x0080;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x0010;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int GclpHicon = -14;
    private const int GclpHiconSm = -34;

    public static void Aplicar(Window ventana, params string[] candidatosRelativos)
    {
        ArgumentNullException.ThrowIfNull(ventana);
        var ruta = Resolver(candidatosRelativos);
        if (ruta is null)
        {
            return;
        }

        try
        {
            ventana.AppWindow.SetIcon(ruta);
        }
        catch
        {
        }

        var hwnd = WindowNative.GetWindowHandle(ventana);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var grande = LoadImage(IntPtr.Zero, ruta, ImageIcon, 256, 256, LoadFromFile);
        if (grande == IntPtr.Zero)
        {
            grande = LoadImage(IntPtr.Zero, ruta, ImageIcon, 32, 32, LoadFromFile);
        }

        var chico = LoadImage(IntPtr.Zero, ruta, ImageIcon, 16, 16, LoadFromFile);
        if (chico != IntPtr.Zero)
        {
            SendMessage(hwnd, WmSetIcon, (IntPtr)IconSmall, chico);
            SetClassIcon(hwnd, GclpHiconSm, chico);
        }

        if (grande != IntPtr.Zero)
        {
            SendMessage(hwnd, WmSetIcon, (IntPtr)IconBig, grande);
            SetClassIcon(hwnd, GclpHicon, grande);
        }
    }

    public static void AplicarAlActivar(Window ventana, params string[] candidatosRelativos)
    {
        ArgumentNullException.ThrowIfNull(ventana);
        void Handler(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                return;
            }

            ventana.Activated -= Handler;
            Aplicar(ventana, candidatosRelativos);
        }

        ventana.Activated += Handler;
        Aplicar(ventana, candidatosRelativos);
    }

    public static string? Resolver(params string[] candidatosRelativos)
    {
        foreach (var relativo in candidatosRelativos)
        {
            if (string.IsNullOrWhiteSpace(relativo))
            {
                continue;
            }

            if (Path.IsPathRooted(relativo) && File.Exists(relativo))
            {
                return relativo;
            }

            var juntoAlExe = Path.Combine(AppContext.BaseDirectory, relativo);
            if (File.Exists(juntoAlExe))
            {
                return Path.GetFullPath(juntoAlExe);
            }

            var cwd = Path.Combine(Environment.CurrentDirectory, relativo);
            if (File.Exists(cwd))
            {
                return Path.GetFullPath(cwd);
            }
        }

        return null;
    }

    private static void SetClassIcon(IntPtr hwnd, int index, IntPtr icon)
    {
        if (IntPtr.Size == 8)
        {
            SetClassLongPtr64(hwnd, index, icon);
            return;
        }

        SetClassLong32(hwnd, index, icon.ToInt32());
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLongW", CharSet = CharSet.Unicode)]
    private static extern int SetClassLong32(IntPtr hWnd, int nIndex, int dwNewLong);
}
