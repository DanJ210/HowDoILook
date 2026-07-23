using System.Net;
using System.Text;
using System.Text.Json;
using AiStyleApp.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiStyleApp.Tests;

public class ReplicateBeardPayloadContractTests
{
    // Snapshot source: https://replicate.com/black-forest-labs/flux-kontext-pro/api/schema (captured 2026-07-22)
    private static readonly HashSet<string> ExpectedAspectRatios = new(StringComparer.Ordinal)
    {
        "match_input_image", "1:1", "16:9", "9:16", "4:3", "3:4", "3:2", "2:3", "4:5", "5:4", "21:9", "9:21", "2:1", "1:2"
    };

    [Fact]
    public async Task CreateBeardPredictionAsync_IncludesRequiredPromptAndInputImage()
    {
        var handler = new CaptureReplicateHttpMessageHandler();
        var client = CreateClient(handler);

        var input = new BeardStyleInput(
            InputImageUrl: "https://example.com/input.jpg",
            Prompt: "Edit only facial hair. Keep everything else unchanged.");

        _ = await client.CreateBeardPredictionAsync(input, "https://example.com/api/webhooks/replicate");

        var payload = ParseCapturedPayload(handler);
        var inputObject = payload.GetProperty("input");

        Assert.Equal(input.Prompt, inputObject.GetProperty("prompt").GetString());
        Assert.Equal(input.InputImageUrl, inputObject.GetProperty("input_image").GetString());
    }

    [Fact]
    public async Task CreateBeardPredictionAsync_UsesSchemaDefaultAspectRatio()
    {
        var handler = new CaptureReplicateHttpMessageHandler();
        var client = CreateClient(handler);

        var input = new BeardStyleInput(
            InputImageUrl: "https://example.com/input.jpg",
            Prompt: "Edit only facial hair.");

        _ = await client.CreateBeardPredictionAsync(input, "https://example.com/api/webhooks/replicate");

        var payload = ParseCapturedPayload(handler);
        var inputObject = payload.GetProperty("input");
        var aspectRatio = inputObject.GetProperty("aspect_ratio").GetString();

        Assert.NotNull(aspectRatio);
        Assert.Equal("match_input_image", aspectRatio);
        Assert.Contains(aspectRatio!, ExpectedAspectRatios);
    }

    [Fact]
    public async Task CreateBeardPredictionAsync_SerializesExplicitAspectRatio()
    {
        var handler = new CaptureReplicateHttpMessageHandler();
        var client = CreateClient(handler);

        const string explicitAspectRatio = "1:1";
        var input = new BeardStyleInput(
            InputImageUrl: "https://example.com/input.jpg",
            Prompt: "Edit only facial hair.",
            AspectRatio: explicitAspectRatio);

        _ = await client.CreateBeardPredictionAsync(input, "https://example.com/api/webhooks/replicate");

        var payload = ParseCapturedPayload(handler);
        var inputObject = payload.GetProperty("input");
        var aspectRatio = inputObject.GetProperty("aspect_ratio").GetString();

        Assert.NotNull(aspectRatio);
        Assert.Equal(explicitAspectRatio, aspectRatio);
        Assert.Contains(aspectRatio!, ExpectedAspectRatios);
    }

    private static ReplicateWorkerClient CreateClient(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Replicate:ApiToken"] = "test-token",
                ["Replicate:BeardModelName"] = "black-forest-labs/flux-kontext-pro"
            })
            .Build();

        var httpClient = new HttpClient(handler);
        return new ReplicateWorkerClient(httpClient, config, NullLogger<ReplicateWorkerClient>.Instance);
    }

    private static JsonElement ParseCapturedPayload(CaptureReplicateHttpMessageHandler handler)
    {
        Assert.NotNull(handler.CapturedPredictionRequestBody);

        using var doc = JsonDocument.Parse(handler.CapturedPredictionRequestBody!);
        return doc.RootElement.Clone();
    }

    private sealed class CaptureReplicateHttpMessageHandler : HttpMessageHandler
    {
        public string? CapturedPredictionRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.Contains("/v1/models/", StringComparison.Ordinal) == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"latest_version\":{\"id\":\"ver_123\"}}", Encoding.UTF8, "application/json")
                };
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath.EndsWith("/v1/predictions", StringComparison.Ordinal) == true)
            {
                CapturedPredictionRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":\"pred_123\"}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
