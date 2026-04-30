using AiStyleApp.Api.Infrastructure;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiStyleApp.Api.Services;

public interface IReplicateClient
{
    Task<string> CreatePredictionAsync(string prompt, string webhookUrl, CancellationToken ct = default);
}

public class ReplicateClient : IReplicateClient
{
    private readonly HttpClient _http;
    private readonly ReplicateOptions _options;

    public ReplicateClient(HttpClient http, IOptions<ReplicateOptions> options)
    {
        _http = http;
        _options = options.Value;
        _http.BaseAddress = new Uri("https://api.replicate.com/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Token", _options.ApiToken);
    }

    public async Task<string> CreatePredictionAsync(string prompt, string webhookUrl, CancellationToken ct = default)
    {
        var body = new
        {
            version = _options.ModelVersion,
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
        var result = await JsonSerializer.DeserializeAsync<ReplicatePredictionResponse>(stream, cancellationToken: ct);

        return result?.Id ?? throw new InvalidOperationException("Replicate did not return a prediction ID.");
    }
}

internal record ReplicatePredictionResponse(
    [property: JsonPropertyName("id")] string Id
);
