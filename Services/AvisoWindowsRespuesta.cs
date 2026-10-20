using Iderma.Core.Services;
using IdermaClinica.Models;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace IdermaClinica.Services;

public static class AvisoWindowsRespuesta
{
    private static bool _registrado;
    private static DispatcherQueue? _ui;

    public static void Preparar(DispatcherQueue? ui = null)
    {
        if (ui is not null)
        {
            _ui = ui;
        }

        if (_registrado)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, _) =>
                EnUi(() => App.MainAppWindow?.IrANotificaciones());
            AppNotificationManager.Default.Register();
            _registrado = true;
        }
        catch
        {
            _registrado = false;
        }
    }

    public static void Mostrar(NotificacionClinica aviso)
    {
        ArgumentNullException.ThrowIfNull(aviso);
        if (!NotificationPrefs.AllowClinica())
        {
            return;
        }

        try
        {
            Preparar();
            var toast = new AppNotificationBuilder()
                .AddText(aviso.Titulo)
                .AddText(aviso.Mensaje)
                .AddText(aviso.Motivo)
                .SetAudioEvent(aviso.Estado == EstadoPedido.Denegado
                    ? AppNotificationSoundEvent.Reminder
                    : AppNotificationSoundEvent.Default)
                .BuildNotification();
            AppNotificationManager.Default.Show(toast);
        }
        catch
        {
        }
    }

    public static void Mostrar(PedidoHistorial pedido)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var aceptado = pedido.Estado == EstadoPedido.Aceptado;
        Mostrar(new NotificacionClinica
        {
            PedidoId = pedido.Id,
            Folio = pedido.Folio,
            Estado = pedido.Estado,
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
            Fecha = pedido.FechaAtendido ?? DateTimeOffset.Now
        });
    }

    private static void EnUi(Action accion)
    {
        var cola = _ui ?? App.MainAppWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (cola is null || cola.HasThreadAccess)
        {
            accion();
            return;
        }

        cola.TryEnqueue(() => accion());
    }
}
