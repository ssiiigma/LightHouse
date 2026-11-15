using System.Xml.Linq;
using Xunit;

namespace Lighthouse.Core.Tests;

/// <summary>Усі коди причин рекомендацій (серверні шаблони) мають текст в обох мовах: клієнт показує їх за кодом.</summary>
public class AdviceLocalizationTests
{
    private static readonly string[] ReasonCodes =
    [
        "priority", "overdue", "deadline_within_day", "deadline_approaching", "duration",
        "usual_time", "usual_weekday", "usual_category", "postponed", "has_reminder"
    ];

    private static HashSet<string> Keys(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lighthouse.sln")))
        {
            dir = dir.Parent;
        }

        var path = Path.Combine(dir!.FullName, "src", "Lighthouse.Mobile", "Resources", "Strings", file);
        return XDocument.Load(path).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
    }

    [Theory]
    [InlineData("StringsUk.resx")]
    [InlineData("StringsEn.resx")]
    public void EveryReasonCode_HasText(string file)
    {
        var keys = Keys(file);
        var missing = ReasonCodes.Where(c => !keys.Contains($"Reason_{c}")).ToList();

        Assert.True(missing.Count == 0, "Немає текстів причин: " + string.Join(", ", missing));
    }
}