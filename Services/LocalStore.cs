using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace IdermaClinica.Services;

public static class LocalStore
{
    private static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Carpeta { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IdermaCapilar");

    public static async Task MigrarSiHaceFaltaAsync(string archivo)
    {
        Directory.CreateDirectory(Carpeta);
        var destino = Path.Combine(Carpeta, archivo);
        if (File.Exists(destino))
        {
            return;
        }

        try
        {
            if (await ApplicationData.Current.LocalFolder.TryGetItemAsync(archivo) is StorageFile viejo)
            {
                File.Copy(viejo.Path, destino, overwrite: false);
            }
        }
        catch
        {
            // Si no hay archivo anterior, se crea al guardar.
        }
    }

    public static async Task<T?> LeerAsync<T>(string archivo) where T : class
    {
        await MigrarSiHaceFaltaAsync(archivo);
        var ruta = Path.Combine(Carpeta, archivo);
        if (!File.Exists(ruta))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(ruta);
        return JsonSerializer.Deserialize<T>(json, Opciones);
    }

    public static async Task GuardarAsync<T>(string archivo, T datos)
    {
        Directory.CreateDirectory(Carpeta);
        var ruta = Path.Combine(Carpeta, archivo);
        await GuardarEnRutaAsync(ruta, datos);
    }

    public static async Task<T?> LeerDesdeRutaAsync<T>(string ruta) where T : class
    {
        if (!File.Exists(ruta))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(ruta);
        return JsonSerializer.Deserialize<T>(json, Opciones);
    }

    public static async Task GuardarEnRutaAsync<T>(string ruta, T datos)
    {
        var carpeta = Path.GetDirectoryName(ruta);
        if (!string.IsNullOrWhiteSpace(carpeta))
        {
            Directory.CreateDirectory(carpeta);
        }

        var temporal = ruta + $".{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporal, JsonSerializer.Serialize(datos, Opciones));
        File.Copy(temporal, ruta, overwrite: true);
        File.Delete(temporal);
    }
}
