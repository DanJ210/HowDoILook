using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiStyleApp.Worker.Services;

public class FaceAnalysisException : Exception
{
    public FaceAnalysisException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public record FaceAnalysisPipelineResult(
    bool QualityPassed,
    string? QualityFailureCode,
    string? QualityMessage,
    string FeatureVectorJson,
    double AnalysisConfidence,
    string RecommendationsJson);

public record QualityMetrics(
    bool Passed,
    string? FailureCode,
    string? Message,
    double Brightness,
    double Contrast,
    double BlurScore,
    double CenterOffset);

public record LandmarkFeatures(
    double JawWidthRatio,
    double ForeheadHeightRatio,
    double FaceElongation);

public record SegmentationFeatures(
    double HairDensityEstimate,
    double BeardDensityEstimate);

public interface IFaceAnalysisPipeline
{
    Task<FaceAnalysisPipelineResult> AnalyzeAsync(string imageUrl, string? gender, string? preferencesJson, CancellationToken ct);
}

public interface IFaceQualityStage
{
    QualityMetrics Evaluate(Image<Rgba32> image);
}

public interface IFaceLandmarkStage
{
    LandmarkFeatures Extract(Image<Rgba32> image);
}

public interface IFaceSegmentationStage
{
    SegmentationFeatures Extract(Image<Rgba32> image, string? gender);
}

public interface IRecommendationStage
{
    IReadOnlyList<object> Rank(
        LandmarkFeatures landmarks,
        SegmentationFeatures segmentation,
        QualityMetrics quality,
        double confidence,
    string? gender,
    bool allowBeardSuggestions);
}

public class HeuristicFaceQualityStage : IFaceQualityStage
{
    public QualityMetrics Evaluate(Image<Rgba32> image) => FaceAnalysisPipeline.ComputeQuality(image);
}

public class HeuristicFaceLandmarkStage : IFaceLandmarkStage
{
    public LandmarkFeatures Extract(Image<Rgba32> image) => FaceAnalysisPipeline.ComputeLandmarkFeatures(image);
}

public class HeuristicFaceSegmentationStage : IFaceSegmentationStage
{
    public SegmentationFeatures Extract(Image<Rgba32> image, string? gender)
        => FaceAnalysisPipeline.ComputeSegmentationFeatures(image, gender);
}

public class RuleBasedRecommendationStage : IRecommendationStage
{
    public IReadOnlyList<object> Rank(
        LandmarkFeatures landmarks,
        SegmentationFeatures segmentation,
        QualityMetrics quality,
        double confidence,
    string? gender,
    bool allowBeardSuggestions)
    => FaceAnalysisPipeline.BuildRecommendations(landmarks, segmentation, quality, confidence, gender, allowBeardSuggestions);
}

public class FaceAnalysisPipeline : IFaceAnalysisPipeline
{
        private static readonly JsonSerializerOptions PreferencesJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

    private const int MinWidth = 768;
    private const int MinHeight = 768;
    private const double MinBlurScore = 8.0;
    private const double MinBrightness = 0.18;
    private const double MaxBrightness = 0.90;
    private const double MaxCenterOffset = 0.22;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFaceQualityStage _qualityStage;
    private readonly IFaceLandmarkStage _landmarkStage;
    private readonly IFaceSegmentationStage _segmentationStage;
    private readonly IRecommendationStage _recommendationStage;
    private readonly ILogger<FaceAnalysisPipeline> _logger;

    public FaceAnalysisPipeline(
        IHttpClientFactory httpClientFactory,
        IFaceQualityStage qualityStage,
        IFaceLandmarkStage landmarkStage,
        IFaceSegmentationStage segmentationStage,
        IRecommendationStage recommendationStage,
        ILogger<FaceAnalysisPipeline> logger)
    {
        _httpClientFactory = httpClientFactory;
        _qualityStage = qualityStage;
        _landmarkStage = landmarkStage;
        _segmentationStage = segmentationStage;
        _recommendationStage = recommendationStage;
        _logger = logger;
    }

