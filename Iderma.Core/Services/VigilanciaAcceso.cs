using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Google.Cloud.Firestore;

namespace Iderma.Core.Services;

public static class VigilanciaAcceso
{
    public static Action? AlEntrarSinRed { get; set; }

    public static void Comprobar() => AlEntrarSinRed?.Invoke();

    public static async Task RegistrarAsync(string motivo, string? ssid)
    {
        var db = await CuentasNube.AbrirAsync();
        if (db is null)
        {
            return;
        }

        await db.Collection("intrusiones").AddAsync(new Dictionary<string, object>
        {
            ["at"] = Timestamp.GetCurrentTimestamp(),
            ["motivo"] = motivo,
            ["equipo"] = Environment.MachineName,
            ["usuarioWindows"] = Environment.UserName,
            ["sistema"] = RuntimeInformation.OSDescription,
            ["version"] = AppInfo.Version,
            ["ssid"] = ssid ?? string.Empty,
            ["ips"] = IpsLocales(),
            ["ipPublica"] = await IpPublicaAsync()
        });
    }

    private static List<string> IpsLocales()
    {
        var ips = new List<string>();
        foreach (var adaptador in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adaptador.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (var direccion in adaptador.GetIPProperties().UnicastAddresses)
            {
                var ip = direccion.Address;
                if (IPAddress.IsLoopback(ip))
                {
                    continue;
                }

                if (ip.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
                {
                    continue;
                }

                var texto = ip.ToString();
                if (!ips.Contains(texto, StringComparer.OrdinalIgnoreCase))
                {
                    ips.Add(texto);
                }
            }
        }

        return ips;
    }

    private static async Task<string> IpPublicaAsync()
    {
        try
        {
            using var cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            var texto = await cliente.GetStringAsync("https://api.ipify.org");
            return texto.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
