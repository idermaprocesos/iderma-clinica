using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace IdermaClinica.Models;

public enum EstadoPedido
{
    Enviado,
    Atendido,
    Aceptado,
    Denegado
}

public sealed class PedidoHistorial : INotifyPropertyChanged
{
    private EstadoPedido _estado = EstadoPedido.Enviado;
    private DateTimeOffset? _fechaAtendido;
    private string _motivoRespuesta = string.Empty;
    private bool _cantidadAcordada;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Folio { get; set; } = string.Empty;
    public string Enfermera { get; set; } = string.Empty;
    public DateTimeOffset Fecha { get; set; } = DateTimeOffset.Now;
    public List<PedidoLinea> Items { get; set; } = [];

    public DateTimeOffset? FechaAtendido
    {
        get => _fechaAtendido;
        set
        {
            if (_fechaAtendido == value)
            {
                return;
            }

            _fechaAtendido = value;
            OnPropertyChanged();
        }
    }

    public string MotivoRespuesta
    {
        get => _motivoRespuesta;
        set
        {
            var texto = value ?? string.Empty;
            if (_motivoRespuesta == texto)
            {
                return;
            }

            _motivoRespuesta = texto;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DetalleRespuesta));
        }
    }

    public bool CantidadAcordada
    {
        get => _cantidadAcordada;
        set
        {
            if (_cantidadAcordada == value)
            {
                return;
            }

            _cantidadAcordada = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Detalle));
            OnPropertyChanged(nameof(DetalleRespuesta));
        }
    }

    public EstadoPedido Estado
    {
        get => _estado;
        set
        {
            if (_estado == value)
            {
                return;
            }

            _estado = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EstadoTexto));
            OnPropertyChanged(nameof(EstaEnviado));
            OnPropertyChanged(nameof(EstaAtendido));
            OnPropertyChanged(nameof(EstaAceptado));
            OnPropertyChanged(nameof(EstaDenegado));
            OnPropertyChanged(nameof(Detalle));
            OnPropertyChanged(nameof(DetalleRespuesta));
        }
    }

    public bool Urgente { get; set; }

    [JsonIgnore]
    public string FechaTexto => Fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    [JsonIgnore]
    public string EstadoTexto => Estado switch
    {
        EstadoPedido.Atendido => "Atendido",
        EstadoPedido.Aceptado => "Aceptado",
        EstadoPedido.Denegado => "Denegado",
        _ => "Enviado"
    };

    [JsonIgnore]
    public bool EstaEnviado => Estado == EstadoPedido.Enviado;

    [JsonIgnore]
    public bool EstaAtendido => Estado == EstadoPedido.Atendido;

    [JsonIgnore]
    public bool EstaAceptado => Estado == EstadoPedido.Aceptado;

    [JsonIgnore]
    public bool EstaDenegado => Estado == EstadoPedido.Denegado;

    [JsonIgnore]
    public string DetalleRespuesta
    {
        get
        {
            if (Estado == EstadoPedido.Aceptado && CantidadAcordada)
            {
                return string.IsNullOrWhiteSpace(MotivoRespuesta)
                    ? "Logística aceptó con cantidad acordada."
                    : "Logística aceptó con cantidad acordada: " + MotivoRespuesta;
            }

            if (Estado == EstadoPedido.Aceptado)
            {
                return "Logística aceptó la solicitud.";
            }

            if (Estado == EstadoPedido.Denegado)
            {
                return string.IsNullOrWhiteSpace(MotivoRespuesta)
                    ? "Logística denegó la solicitud."
                    : "Logística denegó: " + MotivoRespuesta;
            }

            return string.Empty;
        }
    }

    [JsonIgnore]
    public int TotalCantidad => Items.Sum(i => i.Cantidad);

    [JsonIgnore]
    public string Resumen => $"{Enfermera}  ·  {Items.Count} {(Items.Count == 1 ? "producto" : "productos")}  ·  {TotalCantidad} pzas";

    [JsonIgnore]
    public string Detalle => string.Join(Environment.NewLine, Items.Select(TextoLinea));

    public void AplicarLineasRespondidas(IReadOnlyList<(string Codigo, string Nombre, int Cantidad)> lineas)
    {
        if (lineas.Count == 0)
        {
            return;
        }

        var usadas = new bool[lineas.Count];
        foreach (var item in Items)
        {
            var indice = -1;
            for (var i = 0; i < lineas.Count; i++)
            {
                if (usadas[i])
                {
                    continue;
                }

                var mismoCodigo = !string.IsNullOrWhiteSpace(item.Codigo) &&
                                  item.Codigo.Equals(lineas[i].Codigo, StringComparison.OrdinalIgnoreCase);
                var mismoNombre = !string.IsNullOrWhiteSpace(item.Nombre) &&
                                  item.Nombre.Equals(lineas[i].Nombre, StringComparison.OrdinalIgnoreCase);
                if (mismoCodigo || (string.IsNullOrWhiteSpace(item.Codigo) && mismoNombre))
                {
                    indice = i;
                    break;
                }
            }

            if (indice < 0)
            {
                continue;
            }

            usadas[indice] = true;
            item.CantidadRespondida = lineas[indice].Cantidad;
        }

        OnPropertyChanged(nameof(Detalle));
    }

    private string TextoLinea(PedidoLinea linea)
    {
        var baseTexto = $"{linea.Codigo}  {linea.Nombre}".Trim();
        if (linea.CantidadRespondida is not int respondida)
        {
            return linea.Texto;
        }

        if (Estado == EstadoPedido.Denegado)
        {
            return $"{baseTexto}  pedidas x{linea.Cantidad} · denegadas x{respondida}".Trim();
        }

        if (CantidadAcordada)
        {
            return $"{baseTexto}  pedidas x{linea.Cantidad} · acordadas x{respondida}".Trim();
        }

        return linea.Texto;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
