using System.Globalization;
using Google.Cloud.Firestore;

namespace Iderma.Core.Services;

/// <summary>
/// Marca de la última lectura en vivo. La siguiente escucha pide solo documentos más nuevos.
/// </summary>
public static class CursorNube
{
    private static readonly object Candado = new();

    public static Timestamp Epoca { get; } = Timestamp.FromDateTime(DateTime.UnixEpoch);

    public static bool Existe(string nombre)
    {
        lock (Candado)
        {
            return File.Exists(Ruta(nombre));
        }
    }

    public static Timestamp? Cargar(string nombre)
    {
        lock (Candado)
        {
            var ruta = Ruta(nombre);
            if (!File.Exists(ruta))
            {
                return null;
            }

            try
            {
                var texto = File.ReadAllText(ruta).Trim();
                if (texto.Length == 0)
                {
                    return null;
                }

                var momento = DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                return Timestamp.FromDateTimeOffset(momento);
            }
            catch
            {
                return null;
            }
        }
    }

    public static void Guardar(string nombre, Timestamp marca)
    {
        lock (Candado)
        {
            var ruta = Ruta(nombre);
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            File.WriteAllText(ruta, marca.ToDateTimeOffset().ToString("o", CultureInfo.InvariantCulture));
        }
    }

    public static void GuardarSiAvanza(string nombre, Timestamp marca)
    {
        lock (Candado)
        {
            var actual = CargarSinCandado(nombre);
            if (actual.HasValue && actual.Value.CompareTo(marca) >= 0)
            {
                return;
            }

            var ruta = Ruta(nombre);
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            File.WriteAllText(ruta, marca.ToDateTimeOffset().ToString("o", CultureInfo.InvariantCulture));
        }
    }

    private static Timestamp? CargarSinCandado(string nombre)
    {
        var ruta = Ruta(nombre);
        if (!File.Exists(ruta))
        {
            return null;
        }

        try
        {
            var texto = File.ReadAllText(ruta).Trim();
            if (texto.Length == 0)
            {
                return null;
            }

            var momento = DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            return Timestamp.FromDateTimeOffset(momento);
        }
        catch
        {
            return null;
        }
    }

    private static string Ruta(string nombre)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            nombre = nombre.Replace(c, '-');
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IdermaCapilar",
            "nube-cursor-" + nombre + ".json");
    }
}