    public async Task<FaceAnalysisPipelineResult> AnalyzeAsync(string imageUrl, string? gender, string? preferencesJson, CancellationToken ct)
    {
        var imageBytes = await DownloadImageBytesAsync(imageUrl, ct);
        using var image = Image.Load<Rgba32>(imageBytes);

        var preferences = ParsePreferences(preferencesJson);

        var quality = _qualityStage.Evaluate(image);
        if (!quality.Passed)
        {
            throw new FaceAnalysisException(quality.FailureCode!, quality.Message!);
        }

        var landmarks = _landmarkStage.Extract(image);
        var segmentation = _segmentationStage.Extract(image, gender);

        var confidence = ComputeConfidence(quality, landmarks, segmentation);
        var featureVector = new
        {
            source = "worker-v1-staged-analysis",
            schemaVersion = 2,
            quality,
            landmarks,
            segmentation
        };

        var recommendations = _recommendationStage.Rank(
            landmarks,
            segmentation,
            quality,
            confidence,
            gender,
            preferences.AllowBeardSuggestions);

        return new FaceAnalysisPipelineResult(
            QualityPassed: true,
            QualityFailureCode: null,
            QualityMessage: null,
            FeatureVectorJson: JsonSerializer.Serialize(featureVector),
            AnalysisConfidence: confidence,
            RecommendationsJson: JsonSerializer.Serialize(recommendations));
    }

