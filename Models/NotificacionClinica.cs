using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace IdermaClinica.Models;

public sealed class NotificacionClinica : INotifyPropertyChanged
{
    private bool _leida;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PedidoId { get; set; } = string.Empty;
    public string Folio { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
    public EstadoPedido Estado { get; set; } = EstadoPedido.Enviado;
    public DateTimeOffset Fecha { get; set; } = DateTimeOffset.Now;

    public bool Leida
    {
        get => _leida;
        set
        {
            if (_leida == value)
            {
                return;
            }

            _leida = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public string FechaTexto => Fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    [JsonIgnore]
    public string EstadoTexto => Estado switch
    {
        EstadoPedido.Aceptado => "Aceptado",
        EstadoPedido.Denegado => "Denegado",
        _ => "Enviado"
    };

    [JsonIgnore]
    public bool EstaAceptado => Estado == EstadoPedido.Aceptado;

    [JsonIgnore]
    public bool EstaDenegado => Estado == EstadoPedido.Denegado;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
