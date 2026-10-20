using System.Collections.ObjectModel;
using IdermaClinica.Models;
using Microsoft.UI.Dispatching;

namespace IdermaClinica.Services;

public sealed class CatalogoService
{
    private const string NombreArchivo = "catalogo.json";

    public static CatalogoService Instance { get; } = new();

    private CatalogoService()
    {
    }

    public ObservableCollection<Producto> Productos { get; } = [];

    public string ArchivoOrigen { get; private set; } = string.Empty;

    public DateTimeOffset? FechaImportacion { get; private set; }

    public bool TieneProductos => Productos.Count > 0;

    public bool Sincronizando { get; private set; }

    public bool EnVivo { get; private set; }

    public string? ErrorNube { get; private set; }

    private readonly Dictionary<string, Producto> _porDocumento = new(StringComparer.Ordinal);
    private DispatcherQueue? _ui;

    public string Resumen
    {
        get
        {
            if (Sincronizando && !TieneProductos)
            {
                return "Leyendo la colección de inventario en Firebase…";
            }

            if (!TieneProductos)
            {
                return ErrorNube
                    ?? "Aún no hay inventario. La clínica solo lee la colección de inventario en Firebase.";
            }

            var fecha = FechaImportacion?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "sin fecha";
            var origen = EnVivo
                ? "Firebase · inventario en vivo"
                : (string.IsNullOrWhiteSpace(ArchivoOrigen) ? "Firebase · inventario" : ArchivoOrigen);
            var aviso = string.IsNullOrWhiteSpace(ErrorNube) ? string.Empty : " " + ErrorNube;
            return $"{Productos.Count} productos desde {origen} · {fecha}.{aviso}";
        }
    }

    public event EventHandler? CatalogoActualizado;

    public async Task CargarAsync()
    {
        try
        {
            var estado = await LocalStore.LeerAsync<CatalogoEstado>(NombreArchivo);
            if (estado is not null)
            {
                Reemplazar(estado.Productos, estado.ArchivoOrigen, estado.FechaImportacion, guardar: false);
            }
        }
        catch
        {
            // Si la copia local está dañada, se vuelve a leer la colección de inventario.
        }
    }

    public void IniciarEnVivo(DispatcherQueue? ui)
    {
        if (ui is not null)
        {
            _ui = ui;
        }

        InventarioFirebase.IniciarEscucha(
            async (filas, eliminados, reemplazar) =>
            {
                await EnUiAsync(() => AplicarCambio(filas, eliminados, reemplazar));
            },
            mensaje => EnUi(() =>
            {
                Sincronizando = false;
                ErrorNube = "No se pudo leer el inventario de Firebase. " + mensaje;
                CatalogoActualizado?.Invoke(this, EventArgs.Empty);
            }),
            TieneProductos,
            FechaImportacion);
    }

