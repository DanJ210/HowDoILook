using AiStyleApp.Api.Models;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using AiStyleApp.Data.Queue;
using Microsoft.EntityFrameworkCore;

namespace AiStyleApp.Api.Services;

public class StyleService : IStyleService
{
    private readonly AppDbContext _db;
    private readonly IQueuePublisher _queue;

    public StyleService(AppDbContext db, IQueuePublisher queue)
    {
        _db = db;
        _queue = queue;
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

    public async Task<(StyleItemResponse item, Guid jobId)> CreateAndEnqueueAsync(
        GenerateStyleRequest request,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            throw new ArgumentException("ImageUrl is required for hairstyle generation.", nameof(request));
        }

        var item = new StyleItemEntity
        {
            UserId = userId,
            Name = request.Name,
            Description = request.Description,
            Prompt = request.Prompt ?? BuildPromptSummary(request),
            ImageUrl = request.ImageUrl,
            IsResultPublic = request.IsResultPublic
        };

        var job = new StyleJobEntity
        {
            UserId = userId,
            StyleItemId = item.Id,
            Prompt = request.Prompt ?? BuildPromptSummary(request),
            ImageUrl = request.ImageUrl,
            CorrelationId = Guid.NewGuid().ToString()
        };

        item.Jobs.Add(job);
        _db.StyleItems.Add(item);
        await _db.SaveChangesAsync(ct);

        var queueMessage = new StyleJob(
            JobId: job.Id,
            StyleItemId: item.Id,
            UserId: userId,
            JobType: job.JobType,
            Prompt: request.Prompt ?? BuildPromptSummary(request),
            EnqueuedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: job.CorrelationId ?? Guid.NewGuid().ToString(),
            Attempt: 0,
            SchemaVersion: 1,
            ImageUrl: request.ImageUrl,
            Haircut: request.Haircut,
            HairColor: request.HairColor,
            Gender: request.Gender
        );

        await _queue.PublishAsync(queueMessage, ct);

        return (Map(item), job.Id);
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

    private static string BuildPromptSummary(GenerateStyleRequest r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.Haircut)) parts.Add($"Haircut: {r.Haircut}");
        if (!string.IsNullOrWhiteSpace(r.HairColor)) parts.Add($"Hair color: {r.HairColor}");
        if (!string.IsNullOrWhiteSpace(r.Gender) && r.Gender != "none") parts.Add($"Gender: {r.Gender}");
        return parts.Count > 0 ? string.Join(", ", parts) : r.Description;
    }
}

