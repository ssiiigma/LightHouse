using Xunit;

namespace Lighthouse.Core.Tests;

/// <summary>Шрифти для PDF-звітів мають бути в проєкті API (інакше кирилиця не відобразиться).</summary>
public class ReportFontTests
{
    [Theory]
    [InlineData("NotoSans-Regular.ttf")]
    [InlineData("NotoSans-Bold.ttf")]
    public void ReportFont_ExistsInApiProject(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lighthouse.sln")))
        {
            dir = dir.Parent;
        }

        var path = Path.Combine(dir!.FullName, "src", "Lighthouse.Api", "Fonts", file);
        Assert.True(File.Exists(path), $"Немає шрифту {path}");
    }
}