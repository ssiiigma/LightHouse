using System.Globalization;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Services.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lighthouse.Api.Controllers;

[Route("api/reports")]
[Authorize]
public sealed class ReportsController(IReportService reports) : ApiControllerBase
{
    /// <summary>Звіт за діапазон дат (yyyy-MM-dd): format = pdf | csv, lang = uk | en (за замовчуванням uk).</summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? format, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? lang,
        CancellationToken ct)
    {
        var file = await reports.BuildAsync(
            User.GetUserId(), format ?? string.Empty, ParseDate(from, "from"), ParseDate(to, "to"), lang, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    private static DateOnly ParseDate(string? value, string name) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new AppException(StatusCodes.Status400BadRequest, $"{name} must be in yyyy-MM-dd format.");
}