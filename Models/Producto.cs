using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using IdermaClinica;

namespace IdermaClinica.Models;

public sealed class Producto : INotifyPropertyChanged
{
    private string _codigo = string.Empty;
    private string _area = string.Empty;
    private string _nombre = string.Empty;
    private int _cantidad;

    public string Codigo
    {
        get => _codigo;
        set { if (_codigo == value) return; _codigo = value; OnPropertyChanged(); OnPropertyChanged(nameof(Clave)); }
    }

    public string Area
    {
        get => _area;
        set { if (_area == value) return; _area = value; OnPropertyChanged(); OnPropertyChanged(nameof(Clave)); OnPropertyChanged(nameof(Subtitulo)); }
    }

    public string Nombre
    {
        get => _nombre;
        set { if (_nombre == value) return; _nombre = value; OnPropertyChanged(); OnPropertyChanged(nameof(Clave)); OnPropertyChanged(nameof(Subtitulo)); }
    }

    public int Cantidad
    {
        get => _cantidad;
        set
        {
            var nueva = Math.Max(0, value);
            if (_cantidad == nueva)
            {
                return;
            }

            _cantidad = nueva;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CantidadTexto));
            OnPropertyChanged(nameof(Subtitulo));
            OnPropertyChanged(nameof(HayExistencia));
            OnPropertyChanged(nameof(FondoInventario));
        }
    }

    [JsonIgnore]
    public bool HayExistencia => Cantidad > 0;

    [JsonIgnore]
    public string CantidadTexto => Cantidad.ToString();

    [JsonIgnore]
    public Microsoft.UI.Xaml.Media.SolidColorBrush FondoInventario => InventarioColores.De(Cantidad);

    [JsonIgnore]
    public string Clave => !string.IsNullOrWhiteSpace(Codigo)
        ? $"c:{Codigo.Trim()}"
        : $"n:{Area.Trim()}|{Nombre.Trim()}";

    [JsonIgnore]
    public string Subtitulo
    {
        get
        {
            var partes = new List<string>();
            if (!string.IsNullOrWhiteSpace(Codigo))
            {
                partes.Add(Codigo);
            }

            if (!string.IsNullOrWhiteSpace(Area))
            {
                partes.Add(Area);
            }

            partes.Add(Cantidad <= 0 ? "Ya no hay" : $"Disponible {Cantidad}");
            return string.Join(" · ", partes);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
