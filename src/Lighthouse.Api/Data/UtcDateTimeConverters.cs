using Lighthouse.Api.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Lighthouse.Api.Data;

/// <summary>SQL Server повертає datetime2 з Kind=Unspecified; конвертер гарантує UTC, тож JSON міститиме «Z».</summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(v => SyncTime.ToUtc(v), v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}

public sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter()
        : base(
            v => SyncTime.ToUtc(v),
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
    {
    }
}