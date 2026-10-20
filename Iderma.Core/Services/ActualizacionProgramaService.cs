using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Iderma.Core.Services;

public sealed record ReleasePrograma(
    Version Version,
    string Etiqueta,
    string UrlNotas,
    string NombreArchivo,
    string UrlDescarga,
    long TamanoBytes);

/// <summary>
/// Actualización única de todo Iderma (launcher y áreas) contra GitHub Releases.
/// La versión instalada es la de <see cref="AppInfo"/>, para que las áreas no queden en versiones distintas.
/// </summary>
public static class ActualizacionProgramaService
{
    public const string Repositorio = "idermaprocesos/idermalogistica";
    private const string ApiLatest = "https://api.github.com/repos/idermaprocesos/idermalogistica/releases/latest";
    private const string ApiLista = "https://api.github.com/repos/idermaprocesos/idermalogistica/releases?per_page=15";

    private static readonly string[] ProcesosDeArea =
    [
        "IdermaCapilarApp",
        "IdermaClinica",
        "IdermaCapilar.Historial",
        "MantenimientoCapilar",
        "BitacoraGrabacion",
        "Consola",
        "Iderma.UserAdmin"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HttpClient Http = CrearCliente();

    public static Version VersionInstalada =>
        Version.TryParse(AppInfo.Version, out var version)
            ? Normalizar(version)
            : new Version(2, 0, 0, 0);

    public static string TextoVersionInstalada
    {
        get
        {
            var version = VersionInstalada;
            return $"V{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        }
    }

    public static async Task<ReleasePrograma> ConsultarAsync(CancellationToken cancelar = default)
    {
        var json = await DescargarTextoAsync(ApiLatest, cancelar);
        ReleaseGithub? release = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            release = JsonSerializer.Deserialize<ReleaseGithub>(json, JsonOptions);
        }

        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
        {
            json = await DescargarTextoAsync(ApiLista, cancelar);
            var lista = string.IsNullOrWhiteSpace(json)
                ? []
                : JsonSerializer.Deserialize<List<ReleaseGithub>>(json, JsonOptions) ?? [];
            release = lista.FirstOrDefault(r => !r.Draft && !r.Prerelease)
                      ?? lista.FirstOrDefault();
        }

        if (release is null)
        {
            throw new InvalidOperationException(
                "No hay versiones publicadas todavía en GitHub Releases.");
        }

        return Mapear(release);
    }

    public static bool HayActualizacion(ReleasePrograma release) =>
        Normalizar(release.Version) > VersionInstalada;

    public static async Task<string> DescargarInstaladorAsync(
        ReleasePrograma release,
        IProgress<(double Porcentaje, string Mensaje)>? progreso,
        CancellationToken cancelar = default)
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "Iderma-update");
        Directory.CreateDirectory(carpeta);
        var destino = Path.Combine(carpeta, SanitizarNombre(release.NombreArchivo));

        progreso?.Report((5, "Descargando el instalador de Iderma desde GitHub…"));
        using var respuesta = await Http.GetAsync(release.UrlDescarga, HttpCompletionOption.ResponseHeadersRead, cancelar);
        respuesta.EnsureSuccessStatusCode();

        var total = respuesta.Content.Headers.ContentLength ?? release.TamanoBytes;
        await using var origen = await respuesta.Content.ReadAsStreamAsync(cancelar);
        await using var archivo = File.Create(destino);
        var buffer = new byte[81_920];
        long leidos = 0;
        int n;
        while ((n = await origen.ReadAsync(buffer, cancelar)) > 0)
        {
            await archivo.WriteAsync(buffer.AsMemory(0, n), cancelar);
            leidos += n;
            if (total > 0)
            {
                var pct = Math.Clamp(10 + 80.0 * leidos / total, 10, 90);
                progreso?.Report((pct, $"Descargando… {leidos / 1_048_576d:0.0} / {total / 1_048_576d:0.0} MB"));
            }
        }

