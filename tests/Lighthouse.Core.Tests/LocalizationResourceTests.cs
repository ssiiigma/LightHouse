using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Lighthouse.Core.Tests;

/// <summary>Захист від типових помилок локалізації: розбіжність ключів uk/en і звернення до неіснуючого ключа.</summary>
public class LocalizationResourceTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lighthouse.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Lighthouse.sln not found.");
    }

    private static string MobileDir => Path.Combine(Root(), "src", "Lighthouse.Mobile");

    private static Dictionary<string, string> Load(string file) =>
        XDocument.Load(Path.Combine(MobileDir, "Resources", "Strings", file)).Root!
            .Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? string.Empty);

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(MobileDir, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void UkrainianAndEnglish_HaveTheSameKeys()
    {
        var uk = Load("StringsUk.resx").Keys.OrderBy(k => k).ToList();
        var en = Load("StringsEn.resx").Keys.OrderBy(k => k).ToList();

        Assert.Equal(uk, en);
    }

    [Fact]
    public void UkrainianAndEnglish_HaveTheSamePlaceholders()
    {
        var uk = Load("StringsUk.resx");
        var en = Load("StringsEn.resx");

        foreach (var (key, value) in uk)
        {
            var expected = Regex.Matches(value, @"\{\d+\}").Select(m => m.Value).OrderBy(v => v);
            var actual = Regex.Matches(en[key], @"\{\d+\}").Select(m => m.Value).OrderBy(v => v);
            Assert.True(expected.SequenceEqual(actual), $"Placeholders differ for key {key}.");
        }
    }

    [Fact]
    public void EveryKeyUsedInXamlAndCode_ExistsInResources()
    {
        var keys = Load("StringsUk.resx").Keys.ToHashSet();
        var used = new HashSet<string>();

        foreach (var file in SourceFiles("*.xaml"))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"Translate\s+([A-Za-z0-9_]+)"))
            {
                used.Add(m.Groups[1].Value);
            }
        }

        foreach (var file in SourceFiles("*.cs"))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"loc(?:\.Get\(|\[)""([A-Za-z0-9_]+)"""))
            {
                used.Add(m.Groups[1].Value);
            }
        }

        Assert.NotEmpty(used);
        Assert.Empty(used.Where(k => !keys.Contains(k)));
    }
}