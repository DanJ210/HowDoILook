using System.Globalization;
using System.Text;
using AiStyleApp.Backend.Services;
using AiStyleApp.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiStyleApp.Backend.Controllers;

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
    /// <param name="from">Filter from date (UTC, ISO 8601 format)</param>
    /// <param name="to">Filter to date (UTC, ISO 8601 format)</param>
    /// <returns>Recommendation data points with face shape, scores, feedback, and metrics</returns>
    [HttpGet("export-recommendations")]
    public async Task<IActionResult> ExportRecommendationsAsync(
        [FromQuery] string format = "json",
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        try
        {
            DateTimeOffset? fromDate = ParseDateTimeOffset(from);
            DateTimeOffset? toDate = ParseDateTimeOffset(to);

            var data = await _analyticsService.ExportRecommendationsDataAsync(fromDate, toDate);
            var dataList = data.ToList();

            _logger.LogInformation(
                "Exporting {Count} recommendation data points in {Format} format (from: {From}, to: {To})",
                dataList.Count,
                format.ToLower(),
                fromDate?.UtcDateTime,
                toDate?.UtcDateTime);

            if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            {
                return ExportAsCsv(dataList);
            }

            return Ok(new
            {
                format = "json",
                count = dataList.Count,
                periodStart = fromDate,
                periodEnd = toDate,
                data = dataList
            });
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

    private FileContentResult ExportAsCsv(List<RecommendationDataPoint> dataPoints)
    {
        var sb = new StringBuilder();
        
        // Write CSV header
        var properties = typeof(RecommendationDataPoint).GetProperties();
        sb.AppendLine(string.Join(",", properties.Select(p => EscapeCsv(p.Name))));
        
        // Write data rows
        foreach (var dataPoint in dataPoints)
        {
            var values = properties.Select(p => EscapeCsv(p.GetValue(dataPoint)?.ToString() ?? ""));
            sb.AppendLine(string.Join(",", values));
        }

        var fileContent = Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"recommendations-export-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}.csv";

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
}
