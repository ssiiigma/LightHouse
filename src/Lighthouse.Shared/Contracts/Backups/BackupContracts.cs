using Lighthouse.Shared.Enums;

namespace Lighthouse.Shared.Contracts.Backups;

public sealed record BackupInfoDto(Guid Id, DateTime CreatedAtUtc, BackupKind Kind, long SizeBytes);

/// <param name="SafetyBackupId">Копія стану, який був до відновлення (дозволяє відкотитись).</param>
public sealed record RestoreResultDto(int Categories, int Tasks, int Habits, Guid SafetyBackupId);

public sealed record ImportResultDto(int Categories, int Tasks, int Habits, int Skipped, IReadOnlyList<string> Errors);