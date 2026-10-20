using Google.Cloud.Firestore;

namespace Iderma.Core.Services;

internal static class ClaveNube
{
    private const string Documento = "clave";

    public static byte[]? Leer()
    {
        try
        {
            var tarea = Task.Run(LeerAsync);
            return tarea.Wait(TimeSpan.FromSeconds(20)) ? tarea.GetAwaiter().GetResult() : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool PublicarSiVacia(byte[] clave)
    {
        try
        {
            var tarea = Task.Run(() => PublicarSiVaciaAsync(clave));
            return tarea.Wait(TimeSpan.FromSeconds(20)) && tarea.GetAwaiter().GetResult();
        }
        catch
        {
            return false;
        }
    }

    private static async Task<byte[]?> LeerAsync()
    {
        var db = await CuentasNube.AbrirAsync();
        if (db is null)
        {
            return null;
        }

        var snap = await db.Collection("boveda").Document(Documento).GetSnapshotAsync();
        if (!snap.Exists || !snap.ContainsField("k"))
        {
            return null;
        }

        var clave = Convert.FromBase64String(snap.GetValue<string>("k"));
        return clave.Length == 32 ? clave : null;
    }

    private static async Task<bool> PublicarSiVaciaAsync(byte[] clave)
    {
        var db = await CuentasNube.AbrirAsync();
        if (db is null)
        {
            return false;
        }

        var doc = db.Collection("boveda").Document(Documento);
        var yaExistia = false;
        await db.RunTransactionAsync(async tx =>
        {
            var snap = await tx.GetSnapshotAsync(doc);
            if (snap.Exists && snap.ContainsField("k"))
            {
                yaExistia = true;
                return;
            }

            yaExistia = false;
            tx.Set(doc, new Dictionary<string, object>
            {
                ["v"] = 1,
                ["k"] = Convert.ToBase64String(clave)
            });
        });

        return !yaExistia;
    }
}
