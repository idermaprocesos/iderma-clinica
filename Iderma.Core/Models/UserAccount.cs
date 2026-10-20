namespace Iderma.Core.Models;

public sealed class UserAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PhotoPath { get; set; } = string.Empty;
    public string Role { get; set; } = "Usuario";
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public int PasswordIterations { get; set; }
    public bool MustChangePassword { get; set; }
    public bool IsActive { get; set; } = true;
    public bool CanOpenSettings { get; set; } = true;
    public bool CanExportLogins { get; set; } = true;
    public bool NotifyLogistica { get; set; } = true;
    public bool NotifyClinica { get; set; } = true;
    public bool NotifyBaseDeDatos { get; set; } = true;
    public List<string> AppIds { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
