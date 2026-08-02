using System.Net;
using System.Text.Json;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using AiStyleApp.Data.Queue;
using AiStyleApp.Worker.Handlers;
using AiStyleApp.Worker.Services;
using AiStyleApp.Worker.Services.Onnx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.FileProviders;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiStyleApp.Tests;

public class FaceAnalysisJobHandlerTests
{
    [Fact]
    public async Task HandleAsync_InvalidImageUrl_MarksJobFailedWithoutInvokingPipeline()
    {
        await using var db = CreateDbContext();
        var analysisJob = await SeedAnalysisJobAsync(db, imageUrl: "relative/image.jpg");

        var pipeline = new StubFaceAnalysisPipeline();
        var queue = new StubWorkerQueuePublisher();
        var handler = CreateHandler(db, pipeline, new StaticStatusHttpClientFactory(HttpStatusCode.OK, HttpStatusCode.OK), queue);

        await handler.HandleAsync(CreateMessageBody(analysisJob), CancellationToken.None);

        var persisted = await db.FaceAnalysisJobs.FirstAsync(x => x.Id == analysisJob.Id);
        Assert.Equal("Failed", persisted.Status);
        Assert.Equal("ANALYSIS_IMAGE_UNREACHABLE", persisted.ErrorCode);
        Assert.StartsWith("Image URL must be", persisted.ErrorMessage);
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

        var queue = new StubWorkerQueuePublisher();
        var handler = CreateHandler(db, pipeline, new StaticStatusHttpClientFactory(HttpStatusCode.OK, HttpStatusCode.OK), queue);

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

        var queue = new StubWorkerQueuePublisher();
        var handler = CreateHandler(db, pipeline, new StaticStatusHttpClientFactory(HttpStatusCode.OK, HttpStatusCode.OK), queue);

        await handler.HandleAsync(CreateMessageBody(analysisJob), CancellationToken.None);

        var persisted = await db.FaceAnalysisJobs.FirstAsync(x => x.Id == analysisJob.Id);
        Assert.Equal("Succeeded", persisted.Status);
        Assert.True(persisted.QualityPassed);
        Assert.Equal(featureVectorJson, persisted.FeatureVectorJson);
        Assert.Equal(0.941, persisted.AnalysisConfidence);
        Assert.Equal(recommendationsJson, persisted.RecommendationsJson);
        Assert.Equal(analysisJob.PreferencesJson, pipeline.LastPreferencesJson);
        Assert.Single(queue.Messages);
        Assert.NotNull(persisted.StartedAtUtc);
        Assert.NotNull(persisted.CompletedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_RealOnnxLandmarks_SucceedsAndPersistsTelemetry()
    {
        await using var db = CreateDbContext();
        var analysisJob = await SeedAnalysisJobAsync(db);

        var imageBytes = CreateSyntheticFaceImageBytes();
        var httpClientFactory = new StaticImageHttpClientFactory(imageBytes);
        var pipeline = CreateRealPipeline(httpClientFactory);
        var handler = CreateHandler(db, pipeline, httpClientFactory, new StubWorkerQueuePublisher());

        await handler.HandleAsync(CreateMessageBody(analysisJob), CancellationToken.None);

        var persisted = await db.FaceAnalysisJobs.FirstAsync(x => x.Id == analysisJob.Id);
        Assert.True(
            persisted.Status == "Succeeded",
            $"Job failed with {persisted.ErrorCode}: {persisted.ErrorMessage}");
        Assert.NotNull(persisted.FeatureVectorJson);

        using var document = JsonDocument.Parse(persisted.FeatureVectorJson!);
        var landmarkStage = document.RootElement.GetProperty("stageTelemetry")
            .EnumerateArray()
            .Single(stage => stage.GetProperty("Stage").GetString() == "landmarks");

        Assert.Equal("onnx-landmarks", landmarkStage.GetProperty("Model").GetString());
        Assert.Equal("v1", landmarkStage.GetProperty("ModelVersion").GetString());

        var metrics = landmarkStage.GetProperty("Metrics");
        Assert.InRange(metrics.GetProperty("landmarkConfidence").GetDouble(), 0.0, 1.0);
        Assert.InRange(metrics.GetProperty("yaw").GetDouble(), -1.0, 1.0);
        Assert.InRange(metrics.GetProperty("pitch").GetDouble(), -1.0, 1.0);
    }

        [Fact]
        public async Task HandleAsync_ExperimentApplied_EnqueuesPrimaryAndThreeExperimentalVariants()
        {
                await using var db = CreateDbContext();
                var analysisJob = await SeedAnalysisJobAsync(db, preferencesJson: "{\"allowBeardSuggestions\":true}");

                const string recommendationsJson = """
                [
                    {"styleId":"textured-crop","styleName":"Textured Crop","score":0.94,"reasons":["r1"],"constraints":[]},
                    {"styleId":"classic-side-part","styleName":"Classic Side Part","score":0.90,"reasons":["r2"],"constraints":[]},
                    {"styleId":"short-quiff","styleName":"Short Quiff","score":0.88,"reasons":["r3"],"constraints":[]},
                    {"styleId":"short-boxed-beard","styleName":"Short Boxed Beard Pairing","score":0.86,"reasons":["r4"],"constraints":[]},
                    {"styleId":"other-style","styleName":"Other Style","score":0.80,"reasons":["r5"],"constraints":[]}
                ]
                """;

                const string featureVectorJson = """
                {
                    "experiment": {
                        "enabled": true,
                        "trafficPercent": 100,
                        "applied": true,
                        "bucketKey": "user-hash-01"
                    }
                }
                """;

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

                var queue = new StubWorkerQueuePublisher();
                var handler = CreateHandler(db, pipeline, new StaticStatusHttpClientFactory(HttpStatusCode.OK, HttpStatusCode.OK), queue);

                await handler.HandleAsync(CreateMessageBody(analysisJob), CancellationToken.None);

                Assert.Equal(4, queue.Messages.Count);

                var styleItems = await db.StyleItems.AsNoTracking().OrderBy(x => x.CreatedAtUtc).ToListAsync();
                Assert.Equal(4, styleItems.Count);
                Assert.True(styleItems[0].IsResultPublic);
                Assert.False(styleItems[1].IsResultPublic);
                Assert.False(styleItems[2].IsResultPublic);
                Assert.False(styleItems[3].IsResultPublic);

                var persisted = await db.FaceAnalysisJobs.AsNoTracking().SingleAsync(x => x.Id == analysisJob.Id);
                Assert.Equal("textured-crop", persisted.PrimaryStyleId);
                Assert.Equal(styleItems[0].Id, persisted.PrimaryStyleItemId);
                Assert.Equal(queue.Messages[0].JobId, persisted.PrimaryGenerationJobId);

                var exposure = await db.RecommendationExposures
                    .AsNoTracking()
                    .Include(x => x.Candidates)
                    .SingleAsync(x => x.AnalysisJobId == analysisJob.Id);
                Assert.Equal("controlled-exploration-v1", exposure.ExperimentVersion);
                Assert.True(exposure.ExperimentApplied);
                Assert.Equal("textured-crop", exposure.PrimaryStyleId);
                Assert.Equal(queue.Messages[0].JobId, exposure.PrimaryGenerationJobId);
                Assert.Equal(4, exposure.Candidates.Count);
                Assert.All(exposure.Candidates, candidate => Assert.True(candidate.WasShown));
                Assert.All(exposure.Candidates, candidate => Assert.Equal(1, candidate.SelectionProbability));
                Assert.Equal([1, 2, 3, 4], exposure.Candidates.OrderBy(x => x.ShownOrder).Select(x => x.ShownOrder));
                Assert.DoesNotContain(exposure.Candidates, candidate => candidate.StyleId == "other-style");
        }

        [Fact]
        public async Task HandleAsync_ExperimentNotApplied_EnqueuesOnlyPrimaryVariant()
        {
                await using var db = CreateDbContext();
                var analysisJob = await SeedAnalysisJobAsync(db);

                const string recommendationsJson = """
                [
                    {"styleId":"textured-crop","styleName":"Textured Crop","score":0.94,"reasons":["r1"],"constraints":[]},
                    {"styleId":"classic-side-part","styleName":"Classic Side Part","score":0.90,"reasons":["r2"],"constraints":[]}
                ]
                """;

                const string featureVectorJson = """
                {
                    "experiment": {
                        "enabled": true,
                        "trafficPercent": 100,
                        "applied": false,
                        "bucketKey": "user-hash-81"
                    }
                }
                """;

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

                var queue = new StubWorkerQueuePublisher();
                var handler = CreateHandler(db, pipeline, new StaticStatusHttpClientFactory(HttpStatusCode.OK, HttpStatusCode.OK), queue);

                await handler.HandleAsync(CreateMessageBody(analysisJob), CancellationToken.None);

                Assert.Single(queue.Messages);

                var styleItems = await db.StyleItems.AsNoTracking().ToListAsync();
                Assert.Single(styleItems);
                Assert.True(styleItems[0].IsResultPublic);

                var exposure = await db.RecommendationExposures
                    .AsNoTracking()
                    .Include(x => x.Candidates)
                    .SingleAsync(x => x.AnalysisJobId == analysisJob.Id);
                Assert.False(exposure.ExperimentApplied);
                Assert.Equal(2, exposure.Candidates.Count);
                Assert.True(exposure.Candidates.Single(x => x.IsPrimary).WasShown);
                var challenger = exposure.Candidates.Single(x => !x.IsPrimary);
                Assert.False(challenger.WasShown);
                Assert.Equal(0, challenger.SelectionProbability);
                Assert.Null(challenger.GenerationJobId);
        }

    [Fact]
    public async Task HandleAsync_UnknownRecommendedStyleId_FailsWithTemplateMappingError()
    {
        await using var db = CreateDbContext();
        var analysisJob = await SeedAnalysisJobAsync(db);

        const string recommendationsJson = """
        [
            {"styleId":"unmapped-style-id","styleName":"Unmapped Style","score":0.99,"reasons":["r1"],"constraints":[]}
        ]
        """;

        var pipeline = new StubFaceAnalysisPipeline
        {
            AnalyzeResult = new FaceAnalysisPipelineResult(
                QualityPassed: true,
                QualityFailureCode: null,
                QualityMessage: null,
                FeatureVectorJson: "{}",
                AnalysisConfidence: 0.99,
                RecommendationsJson: recommendationsJson)
        };

        var queue = new StubWorkerQueuePublisher();
        var handler = CreateHandler(db, pipeline, new StaticStatusHttpClientFactory(HttpStatusCode.OK, HttpStatusCode.OK), queue);

        await handler.HandleAsync(CreateMessageBody(analysisJob), CancellationToken.None);

        var persisted = await db.FaceAnalysisJobs.FirstAsync(x => x.Id == analysisJob.Id);
        Assert.Equal("Failed", persisted.Status);
        Assert.Equal("ANALYSIS_RECOMMENDATION_TEMPLATE_MISSING", persisted.ErrorCode);
        Assert.Contains("unmapped-style-id", persisted.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.Empty(queue.Messages);
    }

        [Fact]
        public async Task HandleAsync_ReusesExistingPrimaryRecommendationPostPlaceholder()
        {
                await using var db = CreateDbContext();
                var analysisJob = await SeedAnalysisJobAsync(db);

                var placeholder = new StyleItemEntity
                {
                        UserId = analysisJob.UserId,
                        Name = "Recommendation Post (Processing)",
                        Description = $"Primary recommendation from analysis job {analysisJob.Id}. Pending analysis.",
                        Prompt = "Pending recommendation generation",
                        ImageUrl = analysisJob.ImageUrl,
                        IsResultPublic = true
                };

                db.StyleItems.Add(placeholder);
                await db.SaveChangesAsync();

                const string recommendationsJson = """
                [
                    {"styleId":"textured-crop","styleName":"Textured Crop","score":0.94,"reasons":["r1"],"constraints":[]}
                ]
                """;

                const string featureVectorJson = """
                {
                    "experiment": {
                        "enabled": true,
                        "trafficPercent": 100,
                        "applied": false,
                        "bucketKey": "user-hash-01"
                    }
                }
                """;

                var pipeline = new StubFaceAnalysisPipeline
                {
                        AnalyzeResult = new FaceAnalysisPipelineResult(
                                QualityPassed: true,
                                QualityFailureCode: null,
                                QualityMessage: null,
                                FeatureVectorJson: featureVectorJson,
                                AnalysisConfidence: 0.94,
                                RecommendationsJson: recommendationsJson)
                };

                var queue = new StubWorkerQueuePublisher();
                var handler = CreateHandler(db, pipeline, new StaticStatusHttpClientFactory(HttpStatusCode.OK, HttpStatusCode.OK), queue);

                await handler.HandleAsync(CreateMessageBody(analysisJob), CancellationToken.None);

                Assert.Single(queue.Messages);
                var allPublicItems = await db.StyleItems.AsNoTracking().Where(x => x.IsResultPublic).ToListAsync();
                Assert.Single(allPublicItems);
                Assert.Equal(placeholder.Id, allPublicItems[0].Id);
                Assert.StartsWith("Recommended:", allPublicItems[0].Name, StringComparison.Ordinal);
        }

    private static FaceAnalysisJobHandler CreateHandler(
        AppDbContext db,
        IFaceAnalysisPipeline pipeline,
        IHttpClientFactory httpClientFactory,
        IWorkerQueuePublisher? queuePublisher = null)
    {
        return new FaceAnalysisJobHandler(
            db,
            httpClientFactory,
            pipeline,
            queuePublisher ?? new StubWorkerQueuePublisher(),
            NullLogger<FaceAnalysisJobHandler>.Instance,
            new StubMetricsLogger());
    }

    private static FaceAnalysisPipeline CreateRealPipeline(IHttpClientFactory httpClientFactory)
    {
        var landmarkModelPath = FindWorkerModelPath("fan2_68_landmark.onnx");
        var landmarkStage = new OnnxFaceLandmarkStage(
            new OnnxSessionFactory(),
            Options.Create(new OnnxLandmarkOptions
            {
                ModelPath = landmarkModelPath,
                ExecutionProvider = "CPU"
            }),
            new TestHostEnvironment(),
            NullLogger<OnnxFaceLandmarkStage>.Instance);

        return new FaceAnalysisPipeline(
            httpClientFactory,
            new HeuristicFaceDetectorStage(),
            new HeuristicFaceQualityStage(),
            landmarkStage,
            new HeuristicFaceLandmarkStage(),
            new HeuristicFaceRegionEstimationStage(),
            new HeuristicFaceSegmentationStage(),
            new RuleBasedRecommendationStage(),
            Options.Create(new WorkerFeatureFlags
            {
                OnnxFaceDetection = false,
                OnnxLandmarks = true,
                OnnxRegionEstimation = false
            }),
            Options.Create(new FaceAnalysisThresholds
            {
                MinLandmarkConfidence = 0.0
            }),
            NullLogger<FaceAnalysisPipeline>.Instance);
    }

    private static string FindWorkerModelPath(string modelFileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "worker", "Services", "Onnx", "Models", modelFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Unable to locate worker model file '{modelFileName}'.");
    }

    private static byte[] CreateSyntheticFaceImageBytes()
    {
        const int size = 1536;
        using var image = new Image<Rgba32>(size, size);
        var center = size / 2;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var cell = ((x / 12) + (y / 12)) % 2 == 0;
                image[x, y] = cell
                    ? new Rgba32(220, 220, 220)
                    : new Rgba32(40, 40, 40);
            }
        }

        PaintEllipse(image, center, (int)(size * 0.56), (int)(size * 0.24), (int)(size * 0.32), new Rgba32(232, 198, 176));
        PaintEllipse(image, center, (int)(size * 0.21), (int)(size * 0.29), (int)(size * 0.16), new Rgba32(66, 45, 34));
        PaintEllipse(image, (int)(size * 0.44), (int)(size * 0.45), (int)(size * 0.05), (int)(size * 0.03), new Rgba32(255, 255, 255));
        PaintEllipse(image, (int)(size * 0.60), (int)(size * 0.44), (int)(size * 0.05), (int)(size * 0.03), new Rgba32(255, 255, 255));
        PaintCircle(image, (int)(size * 0.44), (int)(size * 0.45), (int)(size * 0.015), new Rgba32(25, 25, 25));
        PaintCircle(image, (int)(size * 0.60), (int)(size * 0.44), (int)(size * 0.015), new Rgba32(25, 25, 25));
        PaintRect(image, (int)(size * 0.48), (int)(size * 0.49), (int)(size * 0.52), (int)(size * 0.63), new Rgba32(162, 127, 106));
        PaintEllipse(image, center, (int)(size * 0.65), (int)(size * 0.15), (int)(size * 0.02), new Rgba32(118, 54, 60));
        PaintRect(image, (int)(size * 0.37), (int)(size * 0.43), (int)(size * 0.49), (int)(size * 0.44), new Rgba32(90, 62, 50));
        PaintRect(image, (int)(size * 0.56), (int)(size * 0.42), (int)(size * 0.68), (int)(size * 0.43), new Rgba32(90, 62, 50));
        PaintEllipse(image, (int)(size * 0.40), (int)(size * 0.58), (int)(size * 0.05), (int)(size * 0.04), new Rgba32(214, 166, 145));
        PaintEllipse(image, (int)(size * 0.64), (int)(size * 0.58), (int)(size * 0.06), (int)(size * 0.05), new Rgba32(205, 157, 136));

        using var stream = new MemoryStream();
        image.Save(stream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
        return stream.ToArray();
    }

    private static void PaintCircle(Image<Rgba32> image, int centerX, int centerY, int radius, Rgba32 color)
        => PaintEllipse(image, centerX, centerY, radius, radius, color);

    private static void PaintEllipse(Image<Rgba32> image, int centerX, int centerY, int radiusX, int radiusY, Rgba32 color)
    {
        var xStart = Math.Max(0, centerX - radiusX);
        var xEnd = Math.Min(image.Width - 1, centerX + radiusX);
        var yStart = Math.Max(0, centerY - radiusY);
        var yEnd = Math.Min(image.Height - 1, centerY + radiusY);

        for (var y = yStart; y <= yEnd; y++)
        {
            for (var x = xStart; x <= xEnd; x++)
            {
                var dx = (x - centerX) / (double)Math.Max(1, radiusX);
                var dy = (y - centerY) / (double)Math.Max(1, radiusY);

                if ((dx * dx) + (dy * dy) <= 1.0)
                {
                    image[x, y] = color;
                }
            }
        }
    }

    private static void PaintRect(Image<Rgba32> image, int x1, int y1, int x2, int y2, Rgba32 color)
    {
        var left = Math.Max(0, Math.Min(x1, x2));
        var right = Math.Min(image.Width - 1, Math.Max(x1, x2));
        var top = Math.Max(0, Math.Min(y1, y2));
        var bottom = Math.Min(image.Height - 1, Math.Max(y1, y2));

        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                image[x, y] = color;
            }
        }
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

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = nameof(FaceAnalysisJobHandlerTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StaticImageHttpClientFactory(byte[] imageBytes) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new StaticImageHttpMessageHandler(imageBytes));
    }

    private sealed class StaticImageHttpMessageHandler(byte[] imageBytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Head)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(imageBytes)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
        }
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
            string? userId,
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

    private sealed class StubMetricsLogger : IMetricsLogger
    {
        public void LogAnalysisJobCompleted(Guid jobId, string userId, bool qualityPassed, string? qualityFailureCode, double? analysisConfidence, string? faceShape, int recommendationCount, TimeSpan duration) { }
        public void LogAnalysisJobFailed(Guid jobId, string userId, string errorCode, string errorMessage, TimeSpan duration) { }
        public void LogRecommendationFeedbackSubmitted(Guid jobId, string userId, string? selectedStyleId, int? rating, string? tags, int? recommendationRank) { }
    }

    private sealed class StubWorkerQueuePublisher : IWorkerQueuePublisher
    {
        public List<StyleJob> Messages { get; } = [];

        public Task PublishAsync(StyleJob job, CancellationToken ct = default)
        {
            Messages.Add(job);
            return Task.CompletedTask;
        }
    }
}