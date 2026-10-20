namespace Iderma.Core.Models;

public sealed class AppDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required AppStatus Status { get; init; }
    public required string LogoFileName { get; init; }
    public bool IsLocked { get; init; }
}
