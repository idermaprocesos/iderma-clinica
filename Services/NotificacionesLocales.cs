using System.Collections.ObjectModel;
using IdermaClinica.Models;

namespace IdermaClinica.Services;

public sealed class NotificacionesLocales
{
    private const string Archivo = "notificaciones-clinica.json";

    public static NotificacionesLocales Instance { get; } = new();

    private NotificacionesLocales()
    {
    }

    public ObservableCollection<NotificacionClinica> Items { get; } = [];

    public event EventHandler? Cambiado;

    public async Task CargarAsync()
    {
        try
        {
            var lista = await LocalStore.LeerAsync<List<NotificacionClinica>>(Archivo) ?? [];
            Items.Clear();
            foreach (var item in lista.OrderByDescending(n => n.Fecha))
            {
                Items.Add(item);
            }

            // Si aún no hay archivo local, arma el historial desde pedidos ya respondidos.
            if (Items.Count == 0)
            {
                foreach (var pedido in PedidoService.Instance.Historial
                             .Where(p => p.Estado is EstadoPedido.Aceptado or EstadoPedido.Denegado)
                             .OrderByDescending(p => p.FechaAtendido ?? p.Fecha))
                {
                    await RegistrarRespuestaAsync(pedido);
                }
            }

            Cambiado?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            Items.Clear();
        }
    }

    public async Task<NotificacionClinica?> RegistrarRespuestaAsync(PedidoHistorial pedido)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        if (pedido.Estado is not (EstadoPedido.Aceptado or EstadoPedido.Denegado))
        {
            return null;
        }

        var existente = Items.FirstOrDefault(n =>
            n.PedidoId.Equals(pedido.Id, StringComparison.OrdinalIgnoreCase) &&
            n.Estado == pedido.Estado);
        if (existente is not null)
        {
            return null;
        }

        var aceptado = pedido.Estado == EstadoPedido.Aceptado;
        var aviso = new NotificacionClinica
        {
            PedidoId = pedido.Id,
            Folio = pedido.Folio,
            Estado = pedido.Estado,
            Fecha = pedido.FechaAtendido ?? DateTimeOffset.Now,
            Titulo = !aceptado
                ? "Logística denegó tu solicitud"
                : pedido.CantidadAcordada
                    ? "Logística aceptó con cantidad acordada"
                    : "Logística aceptó tu solicitud",
            Mensaje = $"{pedido.Folio} · {pedido.Enfermera} · {pedido.Detalle}",
            Motivo = aceptado && !pedido.CantidadAcordada
                ? string.Empty
                : (string.IsNullOrWhiteSpace(pedido.MotivoRespuesta)
                    ? "Sin motivo indicado."
                    : pedido.MotivoRespuesta),
            Leida = false
        };

        Items.Insert(0, aviso);
        await GuardarAsync();
        Cambiado?.Invoke(this, EventArgs.Empty);
        return aviso;
    }

    public async Task MarcarLeidaAsync(NotificacionClinica aviso)
    {
        if (aviso.Leida)
        {
            return;
        }

        aviso.Leida = true;
        await GuardarAsync();
        Cambiado?.Invoke(this, EventArgs.Empty);
    }

    public async Task MarcarTodasLeidasAsync()
    {
        var hubo = false;
        foreach (var item in Items.Where(n => !n.Leida))
        {
            item.Leida = true;
            hubo = true;
        }

        if (!hubo)
        {
            return;
        }

        await GuardarAsync();
        Cambiado?.Invoke(this, EventArgs.Empty);
    }

    private Task GuardarAsync() => LocalStore.GuardarAsync(Archivo, Items.ToList());
}
