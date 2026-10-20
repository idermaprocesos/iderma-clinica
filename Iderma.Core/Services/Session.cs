using Iderma.Core.Models;

namespace Iderma.Core.Services;

public static class Session
{
    public static UserAccount? Current { get; set; }
    public static string? LoginCode { get; set; }
    public static string? Notice { get; set; }

    public static void Clear()
    {
        Current = null;
        LoginCode = null;
    }
}
