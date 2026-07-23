using System.Net;
using System.Text;
using System.Text.Json;
using AiStyleApp.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiStyleApp.Tests;

public class ReplicateHairPayloadContractTests
{
    [Fact]
    public async Task CreateHairPredictionAsync_UsesSchemaDefaultsForGenderAndAspectRatio()
    {
        var handler = new CaptureReplicateHttpMessageHandler();
        var client = CreateClient(handler);

        var input = new HaircutStyleInput(
            InputImageUrl: "https://example.com/input.jpg",
            Haircut: "Layered",
            HairColor: "Honey Blonde");

        _ = await client.CreateHairPredictionAsync(input, "https://example.com/api/webhooks/replicate");

        var payload = ParseCapturedPayload(handler);
        var inputObject = payload.GetProperty("input");

        Assert.Equal("none", inputObject.GetProperty("gender").GetString());
        Assert.Equal("match_input_image", inputObject.GetProperty("aspect_ratio").GetString());
    }

    [Fact]
    public async Task CreateHairPredictionAsync_SerializesExplicitGenderAndAspectRatio()
    {
        var handler = new CaptureReplicateHttpMessageHandler();
        var client = CreateClient(handler);

        var input = new HaircutStyleInput(
            InputImageUrl: "https://example.com/input.jpg",
            Haircut: "Layered",
            HairColor: "Honey Blonde",
            Gender: "male",
            AspectRatio: "1:1");

        _ = await client.CreateHairPredictionAsync(input, "https://example.com/api/webhooks/replicate");

        var payload = ParseCapturedPayload(handler);
        var inputObject = payload.GetProperty("input");

        Assert.Equal("male", inputObject.GetProperty("gender").GetString());
        Assert.Equal("1:1", inputObject.GetProperty("aspect_ratio").GetString());
    }

    private static ReplicateWorkerClient CreateClient(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Replicate:ApiToken"] = "test-token",
                ["Replicate:HairModelName"] = "flux-kontext-apps/change-haircut"
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
