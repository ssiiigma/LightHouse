namespace Lighthouse.Api.Options;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>Порожній — відправлення вимкнено.</summary>
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1025;
    public bool UseTls { get; set; }
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "no-reply@lighthouse.local";
    public string FromName { get; set; } = "Lighthouse";
}

public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Адреса, за якою відкриваються посилання з листів (публічна адреса API).</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5080";
}