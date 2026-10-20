using System.Text.Json.Serialization;

namespace IdermaClinica.Models;

public sealed class PedidoLinea
{
    public string Codigo { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Cantidad { get; set; }

    public int? CantidadRespondida { get; set; }

    [JsonIgnore]
    public string Clave => !string.IsNullOrWhiteSpace(Codigo)
        ? $"c:{Codigo.Trim()}"
        : $"n:{Area.Trim()}|{Nombre.Trim()}";

    [JsonIgnore]
    public string Texto => $"{Codigo}  {Nombre}  x{Cantidad}".Trim();
}
