namespace Lighthouse.Core.Entities;

public class Category : BaseEntity
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ColorHex { get; set; }
    public string? Icon { get; set; }
}
