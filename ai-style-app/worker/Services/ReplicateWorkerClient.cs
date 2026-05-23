using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading;

namespace AiStyleApp.Worker.Services;

/// <summary>Input parameters for the flux-kontext-apps/change-haircut deployment model.</summary>
public record HaircutStyleInput(
    string InputImageUrl,
    string Haircut = "No change",
    string HairColor = "No change",
    string Gender = "none",
    string AspectRatio = "match_input_image"
);

public record BeardStyleInput(
    string InputImageUrl,
    string Prompt,
    string AspectRatio = "match_input_image"
);

public interface IReplicateWorkerClient
{
    Task<string> CreateHairPredictionAsync(HaircutStyleInput input, string webhookUrl, CancellationToken ct = default);
    Task<string> CreateBeardPredictionAsync(BeardStyleInput input, string webhookUrl, CancellationToken ct = default);
}

public class ReplicateWorkerClient : IReplicateWorkerClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ReplicateWorkerClient> _logger;
    private readonly string _hairModelName;
    private readonly string _beardModelName;

    private const string PredictionsPath = "v1/predictions";
    private static readonly SemaphoreSlim VersionLock = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _cachedVersions = new();

    public ReplicateWorkerClient(HttpClient http, IConfiguration config, ILogger<ReplicateWorkerClient> logger)
    {
        _http = http;
        _logger = logger;
        _hairModelName = config["Replicate:HairModelName"] ?? "flux-kontext-apps/change-haircut";
        _beardModelName = config["Replicate:BeardModelName"] ?? "black-forest-labs/flux-kontext-pro";

        var token = config["Replicate:ApiToken"] ?? string.Empty;
        _http.BaseAddress = new Uri("https://api.replicate.com/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public Task<string> CreateHairPredictionAsync(HaircutStyleInput input, string webhookUrl, CancellationToken ct = default)
        => CreatePredictionAsync(
            _hairModelName,
            new
            {
                gender = input.Gender,
                haircut = input.Haircut,
                hair_color = input.HairColor,
                input_image = input.InputImageUrl,
                aspect_ratio = input.AspectRatio
            },
            webhookUrl,
            ct);

    public Task<string> CreateBeardPredictionAsync(BeardStyleInput input, string webhookUrl, CancellationToken ct = default)
        => CreatePredictionAsync(
            _beardModelName,
            new
            {
                prompt = input.Prompt,
                input_image = input.InputImageUrl,
                aspect_ratio = input.AspectRatio
            },
            webhookUrl,
            ct);

    private async Task<string> CreatePredictionAsync(string modelName, object input, string webhookUrl, CancellationToken ct = default)
    {
        var version = await GetModelVersionAsync(modelName, ct);

        var body = new
        {
            version,
            input,
            webhook = webhookUrl,
            webhook_events_filter = new[] { "start", "completed" }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(body),
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await _http.PostAsync(PredictionsPath, content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var trimmedBody = responseBody.Length > 2000
                ? responseBody[..2000] + "..."
                : responseBody;

            _logger.LogError(
                "Replicate prediction request failed with status {StatusCode} ({ReasonPhrase}). Response body: {ResponseBody}",
                (int)response.StatusCode,
                response.ReasonPhrase,
                trimmedBody);

            throw new ReplicateApiException(response.StatusCode, response.ReasonPhrase, responseBody);
        }

        var result = JsonSerializer.Deserialize<PredictionCreated>(responseBody);

        return result?.Id ?? throw new InvalidOperationException("Replicate did not return a prediction ID.");
    }

    private async Task<string> GetModelVersionAsync(string modelName, CancellationToken ct)
    {
        if (_cachedVersions.TryGetValue(modelName, out var cachedVersion)
            && !string.IsNullOrWhiteSpace(cachedVersion))
        {
            return cachedVersion;
        }

        await VersionLock.WaitAsync(ct);
        try
        {
            if (_cachedVersions.TryGetValue(modelName, out cachedVersion)
                && !string.IsNullOrWhiteSpace(cachedVersion))
            {
                return cachedVersion;
            }

            var response = await _http.GetAsync($"v1/models/{modelName}", ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new ReplicateApiException(response.StatusCode, response.ReasonPhrase, body);
            }

            var model = JsonSerializer.Deserialize<ModelDetails>(body);
            var version = model?.LatestVersion?.Id;

            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException($"Replicate model '{modelName}' did not return latest_version.id.");
            }

            _cachedVersions[modelName] = version;
            _logger.LogInformation("Resolved Replicate model version {Version} for {ModelName}.", version, modelName);
            return version;
        }
        finally
        {
            VersionLock.Release();
        }
    }
}

public sealed class ReplicateApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? ReasonPhrase { get; }
    public string ResponseBody { get; }

    public ReplicateApiException(HttpStatusCode statusCode, string? reasonPhrase, string responseBody)
        : base($"Replicate API request failed with status {(int)statusCode} ({reasonPhrase ?? "Unknown"}).")
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        ResponseBody = responseBody;
    }
}

internal record PredictionCreated(
    [property: JsonPropertyName("id")] string Id
);

internal record ModelDetails(
    [property: JsonPropertyName("latest_version")] ModelVersion? LatestVersion
);

internal record ModelVersion(
    [property: JsonPropertyName("id")] string Id
);
