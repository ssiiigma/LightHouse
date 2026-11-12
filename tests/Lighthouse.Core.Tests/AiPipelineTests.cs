using Lighthouse.Core.AI;
using Lighthouse.Core.Entities;
using Lighthouse.Shared.Enums;
using Xunit;

namespace Lighthouse.Core.Tests;

public class AiPipelineTests
{
    // 10 листопада 2026 — вівторок.
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static TaskItem Task(TaskPriority priority, DateTime? deadline = null) =>
        new() { Priority = priority, DeadlineUtc = deadline };

    // ---------- Ознаки ----------

    [Fact]
    public void Extract_BuildsExpectedVector()
    {
        var task = new TaskItem
        {
            Priority = TaskPriority.High,
            DeadlineUtc = Now.AddHours(24),
            EstimatedMinutes = 120,
            ReminderAtUtc = Now.AddHours(1)
        };
        var history = new UserHistory { PostponeCounts = new Dictionary<Guid, int> { [task.Id] = 2 } };

        var x = TaskFeatureExtractor.Extract(task, history, Now, Utc);

        Assert.Equal(10, x.Length);
        Assert.Equal(1.0, x[FeatureIndex.Bias]);
        Assert.Equal(0.67, x[FeatureIndex.Priority], precision: 10);
        Assert.Equal(0.5, x[FeatureIndex.Urgency], precision: 10);   // 1 / (1 + 24/24)
        Assert.Equal(0.0, x[FeatureIndex.Overdue]);
        Assert.Equal(0.5, x[FeatureIndex.Estimate], precision: 10);  // 120 / 240
        Assert.Equal(0.5, x[FeatureIndex.HourBucketRate], precision: 10);
        Assert.Equal(0.5, x[FeatureIndex.WeekDayRate], precision: 10);
        Assert.Equal(0.5, x[FeatureIndex.CategoryRate], precision: 10);
        Assert.Equal(0.4, x[FeatureIndex.Postpone], precision: 10);  // 2 / 5
        Assert.Equal(1.0, x[FeatureIndex.Reminder]);
        Assert.All(x, v => Assert.InRange(v, 0.0, 1.0));
    }

    [Fact]
    public void Extract_OverdueTask_HasMaxUrgencyAndOverdueFlag()
    {
        var x = TaskFeatureExtractor.Extract(Task(TaskPriority.Low, Now.AddHours(-5)), UserHistory.Empty, Now, Utc);

        Assert.Equal(1.0, x[FeatureIndex.Urgency]);
        Assert.Equal(1.0, x[FeatureIndex.Overdue]);
    }

    [Fact]
    public void Extract_NoDeadline_HasZeroUrgencyAndEstimateIsCapped()
    {
        var task = new TaskItem { EstimatedMinutes = 1000 };

        var x = TaskFeatureExtractor.Extract(task, UserHistory.Empty, Now, Utc);

        Assert.Equal(0.0, x[FeatureIndex.Urgency]);
        Assert.Equal(0.0, x[FeatureIndex.Overdue]);
        Assert.Equal(1.0, x[FeatureIndex.Estimate]);
    }

    [Fact]
    public void Extract_UsesSmoothedHistoryRates()
    {
        var categoryId = Guid.NewGuid();
        var hourBuckets = new OutcomeCount[UserHistory.HourBuckets];
        hourBuckets[3] = new OutcomeCount(3, 4);                 // 12:00 → інтервал 12–16
        var weekDays = new OutcomeCount[UserHistory.WeekDays];
        weekDays[1] = new OutcomeCount(1, 1);                    // вівторок
        var history = new UserHistory
        {
            ByHourBucket = hourBuckets,
            ByWeekDay = weekDays,
            ByCategory = new Dictionary<Guid, OutcomeCount> { [categoryId] = new OutcomeCount(0, 2) }
        };
        var task = new TaskItem { CategoryId = categoryId };

        var x = TaskFeatureExtractor.Extract(task, history, Now, Utc);

        Assert.Equal(4.0 / 6.0, x[FeatureIndex.HourBucketRate], precision: 10);  // (3+1)/(4+2)
        Assert.Equal(2.0 / 3.0, x[FeatureIndex.WeekDayRate], precision: 10);     // (1+1)/(1+2)
        Assert.Equal(0.25, x[FeatureIndex.CategoryRate], precision: 10);         // (0+1)/(2+2)
    }