        progreso?.Report((92, "Instalador listo."));
        return destino;
    }

    public static void GuardarInstaladorPendiente(string ruta, ReleasePrograma release)
    {
        var pendiente = new InstaladorPendiente
        {
            Ruta = ruta,
            Etiqueta = release.Etiqueta,
            Version = release.Version.ToString()
        };
        Directory.CreateDirectory(Path.GetDirectoryName(RutaInstaladorPendiente())!);
        File.WriteAllText(RutaInstaladorPendiente(), JsonSerializer.Serialize(pendiente, JsonOptions));
    }

    public static InstaladorPendiente? LeerInstaladorPendiente()
    {
        var meta = RutaInstaladorPendiente();
        if (!File.Exists(meta))
        {
            return null;
        }

        try
        {
            var pendiente = JsonSerializer.Deserialize<InstaladorPendiente>(File.ReadAllText(meta), JsonOptions);
            if (pendiente is null || string.IsNullOrWhiteSpace(pendiente.Ruta) || !File.Exists(pendiente.Ruta))
            {
                File.Delete(meta);
                return null;
            }

            if (!Version.TryParse(pendiente.Version, out var version) || Normalizar(version) <= VersionInstalada)
            {
                return null;
            }

            return pendiente;
        }
        catch
        {
            try
            {
                File.Delete(meta);
            }
            catch
            {
            }

            return null;
        }
    }

    public static void LanzarInstalador(string rutaInstalador)
    {
        CerrarAreasAbiertas();
        Process.Start(new ProcessStartInfo
        {
            FileName = rutaInstalador,
            UseShellExecute = true
        });
    }

    public static string Tamano(ReleasePrograma release) =>
        release.TamanoBytes > 0 ? $" ({release.TamanoBytes / 1_048_576d:0.0} MB)" : string.Empty;

    private static void CerrarAreasAbiertas()
    {
        var actual = Process.GetCurrentProcess().Id;
        foreach (var nombre in ProcesosDeArea)
        {
            foreach (var proceso in Process.GetProcessesByName(nombre))
            {
                if (proceso.Id == actual)
                {
                    continue;
                }

                try
                {
                    proceso.Kill(entireProcessTree: true);
                }
                catch
                {
                }
            }
        }
    }

    private static async Task<string?> DescargarTextoAsync(string url, CancellationToken cancelar)
    {
        using var respuesta = await Http.GetAsync(url, cancelar);
        if (respuesta.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if ((int)respuesta.StatusCode == 403 || (int)respuesta.StatusCode == 429)
        {
            throw new InvalidOperationException(
                "GitHub limitó las consultas. Espere un minuto e inténtelo de nuevo.");
        }

        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadAsStringAsync(cancelar);
    }

    private static ReleasePrograma Mapear(ReleaseGithub release)
    {
        var version = ParsearVersion(release.TagName);
        var asset = ElegirInstalador(release.Assets);
        if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
        {
            throw new InvalidOperationException(
                $"La versión {release.TagName} no incluye un instalador (.exe) en GitHub Releases.");
        }

        return new ReleasePrograma(
            version,
            release.TagName,
            release.HtmlUrl ?? $"https://github.com/{Repositorio}/releases",
            asset.Name ?? "Iderma-setup.exe",
            asset.BrowserDownloadUrl,
            asset.Size);
    }

    private static AssetGithub? ElegirInstalador(IReadOnlyList<AssetGithub>? assets)
    {
        if (assets is null || assets.Count == 0)
        {
            return null;
        }

        bool EsExe(AssetGithub asset) =>
            Nombre(asset).EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        bool EsRedist(AssetGithub asset)
        {
            var nombre = Nombre(asset);
            return nombre.Contains("redist", StringComparison.OrdinalIgnoreCase)
                   || nombre.Contains("webview", StringComparison.OrdinalIgnoreCase);
        }

        return assets.FirstOrDefault(a => EsExe(a) && !EsRedist(a)
                                          && (Nombre(a).Contains("Launcher", StringComparison.OrdinalIgnoreCase)
                                              || Nombre(a).Contains("Iderma-V", StringComparison.OrdinalIgnoreCase)
                                              || Nombre(a).Contains("Setup", StringComparison.OrdinalIgnoreCase)))
               ?? assets.FirstOrDefault(a => EsExe(a)
                                             && Nombre(a).Contains("IdermaCapilarApp-V", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(a => EsExe(a)
                                             && Nombre(a).Contains("instalador", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(a => EsExe(a) && !EsRedist(a));
    }

    private static string Nombre(AssetGithub asset) => asset.Name ?? string.Empty;

    private static Version ParsearVersion(string etiqueta)
    {
        var limpio = etiqueta.Trim();
        if (limpio.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            limpio = limpio[1..].Trim().TrimStart('.').Trim();
        }

        var coincidencia = Regex.Match(limpio, @"\d+(?:\.\d+){1,3}");
        if (!coincidencia.Success || !Version.TryParse(coincidencia.Value, out var version))
        {
            throw new InvalidOperationException($"No se reconoció la versión «{etiqueta}».");
        }

        return Normalizar(version);
    }

    private static Version Normalizar(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    private static string SanitizarNombre(string nombre)
    {
        var invalido = Path.GetInvalidFileNameChars();
        var limpio = new string(nombre.Where(c => !invalido.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(limpio) ? "Iderma-update.exe" : limpio;
    }

    private static string RutaInstaladorPendiente() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Iderma",
            "instalador-programa.json");

    private static HttpClient CrearCliente()
    {
        var cliente = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("IdermaLauncher/2.0.0");
        cliente.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        cliente.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return cliente;
    }

    public sealed class InstaladorPendiente
    {
        public string Ruta { get; set; } = string.Empty;
        public string Etiqueta { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }

    private sealed class ReleaseGithub
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<AssetGithub> Assets { get; set; } = [];
    }

    private sealed class AssetGithub
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
