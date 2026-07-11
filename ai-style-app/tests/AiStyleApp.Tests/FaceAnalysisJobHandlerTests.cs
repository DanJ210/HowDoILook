using System.Net;
using System.Text.Json;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using AiStyleApp.Data.Queue;
using AiStyleApp.Worker.Handlers;
using AiStyleApp.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiStyleApp.Tests;

public class FaceAnalysisJobHandlerTests
{
    [Fact]
    public async Task HandleAsync_InvalidImageUrl_MarksJobFailedWithoutInvokingPipeline()
    {
        await using var db = CreateDbContext();
        var analysisJob = await SeedAnalysisJobAsync(db, imageUrl: "relative/image.jpg");

        var pipeline = new StubFaceAnalysisPipeline();
        var handler = CreateHandler(db, pipeline, new StaticStatusHttpClientFactory(HttpStatusCode.OK, HttpStatusCode.OK));

        await handler.HandleAsync(CreateMessageBody(analysisJob), CancellationToken.None);

        var persisted = await db.FaceAnalysisJobs.FirstAsync(x => x.Id == analysisJob.Id);
        Assert.Equal("Failed", persisted.Status);
        Assert.Equal("ANALYSIS_IMAGE_UNREACHABLE", persisted.ErrorCode);
        Assert.Equal("Image URL must be absolute.", persisted.ErrorMessage);
        Assert.Equal(0, pipeline.Calls);
    }

    [Fact]
    public async Task HandleAsync_QualityGateFailure_MarksJobFailedWithPipelineCode()
    {
        await using var db = CreateDbContext();
        var analysisJob = await SeedAnalysisJobAsync(db);

        var pipeline = new StubFaceAnalysisPipeline
        {
            AnalyzeException = new FaceAnalysisException(
                "ANALYSIS_QUALITY_TOO_BLURRY",
                "Image appears too blurry. Try a sharper photo with better focus.")
        };

        var handler = CreateHandler(db, pipeline, new StaticStatusHttpClientFactory(HttpStatusCode.OK, HttpStatusCode.OK));

        await handler.HandleAsync(CreateMessageBody(analysisJob), CancellationToken.None);

        var persisted = await db.FaceAnalysisJobs.FirstAsync(x => x.Id == analysisJob.Id);
        Assert.Equal("Failed", persisted.Status);
        Assert.Equal("ANALYSIS_QUALITY_TOO_BLURRY", persisted.ErrorCode);
        Assert.Equal("Image appears too blurry. Try a sharper photo with better focus.", persisted.ErrorMessage);
        Assert.NotNull(persisted.StartedAtUtc);
        Assert.NotNull(persisted.CompletedAtUtc);
        Assert.Equal(1, pipeline.Calls);
    }

    [Fact]
    public async Task HandleAsync_Success_PersistsRecommendationsAndConfidence()
    {
        await using var db = CreateDbContext();
        var analysisJob = await SeedAnalysisJobAsync(
            db,
            preferencesJson: "{\"allowBeardSuggestions\":false,\"allowHairColorChange\":true}");

        const string recommendationsJson = "[{\"styleId\":\"textured-crop\",\"score\":0.941}]";
        const string featureVectorJson = "{\"schemaVersion\":2,\"source\":\"worker-v1-staged-analysis\"}";

        var pipeline = new StubFaceAnalysisPipeline
        {
            AnalyzeResult = new FaceAnalysisPipelineResult(
                QualityPassed: true,
                QualityFailureCode: null,
                QualityMessage: null,
                FeatureVectorJson: featureVectorJson,
                AnalysisConfidence: 0.941,
                RecommendationsJson: recommendationsJson)
        };

        var handler = CreateHandler(db, pipeline, new StaticStatusHttpClientFactory(HttpStatusCode.OK, HttpStatusCode.OK));

        await handler.HandleAsync(CreateMessageBody(analysisJob), CancellationToken.None);

        var persisted = await db.FaceAnalysisJobs.FirstAsync(x => x.Id == analysisJob.Id);
        Assert.Equal("Succeeded", persisted.Status);
        Assert.True(persisted.QualityPassed);
        Assert.Equal(featureVectorJson, persisted.FeatureVectorJson);
        Assert.Equal(0.941, persisted.AnalysisConfidence);
        Assert.Equal(recommendationsJson, persisted.RecommendationsJson);
        Assert.Equal(analysisJob.PreferencesJson, pipeline.LastPreferencesJson);
        Assert.NotNull(persisted.StartedAtUtc);
        Assert.NotNull(persisted.CompletedAtUtc);
    }

    private static FaceAnalysisJobHandler CreateHandler(
        AppDbContext db,
        StubFaceAnalysisPipeline pipeline,
        IHttpClientFactory httpClientFactory)
    {
        return new FaceAnalysisJobHandler(
            db,
            httpClientFactory,
            pipeline,
            NullLogger<FaceAnalysisJobHandler>.Instance);
    }

    private static string CreateMessageBody(FaceAnalysisJobEntity analysisJob)
    {
        var message = new StyleJob(
            JobId: analysisJob.Id,
            StyleItemId: Guid.Empty,
            UserId: analysisJob.UserId,
            JobType: "face-analysis",
            Prompt: "analyze",
            EnqueuedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: Guid.NewGuid().ToString(),
            Attempt: 0,
            SchemaVersion: 2,
            ImageUrl: analysisJob.ImageUrl,
            Gender: analysisJob.Gender,
            PreferencesJson: analysisJob.PreferencesJson);

        return JsonSerializer.Serialize(message);
    }

    private static async Task<FaceAnalysisJobEntity> SeedAnalysisJobAsync(
        AppDbContext db,
        string imageUrl = "https://example.com/input.jpg",
        string? preferencesJson = null)
    {
        var entity = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = imageUrl,
            Gender = "male",
            PreferencesJson = preferencesJson,
            Status = "Queued"
        };

        db.FaceAnalysisJobs.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private sealed class StubFaceAnalysisPipeline : IFaceAnalysisPipeline
    {
        public int Calls { get; private set; }
        public string? LastPreferencesJson { get; private set; }
        public Exception? AnalyzeException { get; init; }

        public FaceAnalysisPipelineResult AnalyzeResult { get; init; } =
            new(
                QualityPassed: true,
                QualityFailureCode: null,
                QualityMessage: null,
                FeatureVectorJson: "{}",
                AnalysisConfidence: 0.9,
                RecommendationsJson: "[]");

        public Task<FaceAnalysisPipelineResult> AnalyzeAsync(
            string imageUrl,
            string? gender,
            string? preferencesJson,
            CancellationToken ct)
        {
            Calls++;
            LastPreferencesJson = preferencesJson;

            if (AnalyzeException is not null)
            {
                throw AnalyzeException;
            }

            return Task.FromResult(AnalyzeResult);
        }
    }

    private sealed class StaticStatusHttpClientFactory(HttpStatusCode headStatus, HttpStatusCode getStatus) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new StaticStatusHttpMessageHandler(headStatus, getStatus));
    }

    private sealed class StaticStatusHttpMessageHandler(HttpStatusCode headStatus, HttpStatusCode getStatus) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var statusCode = request.Method == HttpMethod.Head ? headStatus : getStatus;
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}