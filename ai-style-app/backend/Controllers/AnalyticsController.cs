using System.Globalization;
using System.Text;
using AiStyleApp.Api.Models;
using AiStyleApp.Api.Services;
using AiStyleApp.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiStyleApp.Api.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsService _analyticsService;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(IAnalyticsService analyticsService, ILogger<AnalyticsController> logger)
    {
        _analyticsService = analyticsService;
        _logger = logger;
    }

    /// <summary>
    /// Export recommendation dataset as CSV or JSON for analysis and model training.
    /// </summary>
    /// <param name="format">Export format: 'csv' or 'json' (default: json)</param>
    /// <param name="dataset">Dataset: 'exposures' or 'preferences' (default: exposures)</param>
    /// <param name="from">Filter from date (UTC, ISO 8601 format)</param>
    /// <param name="to">Filter to date (UTC, ISO 8601 format)</param>
    /// <returns>Recommendation data points with face shape, scores, feedback, and metrics</returns>
    [HttpGet("export-recommendations")]
    public async Task<IActionResult> ExportRecommendationsAsync(
        [FromQuery] string format = "json",
        [FromQuery] string dataset = "exposures",
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        try
        {
            if (!IsSupportedExportFormat(format))
            {
                return BadRequest(new { error = $"Invalid format: {format}. Supported formats are 'json' and 'csv'." });
            }

            if (!IsSupportedDataset(dataset))
            {
                return BadRequest(new { error = $"Invalid dataset: {dataset}. Supported datasets are 'exposures' and 'preferences'." });
            }

            DateTimeOffset? fromDate = ParseDateTimeOffset(from);
            DateTimeOffset? toDate = ParseDateTimeOffset(to);

            if (dataset.Equals("preferences", StringComparison.OrdinalIgnoreCase))
            {
                var preferences = await _analyticsService.ExportPreferenceLabelsAsync(fromDate, toDate);
                return CreateExportResponse(preferences, "preferences", format, fromDate, toDate);
            }

            var exposures = await _analyticsService.ExportExposureOutcomesAsync(fromDate, toDate);
            return CreateExportResponse(exposures, "exposures", format, fromDate, toDate);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Invalid date format in export request");
            return BadRequest(new { error = $"Invalid date format: {ex.Message}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting recommendation data");
            return StatusCode(500, new { error = "Failed to export recommendation data" });
        }
    }

    [HttpGet("coverage")]
    public async Task<IActionResult> GetCoverageAsync(
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        try
        {
            var fromDate = ParseDateTimeOffset(from);
            var toDate = ParseDateTimeOffset(to);
            return Ok(await _analyticsService.GetCoverageAsync(fromDate, toDate));
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Invalid date format in coverage request");
            return BadRequest(new { error = $"Invalid date format: {ex.Message}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing recommendation coverage");
            return StatusCode(500, new { error = "Failed to compute recommendation coverage" });
        }
    }

    /// <summary>
    /// Get aggregated recommendation metrics.
    /// </summary>
    /// <param name="from">Filter from date (UTC, ISO 8601 format)</param>
    /// <param name="to">Filter to date (UTC, ISO 8601 format)</param>
    /// <returns>Success rate, CTR, face shape distribution, top styles, and other metrics</returns>
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetricsAsync(
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        try
        {
            DateTimeOffset? fromDate = ParseDateTimeOffset(from);
            DateTimeOffset? toDate = ParseDateTimeOffset(to);

            var metrics = await _analyticsService.GetMetricsAsync(fromDate, toDate);

            _logger.LogInformation(
                "Computed recommendation metrics: SuccessRate={Rate:P}, CTR={CTR:P}",
                metrics.SuccessRate,
                metrics.ClickThroughRate);

            return Ok(metrics);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Invalid date format in metrics request");
            return BadRequest(new { error = $"Invalid date format: {ex.Message}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing recommendation metrics");
            return StatusCode(500, new { error = "Failed to compute metrics" });
        }
    }

    private IActionResult CreateExportResponse<T>(
        IReadOnlyList<T> data,
        string dataset,
        string format,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate)
    {
        _logger.LogInformation(
            "Exporting {Count} recommendation {Dataset} rows in {Format} format (from: {From}, to: {To})",
            data.Count,
            dataset,
            format.ToLowerInvariant(),
            fromDate?.UtcDateTime,
            toDate?.UtcDateTime);

        if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            return ExportAsCsv(data, dataset);
        }

        return Ok(new
        {
            format = "json",
            dataset,
            count = data.Count,
            periodStart = fromDate,
            periodEnd = toDate,
            data
        });
    }

    private FileContentResult ExportAsCsv<T>(IReadOnlyList<T> dataPoints, string dataset)
    {
        var sb = new StringBuilder();
        
        // Write CSV header
        var properties = typeof(T).GetProperties();
        sb.AppendLine(string.Join(",", properties.Select(p => EscapeCsv(p.Name))));
        
        // Write data rows
        foreach (var dataPoint in dataPoints)
        {
            var values = properties.Select(p => EscapeCsv(p.GetValue(dataPoint)?.ToString() ?? ""));
            sb.AppendLine(string.Join(",", values));
        }

        var fileContent = Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"recommendations-{dataset}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}.csv";

        return File(fileContent, "text/csv", fileName);
    }

    private DateTimeOffset? ParseDateTimeOffset(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString))
            return null;

        if (DateTimeOffset.TryParse(dateString, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var result))
            return result;

        throw new FormatException($"Invalid date format: {dateString}. Use ISO 8601 format (e.g., 2026-01-15T10:30:00Z)");
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private static bool IsSupportedExportFormat(string format)
        => format.Equals("json", StringComparison.OrdinalIgnoreCase)
            || format.Equals("csv", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedDataset(string dataset)
        => dataset.Equals("exposures", StringComparison.OrdinalIgnoreCase)
            || dataset.Equals("preferences", StringComparison.OrdinalIgnoreCase);
}
