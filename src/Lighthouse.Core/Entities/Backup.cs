using Lighthouse.Shared.Enums;

namespace Lighthouse.Core.Entities;

/// <summary>Резервна копія даних користувача: JSON, стиснений gzip.</summary>
public class Backup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public BackupKind Kind { get; set; }
    public long SizeBytes { get; set; }
    public byte[] Content { get; set; } = [];
}