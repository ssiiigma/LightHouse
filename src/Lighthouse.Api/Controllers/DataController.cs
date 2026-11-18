using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Services.Backups;
using Lighthouse.Shared.Contracts.Backups;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lighthouse.Api.Controllers;

[Route("api/data")]
[Authorize]
public sealed class DataController(IDataTransferService data) : ApiControllerBase
{
    public const int MaxImportBytes = 5 * 1024 * 1024;

    /// <summary>Експорт даних: format = json (повний) | csv (завдання).</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? format, CancellationToken ct)
    {
        var file = await data.ExportAsync(User.GetUserId(), format ?? string.Empty, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>Імпорт: тіло запиту — вміст файлу (до 5 МБ); записи додаються як нові.</summary>
    [HttpPost("import")]
    [RequestSizeLimit(MaxImportBytes)]
    public async Task<ActionResult<ImportResultDto>> Import([FromQuery] string? format, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        return Ok(await data.ImportAsync(User.GetUserId(), format ?? string.Empty, buffer.ToArray(), ct));
    }
}