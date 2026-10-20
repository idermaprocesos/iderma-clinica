using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using IdermaClinica.Models;

namespace IdermaClinica.Services;

public sealed class PedidoService
{
    private const string NombreArchivo = "pedidos.json";

    public static PedidoService Instance { get; } = new();

    private readonly CatalogoService _catalogo = CatalogoService.Instance;
    private readonly SemaphoreSlim _lockGuardar = new(1, 1);
    private int _ultimoFolio;
    private bool _cargando;
    private int _pausaPersistencia;

    private PedidoService()
    {
        Items.CollectionChanged += Items_CollectionChanged;
        Historial.CollectionChanged += (_, _) => PedidoActualizado?.Invoke(this, EventArgs.Empty);
        _catalogo.CatalogoActualizado += (_, _) => ReenlazarConCatalogo();
    }

    public ObservableCollection<PedidoItem> Items { get; } = [];

    public ObservableCollection<PedidoHistorial> Historial { get; } = [];

    public string NombreLista { get; private set; } = string.Empty;

    public DateTimeOffset Fecha { get; private set; }

    public int TotalCantidad => Items.Sum(i => i.Cantidad);

    public string ResumenLista => $"{NombreLista} · {Items.Count} productos · {TotalCantidad} pzas";

    public ResultadoPedido? AvisoPendiente { get; set; }

    public event EventHandler? PedidoActualizado;

    public async Task CargarAsync()
    {
        _cargando = true;
        try
        {
            var estado = await LocalStore.LeerAsync<PedidosEstado>(NombreArchivo);
            _ultimoFolio = estado?.UltimoFolio ?? 0;

            Historial.Clear();
            foreach (var pedido in estado?.Historial ?? [])
            {
                Historial.Add(pedido);
            }

            if (estado?.Borrador is { Items.Count: > 0 } borrador)
            {
                NombreLista = string.IsNullOrWhiteSpace(borrador.NombreLista)
                    ? $"Pedido {DateTimeOffset.Now.ToLocalTime():dd/MM/yyyy HH:mm}"
                    : borrador.NombreLista;
                Fecha = borrador.Fecha == default ? DateTimeOffset.Now : borrador.Fecha;
                ReemplazarItems(borrador.Items, fusionar: false);
            }
            else
            {
                CargarNuevaLista(guardar: false);
            }
        }
        catch
        {
            if (Historial.Count == 0)
            {
                CargarNuevaLista(guardar: false);
            }
        }
        finally
        {
            _cargando = false;
            Notificar();
        }
    }

    public ResultadoPedido AgregarUno(Producto producto)
    {
        if (producto.Cantidad <= 0)
        {
            return ResultadoPedido.SinStock($"Ya no hay de {NombreVisible(producto)}.");
        }

        var existente = BuscarEnLista(producto);
        if (existente is not null)
        {
            if (existente.Cantidad >= producto.Cantidad)
            {
                return ResultadoPedido.SinStock($"Ya no hay más de {NombreVisible(producto)}. Solo hay {producto.Cantidad} en inventario.");
            }

            existente.Cantidad += 1;
            return ResultadoPedido.Ok($"Se sumó 1 de {NombreVisible(producto)}. Van {existente.Cantidad} (disponible {producto.Cantidad}).");
        }

        Items.Add(new PedidoItem(producto, 1));
        return ResultadoPedido.Ok($"Se agregó {NombreVisible(producto)}. Disponible {producto.Cantidad}.");
    }

    public ResultadoPedido IntentarCambiarCantidad(PedidoItem item, int cantidad)
    {
        if (item.Producto.Cantidad <= 0)
        {
            item.Cantidad = 0;
            return ResultadoPedido.SinStock($"Ya no hay de {NombreVisible(item.Producto)}.");
        }

        if (cantidad > item.Producto.Cantidad)
        {
            item.Cantidad = item.Producto.Cantidad;
            return ResultadoPedido.SinStock($"Ya no hay suficientes de {NombreVisible(item.Producto)}. Solo hay {item.Producto.Cantidad} en inventario.");
        }

        item.Cantidad = Math.Max(1, cantidad);
        return ResultadoPedido.Ok(string.Empty);
    }

    public void Quitar(PedidoItem item)
    {
        Items.Remove(item);
    }

