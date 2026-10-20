using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IdermaClinica.Models;

public sealed class Enfermera : INotifyPropertyChanged
{
    private string _nombre = string.Empty;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Nombre
    {
        get => _nombre;
        set
        {
            var nuevo = value.Trim();
            if (_nombre == nuevo)
            {
                return;
            }

            _nombre = nuevo;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
