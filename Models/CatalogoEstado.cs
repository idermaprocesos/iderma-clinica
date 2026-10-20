namespace IdermaClinica.Models;

public sealed class CatalogoEstado
{
    public string ArchivoOrigen { get; set; } = string.Empty;
    public DateTimeOffset? FechaImportacion { get; set; }
    public List<Producto> Productos { get; set; } = [];
}

public sealed class PedidosEstado
{
    public int UltimoFolio { get; set; }
    public PedidoBorrador? Borrador { get; set; }
    public List<PedidoHistorial> Historial { get; set; } = [];
}

public sealed class PedidoBorrador
{
    public string NombreLista { get; set; } = string.Empty;
    public DateTimeOffset Fecha { get; set; }
    public List<PedidoLinea> Items { get; set; } = [];
}

public sealed class EnfermerasEstado
{
    public List<Enfermera> Enfermeras { get; set; } = [];
}