    public void CargarNuevaLista(bool guardar = true)
    {
        LimpiarItems();
        Fecha = DateTimeOffset.Now;
        NombreLista = $"Pedido {Fecha.ToLocalTime():dd/MM/yyyy HH:mm}";
        Notificar();
        if (guardar)
        {
            _ = GuardarAsync();
        }
    }

    public ResultadoPedido Repetir(PedidoHistorial pedido)
    {
        CargarNuevaLista(guardar: false);
        var resultado = Anclar(pedido);
        NombreLista = $"Repetición {pedido.Folio}";
        Notificar();
        _ = GuardarAsync();
        return resultado;
    }

    public ResultadoPedido Anclar(PedidoHistorial pedido)
    {
        var avisos = new List<string>();
        var agregados = 0;

        foreach (var linea in pedido.Items)
        {
            var producto = _catalogo.BuscarPorLinea(linea);
            if (producto is null)
            {
                avisos.Add($"Ya no está en inventario: {linea.Nombre}.");
                continue;
            }

            if (producto.Cantidad <= 0)
            {
                avisos.Add($"Ya no hay de {NombreVisible(producto)}.");
                continue;
            }

            var existente = BuscarEnLista(producto);
            var actual = existente?.Cantidad ?? 0;
            var deseada = actual + linea.Cantidad;
            var final = Math.Min(deseada, producto.Cantidad);

            if (existente is null)
            {
                Items.Add(new PedidoItem(producto, final));
            }
            else
            {
                existente.Cantidad = final;
            }

            agregados++;
            if (final < deseada)
            {
                avisos.Add($"Solo hay {producto.Cantidad} de {NombreVisible(producto)}; no se pudo pedir todo.");
            }
        }

        _ = GuardarAsync();

        if (agregados == 0)
        {
            return ResultadoPedido.SinStock(avisos.Count == 0
                ? "No se pudo anclar el pedido."
                : string.Join(" ", avisos));
        }

        var ok = $"Se anclaron {agregados} productos del {pedido.Folio} a la lista.";
        return avisos.Count == 0
            ? ResultadoPedido.Ok(ok)
            : ResultadoPedido.Parcial($"{ok} {string.Join(" ", avisos)}");
    }

    public async Task<ResultadoPedido> ProcesarAsync(Enfermera enfermera, bool urgente)
    {
        if (Items.Count == 0)
        {
            return ResultadoPedido.Error("La lista está vacía.");
        }

        if (string.IsNullOrWhiteSpace(enfermera.Nombre))
        {
            return ResultadoPedido.Error("Elige a la enfermera que envía el pedido.");
        }

        var lineas = Items
            .Select(i =>
            {
                var cantidad = i.Cantidad > 0 ? i.Cantidad : 1;
                cantidad = Math.Min(cantidad, Math.Max(0, i.Producto.Cantidad));
                var linea = i.ALinea();
                linea.Cantidad = cantidad;
                return linea;
            })
            .Where(l => l.Cantidad > 0)
            .ToList();
        if (lineas.Count == 0)
        {
            return ResultadoPedido.Error("Todos los productos tienen cantidad 0.");
        }

        foreach (var linea in lineas)
        {
            var producto = _catalogo.BuscarPorLinea(linea);
            if (producto is null)
            {
                return ResultadoPedido.SinStock($"Ya no está en inventario: {linea.Nombre}.");
            }

            if (linea.Cantidad > producto.Cantidad)
            {
                return ResultadoPedido.SinStock(
                    linea.Cantidad > 0 && producto.Cantidad <= 0
                        ? $"Ya no hay de {producto.Nombre}."
                        : $"Ya no hay suficientes de {producto.Nombre}. Solo hay {producto.Cantidad} en inventario.");
            }
        }

        _pausaPersistencia++;
        try
        {
            await _catalogo.DescontarAsync(lineas);

            _ultimoFolio++;
            var historial = new PedidoHistorial
            {
                Folio = $"PED-{DateTime.Now:yyyyMMdd}-{_ultimoFolio:000}",
                Enfermera = enfermera.Nombre,
                Fecha = DateTimeOffset.Now,
                Estado = EstadoPedido.Enviado,
                Urgente = urgente,
                Items = lineas
            };

            Historial.Insert(0, historial);
            CargarNuevaLista(guardar: false);
        }
        finally
        {
            _pausaPersistencia--;
        }

        await GuardarAsync();

        var enviado = Historial[0];
        var avisoNube = string.Empty;
        try
        {
            await SolicitudesFirebase.PublicarAsync(enviado);
        }
        catch (Exception ex)
        {
            avisoNube = " El pedido quedó en el historial local, pero no se pudo notificar a logística: " + ex.Message;
        }

        var prioridad = urgente ? " como URGENTE" : string.Empty;
        return ResultadoPedido.Ok(
            $"Pedido {enviado.Folio} enviado{prioridad} por {enfermera.Nombre} a logística." + avisoNube);
    }

