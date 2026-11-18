namespace Lighthouse.Api.Infrastructure;

/// <summary>Захист від CSV-ін'єкції при експорті й зворотне перетворення при імпорті.</summary>
public static class CsvText
{
    private static readonly char[] Dangerous = ['=', '+', '-', '@', '\t', '\r'];

    public static string Safe(string value) =>
        value.Length > 0 && Array.IndexOf(Dangerous, value[0]) >= 0 ? "'" + value : value;

    public static string Restore(string value) =>
        value.Length > 1 && value[0] == '\'' && Array.IndexOf(Dangerous, value[1]) >= 0 ? value[1..] : value;
}