    public async Task SincronizarFirebaseAsync()
    {
        await EnUiAsync(() =>
        {
            Sincronizando = true;
            ErrorNube = null;
            CatalogoActualizado?.Invoke(this, EventArgs.Empty);
        });

        try
        {
            IniciarEnVivo(_ui);
            await InventarioFirebase.EsperarListaAsync();
            await EnUiAsync(() =>
            {
                Sincronizando = false;
                if (!EnVivo)
                {
                    EnVivo = true;
                    ArchivoOrigen = "Firebase · inventario en vivo";
                }

                CatalogoActualizado?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (Exception ex)
        {
            await EnUiAsync(() =>
            {
                Sincronizando = false;
                ErrorNube = "No se pudo leer el inventario de Firebase. " + ex.Message;
                CatalogoActualizado?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    public async Task ImportarExcelAsync(string ruta, string nombreArchivo)
    {
        var productos = ExcelImportService.Importar(ruta);
        Reemplazar(productos, nombreArchivo, DateTimeOffset.Now, guardar: false);
        await GuardarAsync();
    }

    public Producto? BuscarPorClave(string clave)
    {
        return Productos.FirstOrDefault(p => p.Clave.Equals(clave, StringComparison.OrdinalIgnoreCase));
    }

    public Producto? BuscarPorLinea(PedidoLinea linea)
    {
        return BuscarPorClave(linea.Clave)
            ?? Productos.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(linea.Codigo) &&
                p.Codigo.Equals(linea.Codigo, StringComparison.OrdinalIgnoreCase))
            ?? Productos.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(linea.Nombre) &&
                p.Nombre.Equals(linea.Nombre, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<Producto> Buscar(string texto, int maximo = 12)
    {
        if (string.IsNullOrWhiteSpace(texto) || Productos.Count == 0)
        {
            return [];
        }

        var query = texto.Trim();
        return Productos
            .Select(p => new { Producto = p, Puntos = Puntaje(p, query) })
            .Where(x => x.Puntos > 0)
            .OrderByDescending(x => x.Puntos)
            .ThenBy(x => x.Producto.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .Take(maximo)
            .Select(x => x.Producto)
            .ToList();
    }

    public Producto? BuscarExactoOUnico(string texto)
    {
        var coincidencias = Buscar(texto, 8);
        if (coincidencias.Count == 0)
        {
            return null;
        }

        var exacto = coincidencias.FirstOrDefault(p =>
            p.Codigo.Equals(texto.Trim(), StringComparison.OrdinalIgnoreCase) ||
            p.Nombre.Equals(texto.Trim(), StringComparison.OrdinalIgnoreCase));

        return exacto ?? (coincidencias.Count == 1 ? coincidencias[0] : null);
    }

    public async Task DescontarAsync(IEnumerable<PedidoLinea> lineas)
    {
        foreach (var linea in lineas)
        {
            var producto = BuscarPorLinea(linea)
                ?? throw new InvalidOperationException($"Ya no está en inventario: {linea.Nombre}");

            if (linea.Cantidad > producto.Cantidad)
            {
                throw new InvalidOperationException(
                    $"Ya no hay suficiente de {producto.Nombre}. Solo hay {producto.Cantidad} en inventario.");
            }

            producto.Cantidad -= linea.Cantidad;
        }

        try
        {
            await GuardarAsync();
        }
        catch
        {
            // El descuento ya quedó en memoria; se reintenta al siguiente guardado.
        }

        CatalogoActualizado?.Invoke(this, EventArgs.Empty);
    }

    public async Task GuardarAsync()
    {
        var estado = new CatalogoEstado
        {
            ArchivoOrigen = ArchivoOrigen,
            FechaImportacion = FechaImportacion,
            Productos = [.. Productos]
        };

        await LocalStore.GuardarAsync(NombreArchivo, estado);
    }

    private void AplicarCambio(IReadOnlyList<FilaInventario> filas, IReadOnlyList<string> eliminados, bool reemplazar)
    {
        if (reemplazar)
        {
            AplicarFilas(filas, enVivo: true);
            return;
        }

        foreach (var fila in filas)
        {
            if (string.IsNullOrWhiteSpace(fila.Id))
            {
                continue;
            }

            if (!_porDocumento.TryGetValue(fila.Id, out var producto))
            {
                producto = Productos.FirstOrDefault(p =>
                    MismaFila(p, fila) && !_porDocumento.ContainsValue(p));
                if (producto is null)
                {
                    producto = new Producto();
                    Productos.Add(producto);
                }

                _porDocumento[fila.Id] = producto;
            }

            producto.Codigo = fila.Codigo;
            producto.Area = fila.Area;
            producto.Nombre = fila.Nombre;
            producto.Cantidad = fila.Cantidad;
        }

        foreach (var id in eliminados)
        {
            if (!_porDocumento.TryGetValue(id, out var producto))
            {
                continue;
            }

            Productos.Remove(producto);
            _porDocumento.Remove(id);
        }

        EnVivo = true;
        ArchivoOrigen = "Firebase · inventario en vivo";
        FechaImportacion = DateTimeOffset.Now;
        ErrorNube = null;
        CatalogoActualizado?.Invoke(this, EventArgs.Empty);
        _ = GuardarAsync();
    }

    private void AplicarFilas(IReadOnlyList<FilaInventario> filas, bool enVivo)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fila in filas)
        {
            if (string.IsNullOrWhiteSpace(fila.Id))
            {
                continue;
            }

            ids.Add(fila.Id);
            if (!_porDocumento.TryGetValue(fila.Id, out var producto))
            {
                producto = Productos.FirstOrDefault(p =>
                    MismaFila(p, fila) && !_porDocumento.ContainsValue(p));
                if (producto is null)
                {
                    producto = new Producto();
                    Productos.Add(producto);
                }

                _porDocumento[fila.Id] = producto;
            }

            producto.Codigo = fila.Codigo;
            producto.Area = fila.Area;
            producto.Nombre = fila.Nombre;
            producto.Cantidad = fila.Cantidad;
        }

        foreach (var id in _porDocumento.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            Productos.Remove(_porDocumento[id]);
            _porDocumento.Remove(id);
        }

        if (ids.Count > 0)
        {
            var vivos = new HashSet<Producto>(_porDocumento.Values);
            for (var i = Productos.Count - 1; i >= 0; i--)
            {
                if (!vivos.Contains(Productos[i]))
                {
                    Productos.RemoveAt(i);
                }
            }
        }

        EnVivo = enVivo;
        ArchivoOrigen = enVivo ? "Firebase · inventario en vivo" : "Firebase · inventario";
        FechaImportacion = DateTimeOffset.Now;
        ErrorNube = null;
        CatalogoActualizado?.Invoke(this, EventArgs.Empty);
        _ = GuardarAsync();
    }

    private static bool MismaFila(Producto producto, FilaInventario fila)
    {
        if (!string.IsNullOrWhiteSpace(producto.Codigo) && !string.IsNullOrWhiteSpace(fila.Codigo))
        {
            return producto.Codigo.Equals(fila.Codigo, StringComparison.OrdinalIgnoreCase);
        }

        return producto.Nombre.Equals(fila.Nombre, StringComparison.OrdinalIgnoreCase) &&
               producto.Area.Equals(fila.Area, StringComparison.OrdinalIgnoreCase);
    }

    private void EnUi(Action accion) => _ = EnUiAsync(accion);

    private Task EnUiAsync(Action accion)
    {
        var cola = _ui ?? DispatcherQueue.GetForCurrentThread();
        if (cola is null || cola.HasThreadAccess)
        {
            accion();
            return Task.CompletedTask;
        }

        var listo = new TaskCompletionSource();
        if (!cola.TryEnqueue(() =>
        {
            try
            {
                accion();
                listo.TrySetResult();
            }
            catch (Exception ex)
            {
                listo.TrySetException(ex);
            }
        }))
        {
            accion();
            return Task.CompletedTask;
        }

        return listo.Task;
    }

    private void Reemplazar(IEnumerable<Producto> productos, string archivoOrigen, DateTimeOffset? fecha, bool guardar)
    {
        Productos.Clear();
        foreach (var producto in productos)
        {
            Productos.Add(producto);
        }

        ArchivoOrigen = archivoOrigen;
        FechaImportacion = fecha;
        CatalogoActualizado?.Invoke(this, EventArgs.Empty);
        if (guardar)
        {
            _ = GuardarAsync();
        }
    }

    private static int Puntaje(Producto producto, string query)
    {
        var puntos = 0;
        if (producto.Codigo.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            puntos += 100;
        }
        else if (producto.Codigo.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            puntos += 80;
        }
        else if (producto.Codigo.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            puntos += 40;
        }

        if (producto.Nombre.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            puntos += 90;
        }
        else if (producto.Nombre.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            puntos += 70;
        }
        else if (producto.Nombre.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            puntos += 50;
        }

        if (!string.IsNullOrWhiteSpace(producto.Area) &&
            producto.Area.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            puntos += 25;
        }

        return puntos;
    }
}
