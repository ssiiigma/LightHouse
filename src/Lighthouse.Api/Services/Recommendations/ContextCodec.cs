using System.Text.Json;
using Lighthouse.Core.AI;

namespace Lighthouse.Api.Services.Recommendations;

/// <summary>Серіалізація вектора контексту для RecommendationFeedback.ContextJson.</summary>
public static class ContextCodec
{
    public static string Serialize(double[] context) => JsonSerializer.Serialize(context);

    /// <summary>null, якщо JSON пошкоджений або розмірність не збігається з моделлю.</summary>
    public static double[]? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var context = JsonSerializer.Deserialize<double[]>(json);
            return context is { Length: LinUcbDefaults.Dimension } ? context : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}