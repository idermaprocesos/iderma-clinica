using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace IdermaClinica.Models;

public sealed class PedidoItem : INotifyPropertyChanged
{
    private int _cantidad;

    public PedidoItem(Producto producto, int cantidad)
    {
        Producto = producto;
        _cantidad = Math.Max(0, cantidad);
        Producto.PropertyChanged += Producto_PropertyChanged;
    }

    public Producto Producto { get; }

    public string Codigo => Producto.Codigo;
    public string Area => Producto.Area;
    public string Nombre => Producto.Nombre;

    [JsonIgnore]
    public int StockDisponible => Producto.Cantidad;

    [JsonIgnore]
    public string StockTexto => Producto.Cantidad <= 0 ? "Ya no hay" : Producto.Cantidad.ToString();

    [JsonIgnore]
    public Microsoft.UI.Xaml.Media.SolidColorBrush FondoInventario => Producto.FondoInventario;

    public int Cantidad
    {
        get => _cantidad;
        set
        {
            var nueva = Math.Clamp(value, 0, Math.Max(0, Producto.Cantidad));
            if (_cantidad == nueva)
            {
                return;
            }

            _cantidad = nueva;
            OnPropertyChanged();
        }
    }

    public PedidoLinea ALinea() => new()
    {
        Codigo = Codigo,
        Area = Area,
        Nombre = Nombre,
        Cantidad = Cantidad
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Producto_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Producto.Cantidad) or nameof(Producto.CantidadTexto) or nameof(Producto.Subtitulo) or nameof(Producto.FondoInventario))
        {
            if (_cantidad > Producto.Cantidad)
            {
                Cantidad = Producto.Cantidad;
            }

            OnPropertyChanged(nameof(StockDisponible));
            OnPropertyChanged(nameof(StockTexto));
            OnPropertyChanged(nameof(FondoInventario));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
