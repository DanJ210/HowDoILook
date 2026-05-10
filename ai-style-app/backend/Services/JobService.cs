using AiStyleApp.Api.Models;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiStyleApp.Api.Services;

public interface IJobService
{
    Task<JobStatusResponse?> GetJobStatusAsync(Guid jobId, string userId, CancellationToken ct = default);
    Task<IEnumerable<UserJobSummaryResponse>> GetAllAsync(string userId, CancellationToken ct = default);
    Task<UserJobSummaryResponse?> UpdateVisibilityAsync(Guid jobId, string userId, bool isResultPublic, CancellationToken ct = default);
    Task<StyleJobEntity?> GetByExternalIdAsync(string externalPredictionId, CancellationToken ct = default);
    Task UpdateFromWebhookAsync(StyleJobEntity job, string status, string? resultJson, string? errorCode, string? errorMessage, CancellationToken ct = default);
}

public class JobService : IJobService
{
    private readonly AppDbContext _db;

    public JobService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<JobStatusResponse?> GetJobStatusAsync(Guid jobId, string userId, CancellationToken ct = default)
    {
        var job = await _db.StyleJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, ct);

        if (job is null) return null;

        return MapToResponse(job);
    }

    public async Task<IEnumerable<UserJobSummaryResponse>> GetAllAsync(string userId, CancellationToken ct = default)
    {
        var jobs = await _db.StyleJobs
            .AsNoTracking()
            .Include(j => j.StyleItem)
            .Where(j => j.UserId == userId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .ToListAsync(ct);

        return jobs.Select(job => new UserJobSummaryResponse(
            job.Id,
            job.StyleItemId,
            job.StyleItem.Name,
            job.Status,
            job.ResultImageUrl,
            job.StyleItem.IsResultPublic,
            job.CreatedAtUtc,
            job.CompletedAtUtc));
    }

    public Task<StyleJobEntity?> GetByExternalIdAsync(string externalPredictionId, CancellationToken ct = default)
        => _db.StyleJobs.FirstOrDefaultAsync(j => j.ExternalPredictionId == externalPredictionId, ct);

    public async Task<UserJobSummaryResponse?> UpdateVisibilityAsync(Guid jobId, string userId, bool isResultPublic, CancellationToken ct = default)
    {
        var job = await _db.StyleJobs
            .Include(j => j.StyleItem)
            .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, ct);

        if (job is null)
        {
            return null;
        }

        job.StyleItem.IsResultPublic = isResultPublic;
        await _db.SaveChangesAsync(ct);

        return new UserJobSummaryResponse(
            job.Id,
            job.StyleItemId,
            job.StyleItem.Name,
            job.Status,
            job.ResultImageUrl,
            job.StyleItem.IsResultPublic,
            job.CreatedAtUtc,
            job.CompletedAtUtc);
    }

    public async Task UpdateFromWebhookAsync(
        StyleJobEntity job,
        string status,
        string? resultJson,
        string? errorCode,
        string? errorMessage,
        CancellationToken ct = default)
    {
        job.Status = status;
        job.ResultJson = resultJson;
        job.ErrorCode = errorCode;
        job.ErrorMessage = errorMessage;

        if (status == JobStatus.Processing && job.StartedAtUtc is null)
            job.StartedAtUtc = DateTimeOffset.UtcNow;

        if (JobStatus.IsTerminal(status))
            job.CompletedAtUtc = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    private static JobStatusResponse MapToResponse(StyleJobEntity job) => new(
        job.Id,
        job.StyleItemId,
        job.Status,
        job.JobType,
        job.ErrorCode,
        job.ErrorMessage,
        job.ResultJson,
        job.ResultImageUrl,
        job.ExternalPredictionId,
        job.CreatedAtUtc,
        job.StartedAtUtc,
        job.CompletedAtUtc,
        job.AttemptCount
    );
}
