using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Services.Backups;
using Lighthouse.Shared.Contracts.Backups;
using Lighthouse.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lighthouse.Api.Controllers;

[Route("api/backups")]
[Authorize]
public sealed class BackupsController(IBackupService backups) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BackupInfoDto>>> List(CancellationToken ct) =>
        Ok(await backups.ListAsync(User.GetUserId(), ct));

    /// <summary>Ручна копія поточного стану (до 10 штук).</summary>
    [HttpPost]
    public async Task<ActionResult<BackupInfoDto>> Create(CancellationToken ct) =>
        Ok(await backups.CreateAsync(User.GetUserId(), BackupKind.Manual, enforceLimit: true, createdAtUtc: null, ct));

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var (content, fileName) = await backups.DownloadAsync(User.GetUserId(), id, ct);
        return File(content, "application/json", fileName);
    }

    /// <summary>Замінює поточні дані даними з копії; перед цим створюється копія поточного стану.</summary>
    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<RestoreResultDto>> Restore(Guid id, CancellationToken ct) =>
        Ok(await backups.RestoreAsync(User.GetUserId(), id, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await backups.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}