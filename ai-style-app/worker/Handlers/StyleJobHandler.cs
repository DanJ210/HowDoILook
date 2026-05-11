using System.Text.Json;
using AiStyleApp.Data;
using AiStyleApp.Data.Queue;
using AiStyleApp.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace AiStyleApp.Worker.Handlers;

public class StyleJobHandler : IMessageHandler
{
    private const int ErrorMessageMaxLength = 2000;
    private static readonly HashSet<string> AllowedHairColors = new(StringComparer.Ordinal)
    {
        "No change", "Random", "Blonde", "Brunette", "Black", "Dark Brown", "Medium Brown", "Light Brown", "Auburn",
        "Copper", "Red", "Strawberry Blonde", "Platinum Blonde", "Silver", "White", "Blue", "Purple", "Pink", "Green",
        "Blue-Black", "Golden Blonde", "Honey Blonde", "Caramel", "Chestnut", "Mahogany", "Burgundy", "Jet Black",
        "Ash Brown", "Ash Blonde", "Titanium", "Rose Gold"
    };
    private static readonly HashSet<string> AllowedHaircuts = new(StringComparer.Ordinal)
    {
        "No change", "Random", "Straight", "Wavy", "Curly", "Bob", "Pixie Cut", "Layered", "Messy Bun", "High Ponytail",
        "Low Ponytail", "Braided Ponytail", "French Braid", "Dutch Braid", "Fishtail Braid", "Space Buns", "Top Knot",
        "Undercut", "Mohawk", "Crew Cut", "Faux Hawk", "Slicked Back", "Side-Parted", "Center-Parted", "Blunt Bangs",
        "Side-Swept Bangs", "Shag", "Lob", "Angled Bob", "A-Line Bob", "Asymmetrical Bob", "Graduated Bob", "Inverted Bob",
        "Layered Shag", "Choppy Layers", "Razor Cut", "Perm", "Ombré", "Straightened", "Soft Waves", "Glamorous Waves",
        "Hollywood Waves", "Finger Waves", "Tousled", "Feathered", "Pageboy", "Pigtails", "Pin Curls", "Rollerset",
        "Twist Out", "Bantu Knots", "Dreadlocks", "Cornrows", "Box Braids", "Crochet Braids", "Double Dutch Braids",
        "French Fishtail Braid", "Waterfall Braid", "Rope Braid", "Heart Braid", "Halo Braid", "Crown Braid", "Braided Crown",
        "Bubble Braid", "Bubble Ponytail", "Ballerina Braids", "Milkmaid Braids", "Bohemian Braids", "Flat Twist",
        "Crown Twist", "Twisted Bun", "Twisted Half-Updo", "Twist and Pin Updo", "Chignon", "Simple Chignon",
        "Messy Chignon", "French Twist", "French Twist Updo", "French Roll", "Updo", "Messy Updo", "Knotted Updo",
        "Ballerina Bun", "Banana Clip Updo", "Beehive", "Bouffant", "Hair Bow", "Half-Up Top Knot", "Half-Up, Half-Down",
        "Messy Bun with a Headband", "Messy Bun with a Scarf", "Messy Fishtail Braid", "Sideswept Pixie", "Mohawk Fade",
        "Zig-Zag Part", "Victory Rolls"
    };
    private static readonly HashSet<string> AllowedBeardStyles = new(StringComparer.Ordinal)
    {
        "No change", "Random", "Clean Shaven", "Stubble", "Heavy Stubble", "Short Beard", "Medium Beard",
        "Long Beard", "Full Beard", "Circle Beard", "Goatee", "Van Dyke", "Balbo", "Anchor", "Ducktail",
        "Mutton Chops", "Chin Strap", "Soul Patch", "Mustache", "Handlebar Mustache", "Chevron Mustache",
        "Pencil Mustache"
    };
    private static readonly HashSet<string> AllowedBeardColors = new(StringComparer.Ordinal)
    {
        "No change", "Random", "Black", "Dark Brown", "Medium Brown", "Light Brown", "Blonde", "Auburn",
        "Red", "Ginger", "Gray", "Silver", "White"
    };
    private static readonly Dictionary<string, string> HaircutAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bob cut"] = "Bob",
        ["Braid"] = "French Braid",
        ["Crew cut"] = "Crew Cut",
        ["Pixie cut"] = "Pixie Cut",
        ["Ponytail"] = "Low Ponytail",
        ["Slicked back"] = "Slicked Back",
        ["Long straight"] = "Straight",
        ["Man bun"] = "Top Knot",
        ["Fade"] = "Crew Cut",
        ["Buzz cut"] = "Crew Cut"
    };
    private static readonly Dictionary<string, string> HairColorAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dark brown"] = "Dark Brown",
        ["Brown"] = "Medium Brown",
        ["Dark blonde"] = "Honey Blonde",
        ["Platinum"] = "Platinum Blonde",
        ["Gray"] = "Silver",
        ["Ginger"] = "Copper"
    };
    private static readonly Dictionary<string, string> BeardStyleAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["None"] = "Clean Shaven",
        ["No beard"] = "Clean Shaven",
        ["Five o clock shadow"] = "Stubble",
        ["5 o'clock shadow"] = "Stubble",
        ["Mustache only"] = "Mustache"
    };
    private static readonly Dictionary<string, string> BeardColorAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Brown"] = "Medium Brown",
        ["Grey"] = "Gray"
    };

    private readonly ILogger<StyleJobHandler> _logger;
    private readonly AppDbContext _db;
    private readonly IReplicateWorkerClient _replicate;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _webhookBaseUrl;

    public StyleJobHandler(
        ILogger<StyleJobHandler> logger,
        AppDbContext db,
        IReplicateWorkerClient replicate,
        IHttpClientFactory httpClientFactory,
        IConfiguration config)
    {
        _logger = logger;
        _db = db;
        _replicate = replicate;
        _httpClientFactory = httpClientFactory;
        _webhookBaseUrl = config["Replicate:WebhookBaseUrl"]?.TrimEnd('/') ?? string.Empty;
    }

    public async Task HandleAsync(string messageBody, CancellationToken cancellationToken)
    {
        StyleJob? job;
        try
        {
            job = JsonSerializer.Deserialize<StyleJob>(messageBody);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize StyleJob message.");
            return;
        }

        if (job is null)
        {
            _logger.LogWarning("Received null or undeserializable style job message.");
            return;
        }

        _logger.LogInformation(
            "Processing style job {JobId} for item {StyleItemId} (attempt {Attempt}).",
            job.JobId, job.StyleItemId, job.Attempt);

        // Load the persisted job entity
        var entity = await _db.StyleJobs
            .FirstOrDefaultAsync(j => j.Id == job.JobId, cancellationToken);

        if (entity is null)
        {
            _logger.LogWarning("Job entity {JobId} not found in database; skipping.", job.JobId);
            return;
        }

        // Don't reprocess terminal jobs
        if (entity.Status is "Succeeded" or "Failed" or "Canceled" or "TimedOut")
        {
            _logger.LogInformation("Job {JobId} already in terminal state {Status}; skipping.", entity.Id, entity.Status);
            return;
        }

        var nextAttempt = entity.AttemptCount + 1;

        entity.Status = "Processing";
        entity.AttemptCount = nextAttempt;
        entity.StartedAtUtc ??= DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var webhookUrl = $"{_webhookBaseUrl}/api/webhooks/replicate";
            var imageUrl = GetReplicateAccessibleImageUrl(job.ImageUrl)
                ?? throw new InvalidOperationException($"Job {job.JobId} has no input_image URL; cannot call change-haircut model.");

            EnsureReplicateCanFetchImage(imageUrl);
            await EnsureImageUrlReachableAsync(imageUrl, cancellationToken);

            var normalizedHaircut = NormalizeHaircut(job.Haircut);
            var normalizedHairColor = NormalizeHairColor(job.HairColor);
            var normalizedBeardStyle = NormalizeBeardStyle(job.BeardStyle);
            var normalizedBeardColor = NormalizeBeardColor(job.BeardColor);

            var haircutInput = new HaircutStyleInput(
                InputImageUrl: imageUrl,
                Haircut: normalizedHaircut,
                HairColor: normalizedHairColor,
                BeardStyle: normalizedBeardStyle,
                BeardColor: normalizedBeardColor,
                Gender: job.Gender ?? "none"
            );
            var predictionId = await _replicate.CreatePredictionAsync(haircutInput, webhookUrl, cancellationToken);

            entity.ExternalPredictionId = predictionId;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Created Replicate prediction {PredictionId} for job {JobId}.",
                predictionId, entity.Id);
        }
        catch (ReplicateApiException ex) when (
            ex.StatusCode is HttpStatusCode.PaymentRequired
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.UnprocessableEntity)
        {
            var responseBody = ex.ResponseBody.Length > 2000
                ? ex.ResponseBody[..2000] + "..."
                : ex.ResponseBody;

            _logger.LogError(
                ex,
                "Replicate rejected job {JobId} with status {StatusCode}. Response body: {ResponseBody}. Marking as failed without retry.",
                entity.Id,
                (int)ex.StatusCode,
                responseBody);

            entity.Status = "Failed";
            entity.ErrorCode = "replicate_request_rejected";
            entity.ErrorMessage = TruncateForDb($"{ex.Message} Response: {responseBody}");
            entity.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("input_image", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                ex,
                "Job {JobId} has invalid input image and will not be submitted to Replicate.",
                entity.Id);

            entity.Status = "Failed";
            entity.ErrorCode = "invalid_input_image";
            entity.ErrorMessage = TruncateForDb(ex.Message);
            entity.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit job {JobId} to Replicate (attempt {Attempt}).", entity.Id, entity.AttemptCount);

            if (entity.AttemptCount >= entity.MaxAttempts)
            {
                entity.Status = "Failed";
                entity.ErrorCode = "replicate_submission_failed";
                entity.ErrorMessage = TruncateForDb(ex.Message);
                entity.CompletedAtUtc = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogWarning("Job {JobId} marked as Failed after {MaxAttempts} attempts.", entity.Id, entity.MaxAttempts);
            }
            else
            {
                // Re-queue with incremented attempt — leave message on queue by rethrowing
                entity.Status = "Queued";
                await _db.SaveChangesAsync(cancellationToken);
                throw; // Worker's retry logic will re-enqueue
            }
        }
    }

    private async Task EnsureImageUrlReachableAsync(string imageUrl, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient();
        using var headRequest = new HttpRequestMessage(HttpMethod.Head, imageUrl);
        using var headResponse = await client.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, ct);

        if (headResponse.IsSuccessStatusCode)
        {
            return;
        }

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        getRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);

        using var getResponse = await client.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (getResponse.IsSuccessStatusCode || getResponse.StatusCode == HttpStatusCode.PartialContent)
        {
            return;
        }

        throw new InvalidOperationException(
            $"input_image URL is not reachable (HEAD {(int)headResponse.StatusCode} {headResponse.ReasonPhrase}; GET {(int)getResponse.StatusCode} {getResponse.ReasonPhrase}). URL: {imageUrl}");
    }

    private string NormalizeHaircut(string? raw)
    {
        var value = (raw ?? "No change").Trim();
        if (value.Length == 0)
        {
            return "No change";
        }

        if (AllowedHaircuts.Contains(value))
        {
            return value;
        }

        if (HaircutAliases.TryGetValue(value, out var mapped) && AllowedHaircuts.Contains(mapped))
        {
            _logger.LogInformation("Mapped unsupported haircut '{Haircut}' to '{MappedHaircut}'.", value, mapped);
            return mapped;
        }

        _logger.LogWarning("Unsupported haircut '{Haircut}'. Falling back to 'No change'.", value);
        return "No change";
    }

    private string NormalizeHairColor(string? raw)
    {
        var value = (raw ?? "No change").Trim();
        if (value.Length == 0)
        {
            return "No change";
        }

        if (AllowedHairColors.Contains(value))
        {
            return value;
        }

        if (HairColorAliases.TryGetValue(value, out var mapped) && AllowedHairColors.Contains(mapped))
        {
            _logger.LogInformation("Mapped unsupported hair color '{HairColor}' to '{MappedHairColor}'.", value, mapped);
            return mapped;
        }

        _logger.LogWarning("Unsupported hair color '{HairColor}'. Falling back to 'No change'.", value);
        return "No change";
    }

    private string NormalizeBeardStyle(string? raw)
    {
        var value = (raw ?? "No change").Trim();
        if (value.Length == 0)
        {
            return "No change";
        }

        if (AllowedBeardStyles.Contains(value))
        {
            return value;
        }

        if (BeardStyleAliases.TryGetValue(value, out var mapped) && AllowedBeardStyles.Contains(mapped))
        {
            _logger.LogInformation("Mapped unsupported beard style '{BeardStyle}' to '{MappedBeardStyle}'.", value, mapped);
            return mapped;
        }

        _logger.LogWarning("Unsupported beard style '{BeardStyle}'. Falling back to 'No change'.", value);
        return "No change";
    }

    private string NormalizeBeardColor(string? raw)
    {
        var value = (raw ?? "No change").Trim();
        if (value.Length == 0)
        {
            return "No change";
        }

        if (AllowedBeardColors.Contains(value))
        {
            return value;
        }

        if (BeardColorAliases.TryGetValue(value, out var mapped) && AllowedBeardColors.Contains(mapped))
        {
            _logger.LogInformation("Mapped unsupported beard color '{BeardColor}' to '{MappedBeardColor}'.", value, mapped);
            return mapped;
        }

        _logger.LogWarning("Unsupported beard color '{BeardColor}'. Falling back to 'No change'.", value);
        return "No change";
    }

    private static string TruncateForDb(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= ErrorMessageMaxLength)
        {
            return value ?? string.Empty;
        }

        return value[..(ErrorMessageMaxLength - 3)] + "...";
    }

    private string? GetReplicateAccessibleImageUrl(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(_webhookBaseUrl) || !Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
        {
            return imageUrl;
        }

        var isLocalHost = uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

        if (!isLocalHost || uri.Port != 10000)
        {
            return imageUrl;
        }

        // Azurite path shape: /devstoreaccount1/{container}/{userId}/{fileName}
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4)
        {
            return imageUrl;
        }

        var container = segments[1];
        if (!container.Equals("user-uploads", StringComparison.OrdinalIgnoreCase))
        {
            return imageUrl;
        }

        var userId = segments[2];
        var fileName = segments[3];
        var publicUrl = $"{_webhookBaseUrl}/api/upload/public/{Uri.EscapeDataString(userId)}/{Uri.EscapeDataString(fileName)}";

        _logger.LogInformation(
            "Rewriting local blob URL for Replicate access. Local={LocalUrl}, Public={PublicUrl}",
            imageUrl,
            publicUrl);

        return publicUrl;
    }

    private static void EnsureReplicateCanFetchImage(string imageUrl)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"input_image must be an absolute URL. Value: '{imageUrl}'");
        }

        var isLocalHost = uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

        if (isLocalHost)
        {
            throw new InvalidOperationException(
                $"input_image URL is local-only ({imageUrl}) and cannot be fetched by Replicate. " +
                "Use a public URL (for local dev, ensure ngrok is running and Replicate:WebhookBaseUrl points to the active ngrok URL so image URLs are rewritten to /api/upload/public/...).");
        }
    }
}

