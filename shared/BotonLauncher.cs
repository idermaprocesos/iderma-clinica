using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Iderma.Shell;

/// <summary>
/// Botón de la barra de título que vuelve a abrir el launcher de Iderma.
/// </summary>
public static class BotonLauncher
{
    private const int SwRestore = 9;
    private const int GwOwner = 4;

    public static void Colocar(TitleBar titleBar)
    {
        var boton = new Button
        {
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new FontIcon { Glyph = "\uE80F", FontSize = 14 },
                    new TextBlock
                    {
                        Text = "Launcher",
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };

        ToolTipService.SetToolTip(boton, "Volver al launcher");
        TitleBar.SetIsDragRegion(boton, false);
        boton.Click += async (_, _) =>
        {
            var error = Abrir();
            if (string.IsNullOrEmpty(error) || boton.XamlRoot is null)
            {
                return;
            }

            var dialogo = new ContentDialog
            {
                Title = "Launcher",
                Content = error,
                CloseButtonText = "Cerrar",
                XamlRoot = boton.XamlRoot
            };
            await dialogo.ShowAsync();
        };

        titleBar.RightHeader = boton;
    }

    public static string Abrir()
    {
        try
        {
            if (TraerAlFrente())
            {
                return string.Empty;
            }

            var raiz = BuscarRaizRepositorio();
            if (raiz is null)
            {
                return "No se encontró el launcher junto a esta aplicación.";
            }

            var carpeta = Path.Combine(raiz, "Iderma.Launcher");
            var exe = BuscarEjecutable(carpeta);
            if (exe is not null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = Path.GetDirectoryName(exe)!,
                    UseShellExecute = true
                });
                return string.Empty;
            }

            var proyecto = Path.Combine(carpeta, "Iderma.Launcher.csproj");
            if (!File.Exists(proyecto))
            {
                return "No hay un launcher compilado.";
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{proyecto}\"",
                WorkingDirectory = carpeta,
                UseShellExecute = true
            });
            return string.Empty;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static bool TraerAlFrente()
    {
        foreach (var proceso in Process.GetProcessesByName("Iderma.Launcher"))
        {
            try
            {
                var hwnd = proceso.MainWindowHandle;
                if (hwnd == IntPtr.Zero)
                {
                    hwnd = BuscarVentana(proceso.Id);
                }

                if (hwnd == IntPtr.Zero)
                {
                    continue;
                }

                Enfocar(hwnd);
                return true;
            }
            catch
            {
                // El proceso pudo cerrarse entre la consulta y el enfoque.
            }
        }

        return false;
    }

    private static void Enfocar(IntPtr hwnd)
    {
        var primerPlano = GetForegroundWindow();
        var hiloFrente = GetWindowThreadProcessId(primerPlano, out _);
        var hiloActual = GetCurrentThreadId();
        var unido = hiloFrente != 0 && hiloFrente != hiloActual && AttachThreadInput(hiloFrente, hiloActual, true);

        ShowWindow(hwnd, SwRestore);
        SetForegroundWindow(hwnd);

        if (unido)
        {
            AttachThreadInput(hiloFrente, hiloActual, false);
        }
    }

    private static IntPtr BuscarVentana(int idProceso)
    {
        IntPtr encontrada = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != (uint)idProceso || !IsWindowVisible(hwnd) || GetWindow(hwnd, GwOwner) != IntPtr.Zero)
            {
                return true;
            }

            encontrada = hwnd;
            return false;
        }, IntPtr.Zero);
        return encontrada;
    }

    private static string? BuscarRaizRepositorio()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "areas"))
                && Directory.Exists(Path.Combine(dir.FullName, "Iderma.Launcher")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static string? BuscarEjecutable(string carpetaLauncher)
    {
        var bin = Path.Combine(carpetaLauncher, "bin");
        if (!Directory.Exists(bin))
        {
            return null;
        }

        return Directory.EnumerateFiles(bin, "Iderma.Launcher.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