    public async Task<bool> AplicarRespuestaLogisticaAsync(
        string id,
        EstadoPedido estado,
        string motivo,
        DateTimeOffset? fecha,
        bool cantidadAcordada,
        IReadOnlyList<(string Codigo, string Nombre, int Cantidad)> lineas)
    {
        if (estado is not (EstadoPedido.Aceptado or EstadoPedido.Denegado))
        {
            return false;
        }

        var pedido = Historial.FirstOrDefault(h => h.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (pedido is null)
        {
            return false;
        }

        var textoMotivo = motivo ?? string.Empty;
        if (pedido.Estado == estado &&
            pedido.CantidadAcordada == cantidadAcordada &&
            string.Equals(pedido.MotivoRespuesta, textoMotivo, StringComparison.Ordinal) &&
            pedido.FechaAtendido == fecha &&
            LineasYaAplicadas(pedido, lineas))
        {
            return false;
        }

        pedido.CantidadAcordada = cantidadAcordada;
        pedido.Estado = estado;
        pedido.MotivoRespuesta = textoMotivo;
        pedido.FechaAtendido = fecha ?? DateTimeOffset.Now;
        pedido.AplicarLineasRespondidas(lineas);
        await GuardarAsync();
        Notificar();
        return true;
    }

    private static bool LineasYaAplicadas(
        PedidoHistorial pedido,
        IReadOnlyList<(string Codigo, string Nombre, int Cantidad)> lineas)
    {
        if (lineas.Count == 0)
        {
            return pedido.Items.All(i => i.CantidadRespondida is null);
        }

        return lineas.All(linea => pedido.Items.Any(i =>
            i.CantidadRespondida == linea.Cantidad &&
            (string.IsNullOrWhiteSpace(linea.Codigo)
                ? i.Nombre.Equals(linea.Nombre, StringComparison.OrdinalIgnoreCase)
                : i.Codigo.Equals(linea.Codigo, StringComparison.OrdinalIgnoreCase))));
    }

    public async Task<ResultadoPedido> ExportarHistorialAsync(string ruta)
    {
        var datos = new PedidosEstado
        {
            UltimoFolio = _ultimoFolio,
            Historial = [.. Historial]
        };
        await LocalStore.GuardarEnRutaAsync(ruta, datos);
        return ResultadoPedido.Ok($"Se exportaron {Historial.Count} pedidos del historial.");
    }

    public async Task<ResultadoPedido> ImportarHistorialAsync(string ruta, bool reemplazar)
    {
        var datos = await LocalStore.LeerDesdeRutaAsync<PedidosEstado>(ruta);
        if (datos?.Historial is null || datos.Historial.Count == 0)
        {
            return ResultadoPedido.Error("El archivo no contiene pedidos del historial.");
        }

        _pausaPersistencia++;
        var agregados = 0;
        try
        {
            if (reemplazar)
            {
                Historial.Clear();
            }

            var existentes = Historial
                .SelectMany(h => new[] { h.Id, h.Folio })
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var pedido in datos.Historial.OrderByDescending(p => p.Fecha))
            {
                if (!reemplazar && (existentes.Contains(pedido.Id) || existentes.Contains(pedido.Folio)))
                {
                    continue;
                }

                Historial.Add(pedido);
                existentes.Add(pedido.Id);
                existentes.Add(pedido.Folio);
                agregados++;
            }

            var ordenados = Historial.OrderByDescending(h => h.Fecha).ToList();
            Historial.Clear();
            foreach (var pedido in ordenados)
            {
                Historial.Add(pedido);
            }

            _ultimoFolio = Math.Max(_ultimoFolio, datos.UltimoFolio);
            foreach (var pedido in Historial)
            {
                var separador = pedido.Folio.LastIndexOf('-');
                if (separador >= 0 && int.TryParse(pedido.Folio[(separador + 1)..], out var numero) && numero > _ultimoFolio)
                {
                    _ultimoFolio = numero;
                }
            }
        }
        finally
        {
            _pausaPersistencia--;
        }

        await GuardarAsync();
        Notificar();

        if (agregados == 0)
        {
            return ResultadoPedido.Parcial("No se agregaron pedidos; ya estaban en el historial.");
        }

        return ResultadoPedido.Ok(reemplazar
            ? $"Se importaron {agregados} pedidos y se reemplazó el historial."
            : $"Se combinaron {agregados} pedidos con el historial.");
    }