    // ---------- Евристика ----------

    [Fact]
    public void Heuristic_ScoreFollowsFormula()
    {
        var x = new double[10];
        x[FeatureIndex.Priority] = 0.67;
        x[FeatureIndex.Urgency] = 0.5;
        x[FeatureIndex.Overdue] = 1.0;

        Assert.Equal(0.45 * 0.67 + 0.45 * 0.5 + 0.10, HeuristicScorer.Score(x), precision: 10);
    }

    // ---------- Обмеження ----------

    [Theory]
    [InlineData(null, 5)]
    [InlineData(0.0, 3)]
    [InlineData(1.0, 3)]
    [InlineData(4.0, 6)]
    [InlineData(3.5, 6)]
    public void DailyLimit_FollowsSpecification(double? median, int expected)
    {
        Assert.Equal(expected, ConstraintChecker.DailyLimit(median));
    }

    [Fact]
    public void Deferrals_PickLowestScoredEligibleTasks_NeverLayer1_NeverNearDeadline()
    {
        var layer1 = new ConstraintTask(Guid.NewGuid(), 0.9, true, Now.AddHours(-1), null);
        var nearDeadline = new ConstraintTask(Guid.NewGuid(), 0.05, false, Now.AddHours(30), null); // до 11.11 20:00
        var low = new ConstraintTask(Guid.NewGuid(), 0.10, false, null, null);
        var mid = new ConstraintTask(Guid.NewGuid(), 0.40, false, Now.AddDays(5), null);
        var high = new ConstraintTask(Guid.NewGuid(), 0.70, false, Now.AddDays(6), null);
        var boundary = new ConstraintTask(Guid.NewGuid(), 0.20, false, new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc), null);

        var result = ConstraintChecker.Evaluate(
            [layer1, nearDeadline, low, mid, high, boundary], dailyLimit: 3, Now, Utc);

