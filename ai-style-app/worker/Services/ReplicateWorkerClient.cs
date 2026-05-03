using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;

namespace AiStyleApp.Worker.Services;

public interface IReplicateWorkerClient
{
    Task<string> CreatePredictionAsync(string prompt, string webhookUrl, string? imageUrl = null, CancellationToken ct = default);
}

public class ReplicateWorkerClient : IReplicateWorkerClient
{
    private readonly HttpClient _http;
    private readonly string _modelVersion;
    private readonly ILogger<ReplicateWorkerClient> _logger;
    private readonly double _imagePromptStrength;

    public ReplicateWorkerClient(HttpClient http, IConfiguration config, ILogger<ReplicateWorkerClient> logger)
    {
        _http = http;
        _logger = logger;
        _modelVersion = config["Replicate:ModelVersion"] ?? string.Empty;
        _imagePromptStrength = config.GetValue("Replicate:ImagePromptStrength", 0.35d);

        var token = config["Replicate:ApiToken"] ?? string.Empty;
        _http.BaseAddress = new Uri("https://api.replicate.com/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<string> CreatePredictionAsync(string prompt, string webhookUrl, string? imageUrl = null, CancellationToken ct = default)
    {
        object input = imageUrl is not null
            ? new { prompt, image = imageUrl, prompt_strength = _imagePromptStrength, go_fast = false }
            : new { prompt };

        var body = new
        {
            version = _modelVersion,
            input,
            webhook = webhookUrl,
            webhook_events_filter = new[] { "start", "completed" }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(body),
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await _http.PostAsync("v1/predictions", content, ct);
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