    public async Task MarcarEstadoAsync(PedidoHistorial pedido, EstadoPedido estado)
    {
        pedido.Estado = estado;
        pedido.FechaAtendido = estado == EstadoPedido.Atendido ? DateTimeOffset.Now : null;
        await GuardarAsync();
        Notificar();
    }

    private PedidoItem? BuscarEnLista(Producto producto)
    {
        return Items.FirstOrDefault(i => i.Producto.Clave.Equals(producto.Clave, StringComparison.OrdinalIgnoreCase));
    }

    private void ReenlazarConCatalogo()
    {
        if (_pausaPersistencia > 0 || _cargando)
        {
            return;
        }

        if (Items.Count == 0)
        {
            return;
        }

        var lineas = Items.Select(i => i.ALinea()).ToList();
        ReemplazarItems(lineas, fusionar: false);
    }

    private void ReemplazarItems(IEnumerable<PedidoLinea> lineas, bool fusionar)
    {
        if (!fusionar)
        {
            LimpiarItems();
        }

        foreach (var linea in lineas)
        {
            var producto = _catalogo.BuscarPorLinea(linea);
            if (producto is null || producto.Cantidad <= 0)
            {
                continue;
            }

            var existente = BuscarEnLista(producto);
            var cantidad = Math.Min(linea.Cantidad, producto.Cantidad);
            if (existente is null)
            {
                Items.Add(new PedidoItem(producto, cantidad));
            }
            else
            {
                existente.Cantidad = Math.Min(producto.Cantidad, existente.Cantidad + cantidad);
            }
        }
    }

    private void LimpiarItems()
    {
        foreach (var item in Items.ToList())
        {
            item.PropertyChanged -= Item_PropertyChanged;
        }

        Items.Clear();
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (PedidoItem item in e.NewItems)
            {
                item.PropertyChanged += Item_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (PedidoItem item in e.OldItems)
            {
                item.PropertyChanged -= Item_PropertyChanged;
            }
        }

        Notificar();
        if (!_cargando && _pausaPersistencia == 0)
        {
            _ = GuardarAsync();
        }
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Notificar();
        if (!_cargando && _pausaPersistencia == 0)
        {
            _ = GuardarAsync();
        }
    }

    private void Notificar()
    {
        PedidoActualizado?.Invoke(this, EventArgs.Empty);
    }

    private async Task GuardarAsync()
    {
        await _lockGuardar.WaitAsync();
        try
        {
            var estado = new PedidosEstado
            {
                UltimoFolio = _ultimoFolio,
                Borrador = new PedidoBorrador
                {
                    NombreLista = NombreLista,
                    Fecha = Fecha,
                    Items = [.. Items.Select(i => i.ALinea())]
                },
                Historial = [.. Historial]
            };

            await LocalStore.GuardarAsync(NombreArchivo, estado);
        }
        catch
        {
            // El pedido ya quedó en memoria; se reintenta en el siguiente cambio.
        }
        finally
        {
            _lockGuardar.Release();
        }
    }

    private static string NombreVisible(Producto producto)
    {
        return string.IsNullOrWhiteSpace(producto.Nombre) ? producto.Codigo : producto.Nombre;
    }
}

public sealed class ResultadoPedido
{
    public bool Exito { get; private init; }
    public bool EsAdvertencia { get; private init; }
    public string Mensaje { get; private init; } = string.Empty;

    public static ResultadoPedido Ok(string mensaje) => new() { Exito = true, Mensaje = mensaje };
    public static ResultadoPedido Parcial(string mensaje) => new() { Exito = true, EsAdvertencia = true, Mensaje = mensaje };
    public static ResultadoPedido SinStock(string mensaje) => new() { Exito = false, EsAdvertencia = true, Mensaje = mensaje };
    public static ResultadoPedido Error(string mensaje) => new() { Exito = false, Mensaje = mensaje };
}
