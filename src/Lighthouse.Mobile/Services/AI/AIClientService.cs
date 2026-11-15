using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lighthouse.Mobile.Data;
using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Models;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Mobile.Services.Sync;
using Lighthouse.Shared.Contracts.Profile;
using Lighthouse.Shared.Contracts.Recommendations;
using Lighthouse.Shared.Enums;
using Lighthouse.Shared.Ranking;
using Lighthouse.Shared.Scheduling;
using Microsoft.Extensions.Logging;
using SQLite;

namespace Lighthouse.Mobile.Services.AI;

/// <summary>Рекомендації: ранжування сервера (за наявності мережі) або офлайн-евристика, поради щодо часу, відгук для моделі.</summary>
public interface IAIClient
{
    /// <summary>Поради за пріоритетом показу: «почніть із», зміна часу нагадування, ліміти доби.</summary>
    Task<IReadOnlyList<AdviceItem>> GetAdviceAsync(CancellationToken ct);

    /// <summary>Користувач відповів на пораду: прийнято/застосовано або відхилено/сховано.</summary>
    Task ResolveAsync(AdviceItem item, bool applied, CancellationToken ct = default);
}

public sealed class AIClientService(
    ILocalDB localDb, IApiClient api, IConnectivity connectivity, ISyncSvc sync,
    LocalizationService loc, ILogger<AIClientService> logger) : IAIClient
{
    private const int TopCandidates = 3;
    private static readonly TimeSpan SuppressionShort = TimeSpan.FromHours(12);
    private static readonly TimeSpan SuppressionLong = TimeSpan.FromDays(7);
    private static readonly TimeSpan KeyLifetime = TimeSpan.FromDays(13);

    public async Task<IReadOnlyList<AdviceItem>> GetAdviceAsync(CancellationToken ct)
    {
        var db = await localDb.GetConnectionAsync();
        var rows = await db.Table<LocalTask>().Where(t => !t.IsDeleted).ToListAsync();
        var open = rows.Where(t => t.CompletedAtUtc is null).ToDictionary(t => t.Id);
        var completedLocal = rows
            .Where(t => t.CompletedAtUtc is not null)
            .Select(t => ToLocal(t.CompletedAtUtc!.Value))
            .ToList();

        var ranking = await TryGetRankingAsync(db, ct);

        var items = new List<AdviceItem>();
        var focus = ranking is not null ? FocusFromServer(ranking, open) : await FocusOfflineAsync(db, open);
        if (focus is not null)
        {
            items.Add(focus);
        }

        items.AddRange(RescheduleAdvice(open, completedLocal));
        if (ranking is not null)
        {
            items.AddRange(ResolutionAdvice(ranking, open));
        }

        return items;
    }

    public async Task ResolveAsync(AdviceItem item, bool applied, CancellationToken ct = default)
    {
        AdviceStore.Suppress(item.Id, item.Kind == AdviceKind.Reschedule ? SuppressionLong : SuppressionShort);

        if (item.Kind == AdviceKind.Focus
            && item.RecommendationKey is { } key
            && Guid.TryParse(key, out var keyId))
        {
            var payload = JsonSerializer.Serialize(
                new FeedbackRequest(key, applied ? RecommendationAction.Accepted : RecommendationAction.Rejected),
                AppJson.Options);

            var db = await localDb.GetConnectionAsync();
            await db.RunInTransactionAsync(tx =>
                OutboxStore.Enqueue(tx, EntityTypes.Feedback, keyId, OutboxStore.Put, payload));
            sync.RequestSync();
        }
    }

    // ---------- Ранжування ----------

    private async Task<RankingResponse?> TryGetRankingAsync(SQLiteAsyncConnection db, CancellationToken ct)
    {
        if (connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            return null;
        }

        try
        {
            await EnsureTimeZoneAsync(ct);
            var ranking = await api.GetAsync<RankingResponse>("api/recommendations/ranking", ct);
            await CacheAsync(db, ranking);
            return ranking;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogInformation(ex, "Server ranking is unavailable; using the offline heuristic.");
            return null;
        }
    }

    /// <summary>Сервер рахує годину доби й день тижня за поясом користувача: передаємо його один раз (і після зміни).</summary>
    private async Task EnsureTimeZoneAsync(CancellationToken ct)
    {
        var zone = TimeZoneInfo.Local.Id;
        if (Preferences.Default.Get(PreferenceKeys.TimeZoneSent, string.Empty) == zone)
        {
            return;
        }

        try
        {
            await api.PutAsync<TimeZoneDto, TimeZoneDto>("api/profile/timezone", new TimeZoneDto(zone), ct);
            Preferences.Default.Set(PreferenceKeys.TimeZoneSent, zone);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
        {
            Preferences.Default.Set(PreferenceKeys.TimeZoneSent, zone); // сервер не знає цей ідентифікатор: не повторюємо
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation(ex, "Time zone was not sent.");
        }
    }

    private static async Task CacheAsync(SQLiteAsyncConnection db, RankingResponse ranking)
    {
        var now = Utc.Now;
        var rows = ranking.Ranked.Select(r => new RankingCache
        {
            TaskId = r.TaskId,
            Rank = r.Position,
            Score = r.Score,
            ExplanationText = string.Join(',', r.Reasons.Select(x => x.Code)),
            RecommendationKey = r.RecommendationKey,
            ComputedAtUtc = now
        }).ToList();

        await db.RunInTransactionAsync(tx =>
        {
            tx.DeleteAll<RankingCache>();
            tx.InsertAll(rows);
        });
    }

    private AdviceItem? FocusFromServer(RankingResponse ranking, Dictionary<Guid, LocalTask> open)
    {
        foreach (var ranked in ranking.Ranked.Where(r => r.RecommendationKey is not null).Take(TopCandidates))
        {
            if (!open.TryGetValue(ranked.TaskId, out var task) || AdviceStore.IsSuppressed($"focus:{ranked.TaskId}"))
            {
                continue;
            }

            return BuildFocus(task, ranked.Reasons.Select(r => r.Code).ToList(), ranked.IsExploration, ranked.RecommendationKey);
        }

        return null;
    }

    /// <summary>Офлайн-евристика; ключ відгуку береться з кешу останнього ранжування, якщо завдання було в топ-3.</summary>
    private async Task<AdviceItem?> FocusOfflineAsync(SQLiteAsyncConnection db, Dictionary<Guid, LocalTask> open)
    {
        var cache = await db.Table<RankingCache>().ToListAsync();
        var ranked = OfflineRanker.Rank(
            open.Values.Select(t => new OfflineTask(t.Id, t.Priority, Utc.Ensure(t.DeadlineUtc))), Utc.Now);

        foreach (var item in ranked.Take(TopCandidates))
        {
            if (AdviceStore.IsSuppressed($"focus:{item.TaskId}"))
            {
                continue;
            }

            var key = cache
                .FirstOrDefault(c => c.TaskId == item.TaskId && Utc.Now - Utc.Ensure(c.ComputedAtUtc) < KeyLifetime)
                ?.RecommendationKey;
            return BuildFocus(open[item.TaskId], item.ReasonCodes, false, key);
        }

        return null;
    }

    private AdviceItem BuildFocus(LocalTask task, IReadOnlyList<string> codes, bool exploration, string? key)
    {
        var reasons = codes.Select(ReasonText).OfType<string>().ToList();
        var detail = reasons.Count > 0 ? loc.Get("Advice_Why", string.Join("; ", reasons)) : string.Empty;
        if (exploration)
        {
            detail = (detail + " " + loc["Advice_Exploration"]).Trim();
        }

        return new AdviceItem
        {
            Id = $"focus:{task.Id}",
            Kind = AdviceKind.Focus,
            Title = loc.Get("Advice_FocusTitle", task.Title),
            Summary = reasons.Count > 0 ? Capitalize(reasons[0]) : loc["Advice_FocusDefault"],
            Detail = detail.Length > 0 ? detail : loc["Advice_FocusDefault"],
            TaskIds = [task.Id],
            RecommendationKey = key,
            IsActionable = true,
            ApplyText = loc["Btn_Accept"],
            DismissText = loc["Btn_Reject"]
        };
    }

    // ---------- Поради щодо часу й ліміту ----------

    private IEnumerable<AdviceItem> RescheduleAdvice(Dictionary<Guid, LocalTask> open, List<DateTime> completedLocal)
    {
        var candidates = open.Values
            .Where(t => t.ReminderAtUtc is not null)
            .Select(t => new ScheduleTask(
                t.Id, ToLocal(t.ReminderAtUtc!.Value), t.DeadlineUtc is { } d ? ToLocal(d) : null))
            .ToList();

        foreach (var suggestion in ScheduleAdvisor.Suggest(DateTime.Now, completedLocal, candidates).Take(3))
        {
            var id = $"resched:{suggestion.TaskId}:{suggestion.CurrentLocal.Ticks}";
            if (AdviceStore.IsSuppressed(id))
            {
                continue;
            }

            var task = open[suggestion.TaskId];
            yield return new AdviceItem
            {
                Id = id,
                Kind = AdviceKind.Reschedule,
                Title = loc.Get("Advice_Reschedule_Title", task.Title),
                Summary = loc["Advice_Reschedule_Summary"],
                Detail = loc.Get("Advice_Reschedule_Detail",
                    FormatMoment(suggestion.CurrentLocal), BlockLabel(suggestion.BestBlock), FormatMoment(suggestion.ProposedLocal)),
                TaskIds = [task.Id],
                ProposedReminderUtc = DateTime.SpecifyKind(suggestion.ProposedLocal, DateTimeKind.Local).ToUniversalTime(),
                IsActionable = true,
                ApplyText = loc["Btn_Apply"],
                DismissText = loc["Btn_Dismiss"]
            };
        }
    }

    private IEnumerable<AdviceItem> ResolutionAdvice(RankingResponse ranking, Dictionary<Guid, LocalTask> open)
    {
        foreach (var resolution in ranking.Resolutions)
        {
            var ids = resolution.TaskIds.Where(open.ContainsKey).ToList();

            if (resolution.Kind == ResolutionKind.DeferToNextDay)
            {
                // Переносити можна лише завдання, прив'язані до часу (з дедлайном).
                ids = ids.Where(id => open[id].DeadlineUtc is not null).ToList();
                if (ids.Count == 0 || AdviceStore.IsSuppressed($"defer:{Hash(ids)}"))
                {
                    continue;
                }

                yield return new AdviceItem
                {
                    Id = $"defer:{Hash(ids)}",
                    Kind = AdviceKind.Defer,
                    Title = loc["Advice_Defer_Title"],
                    Summary = TitlesList(ids, open),
                    Detail = loc.Get("Advice_Defer_Detail", ranking.DailyLimit, TitlesList(ids, open)),
                    TaskIds = ids,
                    IsActionable = true,
                    ApplyText = loc["Btn_Apply"],
                    DismissText = loc["Btn_Dismiss"]
                };
            }
            else if (resolution.Kind == ResolutionKind.DoesNotFit && ids.Count > 0
                     && !AdviceStore.IsSuppressed($"fit:{Hash(ids)}"))
            {
                yield return new AdviceItem
                {
                    Id = $"fit:{Hash(ids)}",
                    Kind = AdviceKind.DoesNotFit,
                    Title = loc["Advice_Fit_Title"],
                    Summary = TitlesList(ids, open),
                    Detail = loc.Get("Advice_Fit_Detail", TitlesList(ids, open)),
                    TaskIds = ids,
                    IsActionable = false,
                    DismissText = loc["Btn_Hide"]
                };
            }
        }
    }

    // ---------- Допоміжне ----------

    private string? ReasonText(string code)
    {
        var key = $"Reason_{code}";
        var text = loc[key];
        return text.StartsWith('[') ? null : text;
    }

    private string TitlesList(IReadOnlyList<Guid> ids, Dictionary<Guid, LocalTask> open)
    {
        var text = string.Join(", ", ids.Take(3).Select(id => $"«{open[id].Title}»"));
        return ids.Count > 3 ? $"{text} {loc.Get("Advice_More", ids.Count - 3)}" : text;
    }

    private static string FormatMoment(DateTime local) => local.ToString("ddd d MMM, t", CultureInfo.CurrentCulture);

    private static string BlockLabel(int block) => $"{block * 4:00}:00–{block * 4 + 4:00}:00";

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0]) + text[1..];

    private static string Hash(IEnumerable<Guid> ids) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(string.Join(',', ids.OrderBy(i => i)))))[..12];

    private static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
}