using AiStyleApp.Api.Models;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiStyleApp.Api.Services;

public class StyleService : IStyleService
{
    private readonly AppDbContext _db;

    public StyleService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<StyleItemResponse>> GetAllAsync(string userId, CancellationToken ct = default)
    {
        var items = await _db.StyleItems
            .AsNoTracking()
            .Include(x => x.Jobs)
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return items.Select(Map);
    }

    public async Task<StyleItemResponse?> GetByIdAsync(Guid id, string userId, CancellationToken ct = default)
    {
        var item = await _db.StyleItems
            .AsNoTracking()
            .Include(x => x.Jobs)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

        return item is null ? null : Map(item);
    }

    public async Task<FeedPageResponse> GetPublicFeedAsync(int take, DateTimeOffset? before, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 50);

        var query = _db.StyleJobs
            .AsNoTracking()
            .Include(j => j.StyleItem)
            .Where(j =>
                j.StyleItem.IsResultPublic &&
                j.ResultImageUrl != null &&
                j.Status == "Succeeded" &&
                j.CompletedAtUtc != null &&
                _db.FaceAnalysisJobs.Any(analysisJob =>
                    analysisJob.PrimaryGenerationJobId == j.Id ||
                    (analysisJob.PrimaryGenerationJobId == null &&
                     (analysisJob.PrimaryStyleItemId == j.StyleItemId ||
                      (analysisJob.PrimaryStyleItemId == null &&
                       j.StyleItem.Description.Contains(analysisJob.Id.ToString()))) &&
                     j.Id == _db.StyleJobs
                         .Where(candidate => candidate.StyleItemId == j.StyleItemId)
                         .OrderByDescending(candidate => candidate.CreatedAtUtc)
                         .Select(candidate => candidate.Id)
                         .FirstOrDefault())));

        if (before.HasValue)
            query = query.Where(j => j.CompletedAtUtc < before.Value);

        var jobs = await query
            .OrderByDescending(j => j.CompletedAtUtc)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasMore = jobs.Count > take;
        if (hasMore) jobs = jobs.Take(take).ToList();

        var feedItems = jobs
            .Select(j => new PublicFeedItemResponse(
                j.StyleItemId,
                j.Id,
                j.StyleItem.Name,
                j.StyleItem.Description,
                j.ResultImageUrl!,
                j.CompletedAtUtc!.Value))
            .ToList();

        return new FeedPageResponse(feedItems, hasMore);
    }

    public async Task<bool> DeleteAsync(Guid id, string userId, CancellationToken ct = default)
    {
        var item = await _db.StyleItems
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

        if (item is null) return false;

        _db.StyleItems.Remove(item);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static StyleItemResponse Map(StyleItemEntity e)
    {
        var latestJob = e.Jobs.OrderByDescending(job => job.CreatedAtUtc).FirstOrDefault();
        return new(
            e.Id,
            e.Name,
            e.Description,
            e.ImageUrl,
            e.IsResultPublic,
            e.CreatedAtUtc,
            latestJob?.Id,
            latestJob?.Status);
    }

}
