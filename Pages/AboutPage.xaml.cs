using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace IdermaClinica.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        Loaded += AboutPage_Loaded;
    }

    private async void AboutPage_Loaded(object sender, RoutedEventArgs e)
    {
        LogoClinica.Source = await CargarLogoClinicaAsync();
    }

    private static async Task<ImageSource?> CargarLogoClinicaAsync()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "img", "logo-clinica.ico");
        if (!File.Exists(ruta))
        {
            return null;
        }

        var bytes = ExtraerImagen(await File.ReadAllBytesAsync(ruta));
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var software = await decoder.GetSoftwareBitmapAsync();
        if (software.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || software.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
        {
            software = SoftwareBitmap.Convert(software, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }

        var fuente = new SoftwareBitmapSource();
        await fuente.SetBitmapAsync(software);
        return fuente;
    }

    private static byte[] ExtraerImagen(byte[] ico)
    {
        if (ico.Length > 24 && ico[0] == 0 && ico[1] == 0 && ico[2] == 1 && ico[22] == 0x89 && ico[23] == 0x50)
        {
            return ico[22..];
        }

        return ico;
    }

    public string NombreEquipo => Environment.MachineName;

    public string Usuario => $"{Environment.UserDomainName}\\{Environment.UserName}";

    public string Sistema => RuntimeInformation.OSDescription;

    public string MemoriaRam => EquipoInfo.MemoriaRam;

    public string Procesadores => $"{Environment.ProcessorCount} lógicos · {RuntimeInformation.OSArchitecture}";

    public string Version
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}

internal static class EquipoInfo
{
    public static string MemoriaRam
    {
        get
        {
            var estado = new MEMORYSTATUSEX
            {
                dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
            };

            if (!GlobalMemoryStatusEx(ref estado))
            {
                return "No disponible";
            }

            return $"{FormatearGb(estado.ullTotalPhys)} · {FormatearGb(estado.ullAvailPhys)} libres";
        }
    }

    private static string FormatearGb(ulong bytes)
    {
        var gb = bytes / (1024d * 1024d * 1024d);
        return $"{gb:0.#} GB";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