    private async Task<byte[]> DownloadImageBytesAsync(string imageUrl, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient();
        using var response = await client.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    internal static QualityMetrics ComputeQuality(Image<Rgba32> image)
    {
        var width = image.Width;
        var height = image.Height;

        if (width < MinWidth || height < MinHeight)
        {
            return new QualityMetrics(
                Passed: false,
                FailureCode: "ANALYSIS_QUALITY_TOO_LOW_RESOLUTION",
                Message: $"Image resolution is too low. Minimum required is {MinWidth}x{MinHeight}.",
                Brightness: 0.0,
                Contrast: 0.0,
                BlurScore: 0.0,
                CenterOffset: 1.0);
        }

        var sampleStep = Math.Max(1, Math.Min(width, height) / 256);
        double luminanceSum = 0;
        double luminanceSqSum = 0;
        double edgeSum = 0;
        double weightedX = 0;
        double weightedY = 0;
        int count = 0;

        for (var y = sampleStep; y < height - sampleStep; y += sampleStep)
        {
            for (var x = sampleStep; x < width - sampleStep; x += sampleStep)
            {
                var current = Luminance(image[x, y]);
                var left = Luminance(image[x - sampleStep, y]);
                var up = Luminance(image[x, y - sampleStep]);

                luminanceSum += current;
                luminanceSqSum += current * current;
                edgeSum += Math.Abs(current - left) + Math.Abs(current - up);
                weightedX += x * current;
                weightedY += y * current;
                count++;
            }
        }

        var mean = luminanceSum / Math.Max(1, count);
        var variance = (luminanceSqSum / Math.Max(1, count)) - (mean * mean);
        var contrast = Math.Sqrt(Math.Max(0.0, variance));
        var blurScore = edgeSum / Math.Max(1, count);

        var centerX = weightedX / Math.Max(0.0001, luminanceSum);
        var centerY = weightedY / Math.Max(0.0001, luminanceSum);
        var normalizedDx = Math.Abs((centerX / width) - 0.5);
        var normalizedDy = Math.Abs((centerY / height) - 0.5);
        var centerOffset = Math.Sqrt(normalizedDx * normalizedDx + normalizedDy * normalizedDy);

        if (blurScore < MinBlurScore)
        {
            return new QualityMetrics(
                Passed: false,
                FailureCode: "ANALYSIS_QUALITY_TOO_BLURRY",
                Message: "Image appears too blurry. Try a sharper photo with better focus.",
                Brightness: mean,
                Contrast: contrast,
                BlurScore: blurScore,
                CenterOffset: centerOffset);
        }

        if (mean < MinBrightness || mean > MaxBrightness)
        {
            return new QualityMetrics(
                Passed: false,
                FailureCode: "ANALYSIS_QUALITY_BAD_EXPOSURE",
                Message: "Image exposure is not suitable. Try balanced lighting.",
                Brightness: mean,
                Contrast: contrast,
                BlurScore: blurScore,
                CenterOffset: centerOffset);
        }

        if (centerOffset > MaxCenterOffset)
        {
            return new QualityMetrics(
                Passed: false,
                FailureCode: "ANALYSIS_POOR_POSE",
                Message: "Face appears off-center. Try a front-facing centered photo.",
                Brightness: mean,
                Contrast: contrast,
                BlurScore: blurScore,
                CenterOffset: centerOffset);
        }

        return new QualityMetrics(
            Passed: true,
            FailureCode: null,
            Message: null,
            Brightness: mean,
            Contrast: contrast,
            BlurScore: blurScore,
            CenterOffset: centerOffset);
    }

    internal static LandmarkFeatures ComputeLandmarkFeatures(Image<Rgba32> image)
    {
        var width = image.Width;
        var height = image.Height;

        // Approximate face-region geometry from edge-density profiles.
        var topBand = SumHorizontalEdgeBand(image, 0.12, 0.30);
        var midBand = SumHorizontalEdgeBand(image, 0.35, 0.60);
        var jawBand = SumHorizontalEdgeBand(image, 0.68, 0.90);

        var jawWidthRatio = Clamp01(jawBand / Math.Max(0.0001, midBand));
        var foreheadHeightRatio = Clamp01(topBand / Math.Max(0.0001, midBand));
        var faceElongation = Clamp01((double)height / Math.Max(1, width) / 2.0);

        return new LandmarkFeatures(
            JawWidthRatio: jawWidthRatio,
            ForeheadHeightRatio: foreheadHeightRatio,
            FaceElongation: faceElongation);
    }

    internal static SegmentationFeatures ComputeSegmentationFeatures(Image<Rgba32> image, string? gender)
    {
        var width = image.Width;
        var height = image.Height;

        var hairRegionDarkRatio = DarkPixelRatio(image, 0.0, 0.30, 0.18);
        var lowerFaceDarkRatio = DarkPixelRatio(image, 0.62, 0.92, 0.20, xStartFactor: 0.25, xEndFactor: 0.75);

        var beardDensityEstimate = string.Equals(gender, "male", StringComparison.OrdinalIgnoreCase)
            ? Clamp01(lowerFaceDarkRatio)
            : 0.0;

        var hairDensityEstimate = Clamp01(hairRegionDarkRatio);

        return new SegmentationFeatures(
            HairDensityEstimate: hairDensityEstimate,
            BeardDensityEstimate: beardDensityEstimate);
    }

    private static double ComputeConfidence(QualityMetrics quality, LandmarkFeatures landmarks, SegmentationFeatures segmentation)
    {
        var blur = quality.BlurScore;
        var centerOffset = quality.CenterOffset;
        var jaw = landmarks.JawWidthRatio;
        var hair = segmentation.HairDensityEstimate;

        var confidence = 0.45
            + Math.Min(0.25, blur / 80.0)
            + (0.15 * (1.0 - Math.Min(1.0, centerOffset * 3.0)))
            + (0.10 * jaw)
            + (0.05 * hair);

        return Math.Round(Clamp01(confidence), 3);
    }

    internal static IReadOnlyList<object> BuildRecommendations(
        LandmarkFeatures landmarks,
        SegmentationFeatures segmentation,
        QualityMetrics quality,
        double confidence,
        string? gender,
        bool allowBeardSuggestions)
    {
        var jaw = landmarks.JawWidthRatio;
        var forehead = landmarks.ForeheadHeightRatio;
        var elongation = landmarks.FaceElongation;
        var hairDensity = segmentation.HairDensityEstimate;
        var beardDensity = segmentation.BeardDensityEstimate;

        var candidates = new List<(string id, string name, double score, List<string> reasons, List<string> constraints)>
        {
            (
                "textured-crop",
                "Textured Crop",
                ScoreBase(0.62, hairDensity, jaw, elongation, confidence),
                new List<string>
                {
                    "Adds controlled texture without excess volume",
                    "Works well when jaw definition is moderate to strong"
                },
                new List<string> { "Best maintained with trims every 3-4 weeks" }
            ),
            (
                "classic-side-part",
                "Classic Side Part",
                ScoreBase(0.58, hairDensity, 1.0 - forehead, elongation, confidence),
                new List<string>
                {
                    "Balanced shape for most face proportions",
                    "Professional and versatile styling profile"
                },
                new List<string> { "Needs light product for hold and direction" }
            ),
            (
                "short-quiff",
                "Short Quiff",
                ScoreBase(0.55, hairDensity, forehead, 1.0 - elongation, confidence),
                new List<string>
                {
                    "Adds vertical emphasis to balance wider lower face",
                    "Keeps sides controlled while retaining top movement"
                },
                new List<string> { "Requires blow-dry or styling routine for shape" }
            )
        };

        if (allowBeardSuggestions
            && string.Equals(gender, "male", StringComparison.OrdinalIgnoreCase)
            && beardDensity > 0.25)
        {
            candidates.Add(
                (
                    "short-boxed-beard",
                    "Short Boxed Beard Pairing",
                    ScoreBase(0.50, beardDensity, jaw, 1.0 - forehead, confidence),
                    new List<string>
                    {
                        "Facial hair density supports a clean short beard contour",
                        "Can reinforce jaw framing when paired with short sides"
                    },
                    new List<string> { "Optional recommendation and can be skipped" }
                ));
        }

        return candidates
            .OrderByDescending(c => c.score)
            .Take(5)
.Select(c => new
{
    StyleId = c.id,
    StyleName = c.name,
    Score = Math.Round(c.score, 3),
    Reasons = c.reasons,
    Constraints = c.constraints
})
            .Cast<object>()
            .ToList();
    }

    private static double ScoreBase(double baseline, params double[] factors)
    {
        var adjustment = factors.Sum(f => (f - 0.5) * 0.15);
        return Clamp01(baseline + adjustment);
    }

    private static double SumHorizontalEdgeBand(Image<Rgba32> image, double yStartFactor, double yEndFactor)
    {
        var yStart = Math.Clamp((int)(image.Height * yStartFactor), 1, image.Height - 2);
        var yEnd = Math.Clamp((int)(image.Height * yEndFactor), yStart + 1, image.Height - 1);
        var xStep = Math.Max(1, image.Width / 256);
        var yStep = Math.Max(1, image.Height / 256);

        double edgeSum = 0;
        int count = 0;

        for (var y = yStart; y < yEnd; y += yStep)
        {
            for (var x = xStep; x < image.Width - xStep; x += xStep)
            {
                var current = Luminance(image[x, y]);
                var left = Luminance(image[x - xStep, y]);
                edgeSum += Math.Abs(current - left);
                count++;
            }
        }

        return edgeSum / Math.Max(1, count);
    }

    private static double DarkPixelRatio(
        Image<Rgba32> image,
        double yStartFactor,
        double yEndFactor,
        double threshold,
        double xStartFactor = 0.0,
        double xEndFactor = 1.0)
    {
        var yStart = Math.Clamp((int)(image.Height * yStartFactor), 0, image.Height - 1);
        var yEnd = Math.Clamp((int)(image.Height * yEndFactor), yStart + 1, image.Height);
        var xStart = Math.Clamp((int)(image.Width * xStartFactor), 0, image.Width - 1);
        var xEnd = Math.Clamp((int)(image.Width * xEndFactor), xStart + 1, image.Width);

        var xStep = Math.Max(1, image.Width / 256);
        var yStep = Math.Max(1, image.Height / 256);

        int dark = 0;
        int total = 0;

        for (var y = yStart; y < yEnd; y += yStep)
        {
            for (var x = xStart; x < xEnd; x += xStep)
            {
                var lum = Luminance(image[x, y]);
                if (lum <= threshold)
                {
                    dark++;
                }

                total++;
            }
        }

        return total == 0 ? 0 : (double)dark / total;
    }

    private static double Luminance(Rgba32 pixel)
    {
        return (0.2126 * pixel.R + 0.7152 * pixel.G + 0.0722 * pixel.B) / 255.0;
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

    private static StoredRecommendationPreferences ParsePreferences(string? preferencesJson)
    {
        if (string.IsNullOrWhiteSpace(preferencesJson))
        {
            return new StoredRecommendationPreferences();
        }

        try
        {
            return JsonSerializer.Deserialize<StoredRecommendationPreferences>(preferencesJson, PreferencesJsonOptions)
                ?? new StoredRecommendationPreferences();
        }
        catch (JsonException)
        {
            return new StoredRecommendationPreferences();
        }
    }

    private sealed record StoredRecommendationPreferences(
        bool AllowHairColorChange = true,
        bool AllowBeardSuggestions = true);
}
