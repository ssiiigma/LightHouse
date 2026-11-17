namespace Lighthouse.Shared.Categories;

/// <summary>Правила порівняння назв категорій: регістр не враховується, пробіли по краях і повторні пробіли всередині ігноруються.</summary>
public static class CategoryNames
{
    public static string Normalize(string? name) =>
        string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static bool IsSame(string? first, string? second) =>
        string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);

    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;
}