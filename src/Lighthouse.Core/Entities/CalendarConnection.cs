namespace Lighthouse.Core.Entities;

/// <summary>Підключення Google Calendar (зв'язок 1:1 з користувачем; ключ збігається з UserId).</summary>
public class CalendarConnection
{
    public Guid UserId { get; set; }
    public string GoogleAccountEmail { get; set; } = string.Empty;

    /// <summary>Refresh-токен Google, зашифрований засобами ASP.NET Core Data Protection.</summary>
    public string RefreshTokenProtected { get; set; } = string.Empty;
    public string CalendarId { get; set; } = "primary";
    public bool IsEnabled { get; set; } = true;
    public DateTime ConnectedAtUtc { get; set; } = DateTime.UtcNow;
}