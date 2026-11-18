using Lighthouse.Mobile.Services.Api;
using Lighthouse.Shared.Contracts.Backups;

namespace Lighthouse.Mobile.Services.Backups;

/// <summary>Резервні копії, експорт і імпорт даних: усе виконується на сервері й потребує мережі.</summary>
public interface IBackup
{
    Task<IReadOnlyList<BackupInfoDto>> ListAsync(CancellationToken ct);
    Task<BackupInfoDto> CreateAsync(CancellationToken ct);
    Task<RestoreResultDto> RestoreAsync(Guid id, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>Завантажує експорт (json | csv) у кеш застосунку й повертає шлях до файлу.</summary>
    Task<string> ExportAsync(string format, CancellationToken ct);

    Task<ImportResultDto> ImportAsync(string format, byte[] content, CancellationToken ct);
}

public sealed class BackupService(IApiClient api) : IBackup
{
    public async Task<IReadOnlyList<BackupInfoDto>> ListAsync(CancellationToken ct) =>
        await api.GetAsync<List<BackupInfoDto>>("api/backups", ct);

    public Task<BackupInfoDto> CreateAsync(CancellationToken ct) =>
        api.PostAsync<object, BackupInfoDto>("api/backups", new { }, ct);

    public Task<RestoreResultDto> RestoreAsync(Guid id, CancellationToken ct) =>
        api.PostAsync<object, RestoreResultDto>($"api/backups/{id}/restore", new { }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct) => api.DeleteAsync($"api/backups/{id}", ct);

    public async Task<string> ExportAsync(string format, CancellationToken ct)
    {
        var bytes = await api.GetBytesAsync($"api/data/export?format={format}", ct);
        var path = Path.Combine(FileSystem.CacheDirectory, $"lighthouse-export-{DateTime.Now:yyyy-MM-dd}.{format}");
        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }

    public Task<ImportResultDto> ImportAsync(string format, byte[] content, CancellationToken ct) =>
        api.PostBytesAsync<ImportResultDto>(
            $"api/data/import?format={format}", content, format == "json" ? "application/json" : "text/csv", ct);
}