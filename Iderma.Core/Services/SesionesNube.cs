using System.Text.Encodings.Web;
using System.Text.Json;
using Google.Cloud.Firestore;

namespace Iderma.Core.Services;

internal sealed class FilaSesion
{
    public string Equipo { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
}

internal sealed class RegistroSesiones
{
    public List<FilaSesion> Equipos { get; set; } = [];
    public Dictionary<string, long> Cierres { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal static class SesionesNube
{
    private const string Documento = "sesiones";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static FirestoreChangeListener? _escucha;
    private static int _escuchando;

    public static event Action<RegistroSesiones>? Recibido;

    public static async Task<RegistroSesiones?> LeerAsync()
    {
        var db = await CuentasNube.AbrirAsync();
        if (db is null)
        {
            return null;
        }

        var snap = await db.Collection("boveda").Document(Documento).GetSnapshotAsync();
        return Abrir(snap);
    }

    public static async Task<RegistroSesiones> MutarAsync(Func<RegistroSesiones, bool> cambio)
    {
        var db = await CuentasNube.AbrirAsync()
            ?? throw new InvalidOperationException("No hay conexión con Firebase para cerrar la sesión.");
        var doc = db.Collection("boveda").Document(Documento);
        RegistroSesiones resultado = new();
        await db.RunTransactionAsync(async tx =>
        {
            var snap = await tx.GetSnapshotAsync(doc);
            var rev = snap.Exists && snap.ContainsField("rev") ? snap.GetValue<long>("rev") : 0;
            var registro = Abrir(snap) ?? new RegistroSesiones();
            registro.Equipos ??= [];
            registro.Cierres = new Dictionary<string, long>(registro.Cierres ?? [], StringComparer.OrdinalIgnoreCase);
            if (!cambio(registro))
            {
                resultado = registro;
                return;
            }

            var (nonce, cifrado) = CifradoCuentas.Sellar(JsonSerializer.Serialize(registro, Json));
            tx.Set(doc, new Dictionary<string, object>
            {
                ["v"] = CifradoCuentas.VersionActual,
                ["rev"] = rev + 1,
                ["n"] = nonce,
                ["c"] = cifrado
            });
            resultado = registro;
        });
        return resultado;
    }

    public static void AsegurarEscucha()
    {
        if (Interlocked.Exchange(ref _escuchando, 1) == 1)
        {
            return;
        }

        _ = EscucharAsync();
    }

    private static async Task EscucharAsync()
    {
        try
        {
            var db = await CuentasNube.AbrirAsync();
            if (db is null)
            {
                Interlocked.Exchange(ref _escuchando, 0);
                return;
            }

            _escucha = db.Collection("boveda").Document(Documento).Listen(snap =>
            {
                var registro = Abrir(snap);
                if (registro is not null)
                {
                    Recibido?.Invoke(registro);
                }
            });
        }
        catch
        {
            Interlocked.Exchange(ref _escuchando, 0);
        }
    }

    private static RegistroSesiones? Abrir(DocumentSnapshot snap)
    {
        if (!snap.Exists || !snap.ContainsField("n") || !snap.ContainsField("c"))
        {
            return null;
        }

        var version = snap.ContainsField("v") ? (int)snap.GetValue<long>("v") : 1;
        var claro = CifradoCuentas.Abrir(snap.GetValue<string>("n"), snap.GetValue<string>("c"), version);
        var registro = JsonSerializer.Deserialize<RegistroSesiones>(claro, Json) ?? new RegistroSesiones();
        registro.Equipos ??= [];
        registro.Cierres = new Dictionary<string, long>(registro.Cierres ?? [], StringComparer.OrdinalIgnoreCase);
        return registro;
    }
}
