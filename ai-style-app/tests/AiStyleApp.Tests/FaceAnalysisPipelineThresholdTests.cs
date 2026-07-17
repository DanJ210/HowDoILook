using System.Net;
using AiStyleApp.Worker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AiStyleApp.Tests;

public class FaceAnalysisPipelineThresholdTests
{
    [Fact]
    public async Task AnalyzeAsync_HighDetectionAndStablePose_RelaxesLandmarkThreshold()
    {
        var imageBytes = CreateImageBytes();
        var pipeline = CreatePipeline(
            imageBytes,
            detectionConfidence: 0.95,
            landmarkConfidence: 0.52,
            yaw: 0.05,
            pitch: 0.04,
            minLandmarkConfidence: 0.55);

        var result = await pipeline.AnalyzeAsync("https://example.com/photo.jpg", "male", null, "user-1", CancellationToken.None);

        Assert.True(result.QualityPassed);
        Assert.NotNull(result.FeatureVectorJson);
    }

    [Fact]
    public async Task AnalyzeAsync_UnstablePose_UsesStrictLandmarkThresholdAndReturnsDetailedError()
    {
        var imageBytes = CreateImageBytes();
        var pipeline = CreatePipeline(
            imageBytes,
            detectionConfidence: 0.95,
            landmarkConfidence: 0.52,
            yaw: 0.35,
            pitch: 0.04,
            minLandmarkConfidence: 0.55);

        var ex = await Assert.ThrowsAsync<FaceAnalysisException>(() =>
            pipeline.AnalyzeAsync("https://example.com/photo.jpg", "male", null, "user-1", CancellationToken.None));

        Assert.Equal("ANALYSIS_LANDMARK_CONFIDENCE_TOO_LOW", ex.Code);
        Assert.Contains("value: 0.52", ex.Message, StringComparison.Ordinal);
        Assert.Contains("required: 0.55", ex.Message, StringComparison.Ordinal);
    }

    private static FaceAnalysisPipeline CreatePipeline(
        byte[] imageBytes,
        double detectionConfidence,
        double landmarkConfidence,
        double yaw,
        double pitch,
        double minLandmarkConfidence)
    {
        var httpClientFactory = new StubHttpClientFactory(imageBytes);

        return new FaceAnalysisPipeline(
            httpClientFactory,
            new StubFaceDetectorStage(detectionConfidence),
            new StubFaceQualityStage(),
            new StubFaceLandmarkModelStage(landmarkConfidence, yaw, pitch),
            new HeuristicFaceLandmarkStage(),
            new StubFaceRegionEstimationStage(),
            new StubFaceSegmentationStage(),
            new RuleBasedRecommendationStage(),
            Options.Create(new WorkerFeatureFlags
            {
                OnnxFaceDetection = true,
                OnnxLandmarks = true,
                OnnxRegionEstimation = false
            }),
            Options.Create(new FaceAnalysisThresholds
            {
                MinFaceDetectionConfidence = 0.75,
                MinLandmarkConfidence = minLandmarkConfidence,
                HighFaceDetectionConfidenceForLandmarkRelaxation = 0.90,
                LandmarkConfidenceRelaxationWhenPoseStable = 0.05,
                MaxStablePoseYaw = 0.20,
                MaxStablePosePitch = 0.20
            }),
            NullLogger<FaceAnalysisPipeline>.Instance);
    }

    private static byte[] CreateImageBytes()
    {
        using var image = new Image<Rgba32>(1024, 1024);
        image.Mutate(ctx => ctx.BackgroundColor(new Rgba32(190, 170, 150)));

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private sealed class StubFaceDetectorStage : IFaceDetectorStage
    {
        private readonly double _confidence;

        public StubFaceDetectorStage(double confidence)
        {
            _confidence = confidence;
        }

        public FaceDetectionResult Detect(Image<Rgba32> image)
            => new(
                FaceCount: 1,
                PrimaryFace: new FaceBoundingBox(280, 180, 460, 620),
                PrimaryFaceConfidence: _confidence,
                FailureCode: null,
                FailureMessage: null,
                Model: "stub-detector",
                ModelVersion: "test-v1",
                Notes: null);
    }

    private sealed class StubFaceQualityStage : IFaceQualityStage
    {
        public QualityMetrics Evaluate(Image<Rgba32> image)
            => new(
                Passed: true,
                FailureCode: null,
                Message: null,
                Brightness: 0.55,
                Contrast: 0.16,
                BlurScore: 0.18,
                CenterOffset: 0.04);
    }

    private sealed class StubFaceLandmarkModelStage : IFaceLandmarkModelStage
    {
        private readonly double _confidence;
        private readonly double _yaw;
        private readonly double _pitch;

        public StubFaceLandmarkModelStage(double confidence, double yaw, double pitch)
        {
            _confidence = confidence;
            _yaw = yaw;
            _pitch = pitch;
        }

        public LandmarkFeatures Extract(Image<Rgba32> image, FaceBoundingBox? face)
            => new(
                JawWidthRatio: 0.62,
                ForeheadHeightRatio: 0.52,
                FaceElongation: 0.70,
                LandmarkConfidence: _confidence,
                Yaw: _yaw,
                Pitch: _pitch);
    }

    private sealed class StubFaceSegmentationStage : IFaceSegmentationStage
    {
        public SegmentationFeatures Extract(Image<Rgba32> image, string? gender)
            => new(HairDensityEstimate: 0.64, BeardDensityEstimate: 0.45);
    }

    private sealed class StubFaceRegionEstimationStage : IFaceRegionEstimationStage
    {
        public SegmentationFeatures Extract(Image<Rgba32> image, FaceBoundingBox? face, string? gender)
            => new(HairDensityEstimate: 0.64, BeardDensityEstimate: 0.45);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly byte[] _imageBytes;

        public StubHttpClientFactory(byte[] imageBytes)
        {
            _imageBytes = imageBytes;
        }

        public HttpClient CreateClient(string name)
            => new(new StubHttpMessageHandler(_imageBytes));

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly byte[] _imageBytes;

            public StubHttpMessageHandler(byte[] imageBytes)
            {
                _imageBytes = imageBytes;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_imageBytes)
                });
        }
    }
}
