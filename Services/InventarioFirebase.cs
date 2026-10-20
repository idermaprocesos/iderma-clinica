using Google.Cloud.Firestore;
using Iderma.Core.Services;
using IdermaClinica.Models;

namespace IdermaClinica.Services;

public readonly record struct FilaInventario(string Id, string Codigo, string Area, string Nombre, int Cantidad);

public static class InventarioFirebase
{
    public const string Coleccion = "inventario";

    private const string Cursor = "inventario-clinica";
    private const string CursorBajas = "inventario-clinica-bajas";

    private static readonly object Candado = new();
    private static readonly TaskCompletionSource<bool> Lista = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static FirestoreDb? _base;
    private static FirestoreChangeListener? _escucha;
    private static FirestoreChangeListener? _escuchaBajas;
    private static int _escuchando;

    public static Task EsperarListaAsync() => Lista.Task;

    public static void IniciarEscucha(
        Func<IReadOnlyList<FilaInventario>, IReadOnlyList<string>, bool, Task> alRecibir,
        Action<string> alFallar,
        bool hayCopiaLocal,
        DateTimeOffset? fechaCopia)
    {
        ArgumentNullException.ThrowIfNull(alRecibir);
        ArgumentNullException.ThrowIfNull(alFallar);
        if (Interlocked.Exchange(ref _escuchando, 1) == 1)
        {
            return;
        }

        _ = EscucharAsync(alRecibir, alFallar, hayCopiaLocal, fechaCopia);
    }

    private static async Task EscucharAsync(
        Func<IReadOnlyList<FilaInventario>, IReadOnlyList<string>, bool, Task> alRecibir,
        Action<string> alFallar,
        bool hayCopiaLocal,
        DateTimeOffset? fechaCopia)
    {
        try
        {
            var db = await AsegurarBaseAsync();
            var guardado = CursorNube.Cargar(Cursor);
            Timestamp desde;
            if (guardado.HasValue)
            {
                desde = guardado.Value;
            }
            else if (hayCopiaLocal)
            {
                desde = fechaCopia.HasValue
                    ? Timestamp.FromDateTimeOffset(fechaCopia.Value.ToUniversalTime())
                    : Timestamp.FromDateTime(DateTime.UtcNow);
                CursorNube.Guardar(Cursor, desde);
            }
            else
            {
                var snap = await db.Collection(Coleccion).GetSnapshotAsync();
                await alRecibir(Mapear(snap.Documents), [], true);
                desde = snap.ReadTime;
                CursorNube.Guardar(Cursor, desde);
            }

            var desdeBajas = CursorNube.Cargar(CursorBajas) ?? desde;
            _escucha = db.Collection(Coleccion)
                .WhereGreaterThan("Actualizado", desde)
                .OrderBy("Actualizado")
                .Listen(snap => _ = EntregarAsync(snap, alRecibir, alFallar, Cursor, bajas: false));
            _escuchaBajas = db.Collection(Coleccion + "-bajas")
                .WhereGreaterThan("Actualizado", desdeBajas)
                .OrderBy("Actualizado")
                .Listen(snap => _ = EntregarAsync(snap, alRecibir, alFallar, CursorBajas, bajas: true));
            Lista.TrySetResult(true);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _escuchando, 0);
            Lista.TrySetException(ex);
            alFallar(ex.Message);
        }
    }

    private static async Task EntregarAsync(
        QuerySnapshot snap,
        Func<IReadOnlyList<FilaInventario>, IReadOnlyList<string>, bool, Task> alRecibir,
        Action<string> alFallar,
        string cursor,
        bool bajas)
    {
        try
        {
            var filas = new List<FilaInventario>();
            var eliminados = new List<string>();
            foreach (var cambio in snap.Changes)
            {
                if (bajas || cambio.ChangeType == DocumentChange.Type.Removed || !cambio.Document.Exists)
                {
                    eliminados.Add(cambio.Document.Id);
                    continue;
                }

                filas.AddRange(Mapear([cambio.Document]));
            }

            if (filas.Count > 0 || eliminados.Count > 0)
            {
                await alRecibir(filas, eliminados, false);
            }

            CursorNube.GuardarSiAvanza(cursor, snap.ReadTime);
            Lista.TrySetResult(true);
        }
        catch (Exception ex)
        {
            alFallar(ex.Message);
        }
    }

    private static async Task<FirestoreDb> AsegurarBaseAsync()
    {
        if (_base is not null)
        {
            return _base;
        }

        var db = await ConexionFirebase.AbrirAsync()
            ?? throw new InvalidOperationException("No se encontraron las credenciales de Firebase.");

        lock (Candado)
        {
            _base ??= db;
        }

        return _base;
    }

    private static List<FilaInventario> Mapear(IEnumerable<DocumentSnapshot> documentos)
    {
        var filas = new List<FilaInventario>();
        foreach (var doc in documentos)
        {
            if (!doc.Exists)
            {
                continue;
            }

            var datos = doc.ToDictionary();
            var codigo = Texto(datos, "Código", "Codigo");
            var area = Texto(datos, "Área", "Area");
            var nombre = Texto(datos, "Nombre", "Producto");
            if (string.IsNullOrWhiteSpace(codigo) &&
                string.IsNullOrWhiteSpace(area) &&
                string.IsNullOrWhiteSpace(nombre))
            {
                continue;
            }

            filas.Add(new FilaInventario(doc.Id, codigo, area, nombre, CantidadDe(datos)));
        }

        return filas;
    }

    private static int CantidadDe(IDictionary<string, object> datos)
    {
        if (datos.ContainsKey("Cantidad"))
        {
            return Entero(datos["Cantidad"]);
        }

        if (datos.ContainsKey("Existencia"))
        {
            return Entero(datos["Existencia"]);
        }

        if (!datos.TryGetValue("Partidas", out var raw) || raw is not IEnumerable<object> partidas)
        {
            return 0;
        }

        var total = 0;
        foreach (var partida in partidas)
        {
            if (partida is IDictionary<string, object> mapa && mapa.TryGetValue("Cantidad", out var cantidad))
            {
                total += Entero(cantidad);
            }
        }

        return total;
    }

    private static string Texto(IDictionary<string, object> datos, params string[] claves)
    {
        foreach (var clave in claves)
        {
            if (datos.TryGetValue(clave, out var valor) && valor is string texto && !string.IsNullOrWhiteSpace(texto))
            {
                return texto.Trim();
            }
        }

        return string.Empty;
    }

    private static int Entero(object? valor)
    {
        switch (valor)
        {
            case int entero:
                return Math.Max(0, entero);
            case long largo:
                return (int)Math.Max(0, largo);
            case double doble when !double.IsNaN(doble) && !double.IsInfinity(doble):
                return Math.Max(0, (int)Math.Round(doble, MidpointRounding.AwayFromZero));
            case string texto when int.TryParse(texto.Trim(), out var parseado):
                return Math.Max(0, parseado);
            default:
                return 0;
        }
    }

}