        // Надлишок = 6 − 3 = 3; придатні: low (0.10), boundary (0.20), mid (0.40), high (0.70).
        Assert.Equal(new[] { low.Id, boundary.Id, mid.Id }, result.DeferralSuggestions.ToArray());
    }

    [Fact]
    public void Deferrals_AreEmpty_WhenWithinLimit()
    {
        var tasks = new[] { new ConstraintTask(Guid.NewGuid(), 0.1, false, null, null) };

        Assert.Empty(ConstraintChecker.Evaluate(tasks, dailyLimit: 5, Now, Utc).DeferralSuggestions);
    }

    [Fact]
    public void DoNotFit_UsesCumulativeEstimateInDeadlineOrder_AndIgnoresOverdue()
    {
        var a = new ConstraintTask(Guid.NewGuid(), 0.5, false, Now.AddHours(2), 60);   // 60 ≤ 120 — вкладається
        var b = new ConstraintTask(Guid.NewGuid(), 0.5, false, Now.AddHours(3), 130);  // 190 > 180 — ні
        var overdue = new ConstraintTask(Guid.NewGuid(), 0.9, true, Now.AddHours(-2), 500);

        var result = ConstraintChecker.Evaluate([overdue, b, a], dailyLimit: 10, Now, Utc);

        Assert.Equal(new[] { b.Id }, result.DoNotFit.ToArray());
    }

    // ---------- Пояснення ----------

    [Fact]
    public void Explanation_TakesTwoLargestPositiveContributions_IgnoringBias()
    {
        var x = new double[10];
        x[FeatureIndex.Urgency] = 0.8;
        var contributions = new double[10];
        contributions[FeatureIndex.Bias] = 9.0;
        contributions[FeatureIndex.Priority] = 0.1;
        contributions[FeatureIndex.Urgency] = 0.3;
        contributions[FeatureIndex.Overdue] = -0.5;
        contributions[FeatureIndex.Postpone] = 0.2;

        var explanation = ExplanationBuilder.Build(contributions, x, uncertaintyBonus: 0.2, expectedReward: 0.5);

        Assert.Equal(new[] { "deadline_within_day", "postponed" }, explanation.Reasons.Select(r => r.Code).ToArray());
        Assert.Equal("до дедлайну менше доби", explanation.Reasons[0].Text);
        Assert.Equal(0.2, explanation.UncertaintyBonus);
        Assert.False(explanation.IsExploration);
    }

    [Fact]
    public void Explanation_OverdueAndUrgencyDoNotDuplicateReason()
    {
        var x = new double[10];
        x[FeatureIndex.Urgency] = 1.0;
        x[FeatureIndex.Overdue] = 1.0;
        var contributions = HeuristicScorer.Contributions(x);

        var explanation = ExplanationBuilder.Build(contributions, x, 0.0, 0.55);

        Assert.Single(explanation.Reasons);
        Assert.Equal("overdue", explanation.Reasons[0].Code);
    }

    [Fact]
    public void Explanation_WithNoPositiveContributions_HasNoReasons_AndFlagsExploration()
    {
        var explanation = ExplanationBuilder.Build(new double[10], new double[10], uncertaintyBonus: 0.4, expectedReward: 0.0);

        Assert.Empty(explanation.Reasons);
        Assert.True(explanation.IsExploration);
    }

    // ---------- Винагорода ----------

    [Fact]
    public void Reward_ForFeedback_IsOneOnlyForAcceptedOrCompleted()
    {
        Assert.Equal(1.0, RewardCalculator.ForFeedback(FeedbackKind.Accepted));
        Assert.Equal(1.0, RewardCalculator.ForFeedback(FeedbackKind.Completed));
        Assert.Equal(0.0, RewardCalculator.ForFeedback(FeedbackKind.Rejected));
        Assert.Equal(0.0, RewardCalculator.ForFeedback(FeedbackKind.Postponed));
    }

    [Fact]
    public void Reward_ManualPostponement_WithDeadline()
    {
        var deadline = Now.AddDays(2);

        Assert.Equal(1.0, RewardCalculator.ForManualPostponement(Now, deadline, Now.AddDays(1), Now.AddDays(1)));
        Assert.Equal(0.0, RewardCalculator.ForManualPostponement(Now, deadline, Now.AddDays(3), Now.AddDays(3)));
        Assert.Equal(0.0, RewardCalculator.ForManualPostponement(Now, deadline, null, Now.AddDays(3)));
        Assert.Null(RewardCalculator.ForManualPostponement(Now, deadline, null, Now.AddDays(1)));
    }

    [Fact]
    public void Reward_ManualPostponement_WithoutDeadline_UsesThreeDayHorizon()
    {
        Assert.Equal(1.0, RewardCalculator.ForManualPostponement(Now, null, Now.AddDays(2), Now.AddDays(2)));
        Assert.Equal(0.0, RewardCalculator.ForManualPostponement(Now, null, Now.AddDays(4), Now.AddDays(4)));
        Assert.Equal(0.0, RewardCalculator.ForManualPostponement(Now, null, null, Now.AddDays(3).AddMinutes(1)));
        Assert.Null(RewardCalculator.ForManualPostponement(Now, null, null, Now.AddDays(2)));
    }

    // ---------- Перерахунок моделі ----------

    [Fact]
    public void Recompute_WithUnitWeights_MatchesSequentialShermanMorrison()
    {
        var random = new Random(42);
        var observations = Enumerable.Range(0, 30)
            .Select(_ => new WeightedObservation(
                Enumerable.Range(0, 10).Select(__ => random.NextDouble()).ToArray(),
                random.Next(2)))
            .ToList();

        var incremental = new LinUcbModel(10, alpha: 0.5);
        foreach (var o in observations)
        {
            incremental.Update(o.Context, o.Reward);
        }

        var recomputed = new LinUcbModel(10, alpha: 0.5);
        recomputed.Recompute(observations);

        double[] probe = [1, 0.5, 0.2, 0, 0.7, 0.4, 0.9, 0.1, 0.3, 1];
        Assert.Equal(incremental.Score(probe), recomputed.Score(probe), precision: 8);
        Assert.Equal(incremental.Uncertainty(probe), recomputed.Uncertainty(probe), precision: 8);
    }

    [Fact]
    public void Recompute_WeightTwo_EqualsTwoIdenticalUpdates()
    {
        double[] x = [1, 0.4, 0.6, 0, 0.2, 0.5, 0.5, 0.5, 0, 1];

        var twice = new LinUcbModel(10);
        twice.Update(x, 1.0);
        twice.Update(x, 1.0);

        var weighted = new LinUcbModel(10);
        weighted.Recompute([new WeightedObservation(x, 1.0, 2.0)]);

        Assert.Equal(twice.Score(x), weighted.Score(x), precision: 9);
    }

    [Fact]
    public void Recompute_IgnoresNonPositiveWeights_AndResetsPreviousState()
    {
        double[] x = [1, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        var model = new LinUcbModel(10);
        model.Update(x, 1.0);

        model.Recompute([new WeightedObservation(x, 1.0, 0.0)]);

        Assert.Equal(0.0, model.ExpectedReward(x), precision: 12);
        Assert.Equal(1.0, model.Uncertainty(x), precision: 12);
    }

    [Fact]
    public void ForgettingWeight_DecaysByWholeWeeks()
    {
        Assert.Equal(1.0, LinUcbDefaults.ForgettingWeight(0));
        Assert.Equal(1.0, LinUcbDefaults.ForgettingWeight(6.9));
        Assert.Equal(0.9, LinUcbDefaults.ForgettingWeight(7), precision: 10);
        Assert.Equal(0.81, LinUcbDefaults.ForgettingWeight(14), precision: 10);
    }

    // ---------- Ранжування ----------

    private static PrioritizationRequest Request(
        IReadOnlyList<TaskItem> tasks, LinUcbModel? model = null, int feedbackCount = 0) =>
        new(tasks, UserHistory.Empty, model, feedbackCount, Now, Utc);

    [Fact]
    public void Rank_Heuristic_PutsLayer1First_ThenByScore()
    {
        var low = Task(TaskPriority.Low);
        var critical = Task(TaskPriority.Critical, Now.AddHours(10));
        var overdue = Task(TaskPriority.High, Now.AddHours(-3));
        var medium = Task(TaskPriority.Medium, Now.AddDays(3));
        var done = new TaskItem { Priority = TaskPriority.Critical, CompletedAtUtc = Now.AddHours(-1) };

        var result = new Prioritizer().Rank(Request([low, critical, overdue, medium, done]));

        Assert.Equal(RankingMode.Heuristic, result.Mode);
        Assert.Equal(
            new[] { overdue.Id, critical.Id, medium.Id, low.Id },
            result.Ranked.Select(r => r.TaskId).ToArray());
        Assert.Equal(new[] { true, true, false, false }, result.Ranked.Select(r => r.IsLayer1).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4 }, result.Ranked.Select(r => r.Position).ToArray());
        Assert.Equal(ConstraintChecker.NewUserLimit, result.DailyLimit);
    }

    [Fact]
    public void Rank_SwitchesToLinUcb_OnlyAfterMinimumFeedback()
    {
        var model = new LinUcbModel(10, LinUcbDefaults.Alpha);
        var tasks = new[] { Task(TaskPriority.High, Now.AddDays(1)) };

        var cold = new Prioritizer().Rank(Request(tasks, model, LinUcbDefaults.MinFeedbackEvents - 1));
        var warm = new Prioritizer().Rank(Request(tasks, model, LinUcbDefaults.MinFeedbackEvents));

        Assert.Equal(RankingMode.Heuristic, cold.Mode);
        Assert.Equal(RankingMode.LinUcb, warm.Mode);
    }

    [Fact]
    public void Rank_LinUcb_ScoreIncludesUncertaintyBonus_AndReturnsContext()
    {
        var model = new LinUcbModel(10, LinUcbDefaults.Alpha);
        var task = Task(TaskPriority.High, Now.AddDays(1));

        var result = new Prioritizer().Rank(Request([task], model, feedbackCount: 50));

        var ranked = Assert.Single(result.Ranked);
        Assert.Equal(model.Score(ranked.Context), ranked.Score, precision: 12);
        Assert.Equal(10, ranked.Context.Length);
    }

    [Fact]
    public void Rank_WithWrongModelDimension_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new Prioritizer().Rank(Request([Task(TaskPriority.Low)], new LinUcbModel(3), 50)));
    }
}