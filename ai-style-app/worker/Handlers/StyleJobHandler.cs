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
            var stage = ResolveStage(job, entity);
            var imageUrl = GetReplicateAccessibleImageUrl(job.ImageUrl ?? entity.IntermediateImageUrl)
                ?? throw new InvalidOperationException($"Job {job.JobId} has no input_image URL; cannot call Replicate.");

            EnsureReplicateCanFetchImage(imageUrl);
            await EnsureImageUrlReachableAsync(imageUrl, cancellationToken);

            var predictionId = await SubmitStageAsync(stage, job, entity, imageUrl, webhookUrl, cancellationToken);

            entity.CurrentStage = stage;
            entity.ExternalPredictionId = predictionId;
            entity.ErrorCode = null;
            entity.ErrorMessage = null;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Created Replicate prediction {PredictionId} for job {JobId} stage {Stage}.",
                predictionId, entity.Id, stage);
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

    private string ResolveStage(StyleJob job, Data.Entities.StyleJobEntity entity)
    {
        if (string.Equals(job.Stage, StyleJobStage.Beard, StringComparison.OrdinalIgnoreCase))
        {
            return StyleJobStage.Beard;
        }

        if (string.Equals(job.Stage, StyleJobStage.Hair, StringComparison.OrdinalIgnoreCase))
        {
            return StyleJobStage.Hair;
        }

        if (string.Equals(entity.CurrentStage, StyleJobStage.Beard, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(entity.IntermediateImageUrl))
        {
            return StyleJobStage.Beard;
        }

        return StyleJobRouting.DeterminePipelineMode(
            entity.Haircut ?? job.Haircut,
            entity.HairColor ?? job.HairColor,
            entity.BeardStyle ?? job.BeardStyle,
            entity.BeardColor ?? job.BeardColor,
            entity.Gender ?? job.Gender) switch
        {
            StyleJobPipelineMode.BeardOnly => StyleJobStage.Beard,
            _ => StyleJobStage.Hair
        };
    }

    private async Task<string> SubmitStageAsync(
        string stage,
        StyleJob job,
        Data.Entities.StyleJobEntity entity,
        string imageUrl,
        string webhookUrl,
        CancellationToken cancellationToken)
    {
        if (string.Equals(stage, StyleJobStage.Beard, StringComparison.OrdinalIgnoreCase))
        {
            var beardPrompt = BuildBeardPrompt(entity.BeardStyle ?? job.BeardStyle, entity.BeardColor ?? job.BeardColor);
            if (string.IsNullOrWhiteSpace(beardPrompt))
            {
                throw new InvalidOperationException($"Job {job.JobId} has no beard change to submit.");
            }

            var beardInput = new BeardStyleInput(
                InputImageUrl: imageUrl,
                Prompt: beardPrompt
            );

            return await _replicate.CreateBeardPredictionAsync(beardInput, webhookUrl, cancellationToken);
        }

        var normalizedHaircut = NormalizeHaircut(entity.Haircut ?? job.Haircut);
        var normalizedHairColor = NormalizeHairColor(entity.HairColor ?? job.HairColor);

        var haircutInput = new HaircutStyleInput(
            InputImageUrl: imageUrl,
            Haircut: normalizedHaircut,
            HairColor: normalizedHairColor,
            Gender: entity.Gender ?? job.Gender ?? "none"
        );

        return await _replicate.CreateHairPredictionAsync(haircutInput, webhookUrl, cancellationToken);
    }

    private static string BuildBeardPrompt(string? beardStyle, string? beardColor)
    {
        var instructions = new List<string>
        {
            "Edit only the subject's facial hair.",
            "Preserve identity, scalp hair, hair color, face shape, pose, expression, lighting, clothing, and background."
        };

        if (StyleJobRouting.HasMeaningfulSelection(beardStyle))
        {
            instructions.Add(
                string.Equals(beardStyle?.Trim(), "Random", StringComparison.OrdinalIgnoreCase)
                    ? "Choose a flattering beard style."
                    : $"Set the beard or facial hair style to {beardStyle!.Trim()}.");
        }

        if (StyleJobRouting.HasMeaningfulSelection(beardColor))
        {
            instructions.Add(
                string.Equals(beardColor?.Trim(), "Random", StringComparison.OrdinalIgnoreCase)
                    ? "Choose a natural-looking facial hair color."
                    : $"Set the beard or facial hair color to {beardColor!.Trim()}.");
        }

        return instructions.Count > 2
            ? string.Join(" ", instructions)
            : string.Empty;
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
