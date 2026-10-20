namespace Iderma.Core.Models;

public sealed class SecurityPolicy
{
    public int IdleLockMinutes { get; set; }
    public bool LockoutEnabled { get; set; }
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 5;
}

public static class SecurityEventKind
{
    public const string SignIn = "sign-in";
    public const string SignOut = "sign-out";
    public const string SignInFailed = "sign-in-failed";
    public const string Lockout = "lockout";
    public const string PasswordChanged = "password-changed";
    public const string PolicyChanged = "policy-changed";
    public const string SessionLocked = "session-locked";
    public const string SessionUnlocked = "session-unlocked";
    public const string ForcedSignOut = "forced-sign-out";
}

public sealed class SecurityEvent
{
    public DateTime At { get; set; } = DateTime.Now;
    public string Kind { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}
