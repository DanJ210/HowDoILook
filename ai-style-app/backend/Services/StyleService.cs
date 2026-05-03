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
        var item = new StyleItemEntity
        {
            UserId = userId,
            Name = request.Name,
            Description = request.Description,
            Prompt = request.Prompt,
            ImageUrl = request.ImageUrl
        };

        var job = new StyleJobEntity
        {
            UserId = userId,
            StyleItemId = item.Id,
            Prompt = request.Prompt,
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
            Prompt: request.Prompt,
            EnqueuedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: job.CorrelationId ?? Guid.NewGuid().ToString(),
            Attempt: 0,
            SchemaVersion: 1,
            ImageUrl: request.ImageUrl
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

    private static StyleItemResponse Map(StyleItemEntity e) =>
        new(
            e.Id,
            e.Name,
            e.Description,
            e.ImageUrl,
            e.CreatedAtUtc,
            e.Jobs.OrderByDescending(job => job.CreatedAtUtc).Select(job => (Guid?)job.Id).FirstOrDefault());
}

