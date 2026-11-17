using Lighthouse.Shared.Categories;
using Xunit;

namespace Lighthouse.Core.Tests;

public class CategoryNamesTests
{
    [Theory]
    [InlineData("Study", "study", true)]
    [InlineData("  Study ", "Study", true)]
    [InlineData("Home  work", "home work", true)]
    [InlineData("Навчання", "навчання", true)]
    [InlineData("Study", "Sport", false)]
    [InlineData("", "  ", true)]
    public void IsSame_FollowsRules(string first, string second, bool expected)
    {
        Assert.Equal(expected, CategoryNames.IsSame(first, second));
    }

    [Fact]
    public void Normalize_TrimsAndCollapsesSpaces()
    {
        Assert.Equal("Home work", CategoryNames.Normalize("  Home   work "));
        Assert.Equal(string.Empty, CategoryNames.Normalize(null));
    }
}