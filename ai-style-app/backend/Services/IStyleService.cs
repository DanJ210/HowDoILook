using AiStyleApp.Api.Models;

namespace AiStyleApp.Api.Services;

public interface IStyleService
{
    Task<IEnumerable<StyleItemResponse>> GetAllAsync(string userId, CancellationToken ct = default);
    Task<StyleItemResponse?> GetByIdAsync(Guid id, string userId, CancellationToken ct = default);
    Task<(StyleItemResponse item, Guid jobId)> CreateAndEnqueueAsync(GenerateStyleRequest request, string userId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, string userId, CancellationToken ct = default);
}

