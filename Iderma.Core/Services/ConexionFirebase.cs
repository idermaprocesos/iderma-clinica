using System.Collections.Concurrent;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;

namespace Iderma.Core.Services;

/// <summary>
/// Una sola credencial y un cliente de Firestore por base.
/// Las lecturas y escrituras de todo el programa reutilizan ese canal.
/// </summary>
public static class ConexionFirebase
{
    public const string Proyecto = "iderma-db-2026";
    private const string Predeterminada = "(default)";
    private const string Alcance = "https://www.googleapis.com/auth/cloud-platform";

    private static readonly SemaphoreSlim Puerta = new(1, 1);
    private static readonly ConcurrentDictionary<string, FirestoreDb> Bases = new(StringComparer.Ordinal);
    private static GoogleCredential? _credencial;
    private static string? _ruta;

    public static Task<FirestoreDb?> AbrirAsync(string? baseDatos = null) => AbrirInternoAsync(baseDatos);

    public static async Task<GoogleCredential?> CredencialAsync()
    {
        if (_credencial is not null)
        {
            return _credencial;
        }

        await Puerta.WaitAsync();
        try
        {
            return CredencialBajoCandado();
        }
        finally
        {
            Puerta.Release();
        }
    }

    private static async Task<FirestoreDb?> AbrirInternoAsync(string? baseDatos)
    {
        var id = string.IsNullOrWhiteSpace(baseDatos) ? Predeterminada : baseDatos;
        if (Bases.TryGetValue(id, out var existente))
        {
            return existente;
        }

        await Puerta.WaitAsync();
        try
        {
            if (Bases.TryGetValue(id, out existente))
            {
                return existente;
            }

            var credencial = CredencialBajoCandado();
            if (credencial is null)
            {
                return null;
            }

            var builder = new FirestoreDbBuilder
            {
                ProjectId = Proyecto,
                Credential = credencial
            };
            if (!string.Equals(id, Predeterminada, StringComparison.Ordinal))
            {
                builder.DatabaseId = id;
            }

            var db = await builder.BuildAsync();
            Bases[id] = db;
            return db;
        }
        finally
        {
            Puerta.Release();
        }
    }

    private static GoogleCredential? CredencialBajoCandado()
    {
        if (_credencial is not null)
        {
            return _credencial;
        }

        var ruta = BuscarArchivoCredenciales();
        if (ruta is null)
        {
            return null;
        }

        _credencial = CredentialFactory.FromFile<ServiceAccountCredential>(ruta)
            .ToGoogleCredential()
            .CreateScoped(Alcance);
        return _credencial;
    }

    public static string? BuscarArchivoCredenciales()
    {
        if (_ruta is not null && File.Exists(_ruta))
        {
            return _ruta;
        }

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IdermaCapilar",
            "firebase-adminsdk.json");
        if (File.Exists(appData))
        {
            _ruta = appData;
            return _ruta;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var cred = Path.Combine(dir.FullName, "cred");
            if (Directory.Exists(cred))
            {
                var archivo = Directory.GetFiles(cred, "*firebase-adminsdk*.json").FirstOrDefault();
                if (archivo is not null)
                {
                    _ruta = archivo;
                    return _ruta;
                }
            }

            dir = dir.Parent;
        }

        return null;
    }
}
