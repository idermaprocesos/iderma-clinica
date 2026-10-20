using Iderma.Core.Models;

namespace Iderma.Core.Services;

/// <summary>
/// Lee la sesión activa y el perfil cifrado de cuentas para saber si ese usuario
/// puede recibir notificaciones de logística o clínica.
/// </summary>
public static class NotificationPrefs
{
    public static bool AllowLogistica() => Allow(static u => u.NotifyLogistica);

    public static bool AllowClinica() => Allow(static u => u.NotifyClinica);

    public static bool AllowBaseDeDatos() => Allow(static u => u.NotifyBaseDeDatos);

    private static bool Allow(Func<UserAccount, bool> selector)
    {
        try
        {
            using var sessions = new SessionStore();
            var snap = sessions.Snapshot();
            if (string.IsNullOrWhiteSpace(snap.UserId))
            {
                // Sin sesión en el launcher: no bloquear (p. ej. depuración directa).
                return true;
            }

            using var users = new UserStore();
            var user = users.FindById(snap.UserId);
            return user is null || selector(user);
        }
        catch
        {
            return true;
        }
    }
}
