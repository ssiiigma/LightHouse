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
    public async Task<ActionResult<ImportResultDto>> Import([FromQuery] string? format, CancellationToken ct) =>
        Ok(await data.ImportAsync(
            User.GetUserId(), format ?? string.Empty, await ReadLimitedAsync(Request.Body, MaxImportBytes, ct), ct));

    /// <summary>Читає тіло запиту й перериває, щойно перевищено ліміт (працює незалежно від налаштувань сервера).</summary>
    private static async Task<byte[]> ReadLimitedAsync(Stream body, int maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new AppException(StatusCodes.Status413PayloadTooLarge, "The file is too large.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}