using Google.Cloud.Firestore;

namespace Iderma.Core.Services;

internal sealed class RevisionNubeException : Exception;

internal readonly record struct SobreCuentas(long Rev, int Version, string Nonce, string Cifrado);

internal readonly record struct LecturaNube(bool EnLinea, SobreCuentas? Sobre);

internal static class CuentasNube
{
    public const string Proyecto = ConexionFirebase.Proyecto;
    private const string Coleccion = "boveda";
    private const string Documento = "cuentas";

    private static FirestoreChangeListener? _escucha;
    private static int _escuchando;

    public static event Action<SobreCuentas>? Recibido;

    public static Task<LecturaNube> LeerAsync() => LeerDocumentoAsync(Documento);

    public static async Task<LecturaNube> LeerDocumentoAsync(string documento)
    {
        try
        {
            var db = await BaseAsync();
            if (db is null)
            {
                return new LecturaNube(false, null);
            }

            var snap = await db.Collection(Coleccion).Document(documento).GetSnapshotAsync();
            return new LecturaNube(true, Mapear(snap));
        }
        catch
        {
            return new LecturaNube(false, null);
        }
    }

    public static Task<bool> GuardarSiActualAsync(long esperada, long nueva, int version, string nonce, string cifrado) =>
        GuardarDocumentoAsync(Documento, esperada, nueva, version, nonce, cifrado);

    public static async Task<bool> GuardarDocumentoAsync(string documento, long esperada, long nueva, int version, string nonce, string cifrado)
    {
        var db = await BaseAsync();
        if (db is null)
        {
            return true;
        }

        var doc = db.Collection(Coleccion).Document(documento);
        try
        {
            await db.RunTransactionAsync(async tx =>
            {
                var snap = await tx.GetSnapshotAsync(doc);
                long actual = 0;
                if (snap.Exists && snap.ContainsField("rev"))
                {
                    actual = snap.GetValue<long>("rev");
                }

                if (actual > esperada)
                {
                    throw new RevisionNubeException();
                }

                tx.Set(doc, new Dictionary<string, object>
                {
                    ["v"] = version,
                    ["rev"] = nueva,
                    ["n"] = nonce,
                    ["c"] = cifrado
                });
            });
            return true;
        }
        catch (RevisionNubeException)
        {
            return false;
        }
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
            var db = await BaseAsync();
            if (db is null)
            {
                Interlocked.Exchange(ref _escuchando, 0);
                return;
            }

            _escucha = db.Collection(Coleccion).Document(Documento).Listen(snap =>
            {
                var sobre = Mapear(snap);
                if (sobre is not null)
                {
                    Recibido?.Invoke(sobre.Value);
                }
            });
        }
        catch
        {
            Interlocked.Exchange(ref _escuchando, 0);
        }
    }

    private static SobreCuentas? Mapear(DocumentSnapshot snap)
    {
        if (!snap.Exists || !snap.ContainsField("n") || !snap.ContainsField("c"))
        {
            return null;
        }

        var rev = snap.ContainsField("rev") ? snap.GetValue<long>("rev") : 0;
        var version = snap.ContainsField("v") ? (int)snap.GetValue<long>("v") : 1;
        return new SobreCuentas(rev, version, snap.GetValue<string>("n"), snap.GetValue<string>("c"));
    }

    private static Task<FirestoreDb?> BaseAsync() => ConexionFirebase.AbrirAsync();

    internal static Task<FirestoreDb?> AbrirAsync() => BaseAsync();
}
