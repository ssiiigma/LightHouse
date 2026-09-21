namespace Lighthouse.Core.Entities;

/// <summary>Збережений стан персональної моделі LinUCB (серіалізований <c>LinUcbSnapshot</c>).</summary>
public class BanditState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public int Dimension { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
