using System.Globalization;
using Google.Cloud.Firestore;
using Iderma.Core.Services;
using IdermaClinica.Models;
using Microsoft.UI.Dispatching;

namespace IdermaClinica.Services;

public static class SolicitudesFirebase
{
    public const string Coleccion = "solicitudes-clinica";

    private static FirestoreDb? _base;
    private static FirestoreChangeListener? _escucha;
    private static int _iniciado;
    private static bool _avisarAltas;
    private static DispatcherQueue? _ui;

    public static event Action? RespuestasRecibidas;

    public static void IniciarEscucha(DispatcherQueue? ui = null)
    {
        if (ui is not null)
        {
            _ui = ui;
        }

        AvisoWindowsRespuesta.Preparar(ui);

        if (Interlocked.Exchange(ref _iniciado, 1) == 1)
        {
            return;
        }

        _ = EscucharAsync();
    }

    public static async Task PublicarAsync(PedidoHistorial pedido)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        await AsegurarBaseAsync();
        if (_base is null)
        {
            throw new InvalidOperationException("No se encontraron las credenciales de Firebase.");
        }

        var datos = new Dictionary<string, object>
        {
            ["Folio"] = pedido.Folio,
            ["Enfermera"] = pedido.Enfermera,
            ["Fecha"] = pedido.Fecha.ToString("o", CultureInfo.InvariantCulture),
            ["Urgente"] = pedido.Urgente,
            ["Estado"] = "Pendiente",
            ["Motivo denegación"] = string.Empty,
            ["Fecha respuesta"] = string.Empty,
            ["Origen"] = "clinica",
            ["Items"] = pedido.Items.Select(i => (object)new Dictionary<string, object>
            {
                ["Código"] = i.Codigo,
                ["Área"] = i.Area,
                ["Nombre"] = i.Nombre,
                ["Cantidad"] = i.Cantidad
            }).ToList(),
            ["Actualizado"] = FieldValue.ServerTimestamp
        };

