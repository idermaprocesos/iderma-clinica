namespace Iderma.Core.Models;

public sealed class LoginRecord
{
    public string Code { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime At { get; set; } = DateTime.Now;
    public string MachineName { get; set; } = string.Empty;
    public string WindowsAccount { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string Culture { get; set; } = string.Empty;
}
