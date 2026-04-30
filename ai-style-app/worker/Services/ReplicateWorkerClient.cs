using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace AiStyleApp.Worker.Services;

public interface IReplicateWorkerClient
{
    Task<string> CreatePredictionAsync(string prompt, string webhookUrl, CancellationToken ct = default);
}

public class ReplicateWorkerClient : IReplicateWorkerClient
{
    private readonly HttpClient _http;
    private readonly string _modelVersion;

    public ReplicateWorkerClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _modelVersion = config["Replicate:ModelVersion"] ?? string.Empty;

        var token = config["Replicate:ApiToken"] ?? string.Empty;
        _http.BaseAddress = new Uri("https://api.replicate.com/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Token", token);
    }

    public async Task<string> CreatePredictionAsync(string prompt, string webhookUrl, CancellationToken ct = default)
    {
        var body = new
        {
            version = _modelVersion,
            input = new { prompt },
            webhook = webhookUrl,
            webhook_events_filter = new[] { "start", "completed" }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(body),
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await _http.PostAsync("v1/predictions", content, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var result = await JsonSerializer.DeserializeAsync<PredictionCreated>(stream, cancellationToken: ct);

        return result?.Id ?? throw new InvalidOperationException("Replicate did not return a prediction ID.");
    }
}

internal record PredictionCreated(
    [property: JsonPropertyName("id")] string Id
);