        await _base.Collection(Coleccion).Document(pedido.Id).SetAsync(datos);
    }

    private static async Task EscucharAsync()
    {
        try
        {
            await AsegurarBaseAsync();
            if (_base is null)
            {
                return;
            }

            var guardado = CursorNube.Cargar(Coleccion + "-respuestas");
            _avisarAltas = guardado.HasValue;
            var desde = guardado ?? CursorNube.Epoca;
            _escucha = _base.Collection(Coleccion)
                .WhereGreaterThan("Actualizado", desde)
                .OrderBy("Actualizado")
                .Listen(snapshot =>
                {
                    AlRecibir(snapshot);
                    CursorNube.GuardarSiAvanza(Coleccion + "-respuestas", snapshot.ReadTime);
                });
        }
        catch
        {
        }
    }

    private static void AlRecibir(QuerySnapshot snapshot)
    {
        try
        {
            var cambios = new List<CambioRespuesta>();
            foreach (var cambio in snapshot.Changes)
            {
                if (cambio.ChangeType == DocumentChange.Type.Removed || !cambio.Document.Exists)
                {
                    continue;
                }

                var datos = cambio.Document.ToDictionary();
                var estadoTexto = Texto(datos, "Estado");
                if (!estadoTexto.Equals("Aceptada", StringComparison.OrdinalIgnoreCase) &&
                    !estadoTexto.Equals("Denegada", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var estado = estadoTexto.Equals("Aceptada", StringComparison.OrdinalIgnoreCase)
                    ? EstadoPedido.Aceptado
                    : EstadoPedido.Denegado;
                // La carga inicial (sin cursor) llega como Added y no debe avisar.
                // Con cursor, una respuesta nueva también llega como Added.
                var aviso = cambio.ChangeType == DocumentChange.Type.Modified
                    || (_avisarAltas && cambio.ChangeType == DocumentChange.Type.Added);
                cambios.Add(new CambioRespuesta(
                    cambio.Document.Id,
                    estado,
                    Texto(datos, "Motivo denegación", "MotivoDenegacion"),
                    Fecha(datos, "Fecha respuesta", "FechaRespuesta"),
                    aviso,
                    Bool(datos, "Cantidad acordada"),
                    LeerLineasRespuesta(datos)));
            }

            if (cambios.Count == 0)
            {
                return;
            }

            EnUi(() => _ = AplicarRespuestasAsync(cambios));
        }
        catch
        {
        }
    }

    private static async Task AplicarRespuestasAsync(IReadOnlyList<CambioRespuesta> cambios)
    {
        var hubo = false;
        foreach (var cambio in cambios)
        {
            var aplicado = await PedidoService.Instance.AplicarRespuestaLogisticaAsync(
                cambio.Id,
                cambio.Estado,
                cambio.Motivo,
                cambio.Fecha,
                cambio.CantidadAcordada,
                cambio.Lineas);
            hubo |= aplicado;
            if (!aplicado)
            {
                continue;
            }

            var pedido = PedidoService.Instance.Historial
                .FirstOrDefault(h => h.Id.Equals(cambio.Id, StringComparison.OrdinalIgnoreCase));
            if (pedido is null)
            {
                continue;
            }

            // Guarda en local; el toast solo en cambios reales (Modified).
            var local = await NotificacionesLocales.Instance.RegistrarRespuestaAsync(pedido);
            if (cambio.Aviso && local is not null)
            {
                AvisoWindowsRespuesta.Mostrar(local);
            }
        }

        if (hubo)
        {
            RespuestasRecibidas?.Invoke();
        }
    }

    private static async Task AsegurarBaseAsync()
    {
        if (_base is not null)
        {
            return;
        }

        _base = await ConexionFirebase.AbrirAsync();
    }

    private static void EnUi(Action accion)
    {
        var cola = _ui ?? DispatcherQueue.GetForCurrentThread();
        if (cola is null || cola.HasThreadAccess)
        {
            accion();
            return;
        }

        cola.TryEnqueue(() => accion());
    }

    private static List<(string Codigo, string Nombre, int Cantidad)> LeerLineasRespuesta(IDictionary<string, object> datos)
    {
        var lineas = new List<(string Codigo, string Nombre, int Cantidad)>();
        if (!datos.TryGetValue("Items respuesta", out var raw) || raw is not IEnumerable<object> lista)
        {
            return lineas;
        }

        foreach (var item in lista)
        {
            if (item is not IDictionary<string, object> mapa)
            {
                continue;
            }

            lineas.Add((
                Texto(mapa, "Código", "Codigo"),
                Texto(mapa, "Nombre", "Producto"),
                Entero(mapa.TryGetValue("Cantidad", out var cantidad) ? cantidad : 0)));
        }

        return lineas;
    }

    private static bool Bool(IDictionary<string, object> datos, string clave) =>
        datos.TryGetValue(clave, out var valor) && valor switch
        {
            bool b => b,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1",
            _ => false
        };

    private static int Entero(object? valor) => valor switch
    {
        int i => Math.Max(0, i),
        long l => (int)Math.Max(0, l),
        double d when !double.IsNaN(d) && !double.IsInfinity(d) => Math.Max(0, (int)Math.Round(d)),
        string s when int.TryParse(s, out var n) => Math.Max(0, n),
        _ => 0
    };

    private sealed record CambioRespuesta(
        string Id,
        EstadoPedido Estado,
        string Motivo,
        DateTimeOffset? Fecha,
        bool Aviso,
        bool CantidadAcordada,
        IReadOnlyList<(string Codigo, string Nombre, int Cantidad)> Lineas);

    private static string Texto(IDictionary<string, object> datos, params string[] claves)
    {
        foreach (var clave in claves)
        {
            if (datos.TryGetValue(clave, out var valor) && valor is string texto)
            {
                return texto.Trim();
            }
        }

        return string.Empty;
    }

    private static DateTimeOffset? Fecha(IDictionary<string, object> datos, params string[] claves)
    {
        foreach (var clave in claves)
        {
            if (!datos.TryGetValue(clave, out var valor))
            {
                continue;
            }

            if (valor is Timestamp ts)
            {
                return ts.ToDateTimeOffset();
            }

            if (valor is string texto &&
                DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fecha))
            {
                return fecha;
            }
        }

        return null;
    }

}
