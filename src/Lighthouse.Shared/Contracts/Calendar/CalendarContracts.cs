namespace Lighthouse.Shared.Contracts.Calendar;

public sealed record CalendarStatusDto(bool Connected, int EventCount);

/// <summary>Адреса входу Google, яку застосунок відкриває в браузері.</summary>
public sealed record CalendarConnectDto(string AuthorizationUrl);

/// <param name="Remaining">Скільки змін лишилось до наступного циклу (за один цикл виконується до 50 операцій).</param>
public sealed record CalendarSyncResultDto(int Created, int Updated, int Deleted, int Remaining);