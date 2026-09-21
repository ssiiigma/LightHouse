using Lighthouse.Core.AI;
using Xunit;

namespace Lighthouse.Core.Tests;

public class LinUcbModelTests
{
    [Fact]
    public void NewModel_ScoreEqualsAlphaTimesNormOfContext()
    {
        var model = new LinUcbModel(3, alpha: 1.0);

        var score = model.Score([1, 0, 0]);

        Assert.Equal(1.0, score, precision: 10);
    }

    [Fact]
    public void Update_WithPositiveReward_RaisesExpectedRewardOnlyForSeenContext()
    {
        var model = new LinUcbModel(3, alpha: 0.0);
        double[] seen = [1, 0, 0];
        double[] unseen = [0, 1, 0];

        for (var i = 0; i < 10; i++)
        {
            model.Update(seen, 1.0);
        }

        Assert.True(model.Score(seen) > 0.9);
        Assert.Equal(0.0, model.Score(unseen), precision: 10);
    }

    [Fact]
    public void Update_ReducesUncertaintyForSeenContext()
    {
        var model = new LinUcbModel(3, alpha: 1.0);
        double[] context = [1, 0, 0];
        var before = model.Uncertainty(context);

        for (var i = 0; i < 10; i++)
        {
            model.Update(context, 0.5);
        }

        Assert.True(model.Uncertainty(context) < before / 2);
    }

    [Fact]
    public void Snapshot_RoundTrip_PreservesScores()
    {
        var model = new LinUcbModel(3, alpha: 0.7);
        model.Update([1, 0.5, 0], 1.0);
        model.Update([0, 1, 0.2], 0.0);

        var restored = LinUcbModel.FromSnapshot(model.ToSnapshot());

        double[] probe = [0.3, 0.6, 0.9];
        Assert.Equal(model.Score(probe), restored.Score(probe), precision: 12);
    }

    [Fact]
    public void RankDescending_PutsBetterContextFirst()
    {
        var model = new LinUcbModel(2, alpha: 0.0);
        for (var i = 0; i < 5; i++)
        {
            model.Update([1, 0], 1.0);
            model.Update([0, 1], 0.0);
        }

        var order = model.RankDescending([[0, 1], [1, 0]]);

        Assert.Equal(new[] { 1, 0 }, order.ToArray());
    }

    [Fact]
    public void Score_WithWrongDimension_Throws()
    {
        var model = new LinUcbModel(3);

        Assert.Throws<ArgumentException>(() => model.Score([1, 2]));
    }
}
