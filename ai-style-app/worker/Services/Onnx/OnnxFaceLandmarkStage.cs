using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using AiStyleApp.Worker.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiStyleApp.Worker.Services.Onnx;

public class OnnxFaceLandmarkStage : IFaceLandmarkModelStage
{
    private readonly IOnnxSessionFactory _sessionFactory;
    private readonly IOptions<OnnxLandmarkOptions> _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<OnnxFaceLandmarkStage> _logger;

    public OnnxFaceLandmarkStage(
        IOnnxSessionFactory sessionFactory,
        IOptions<OnnxLandmarkOptions> options,
        IHostEnvironment environment,
        ILogger<OnnxFaceLandmarkStage> logger)
    {
        _sessionFactory = sessionFactory;
        _options = options;
        _environment = environment;
        _logger = logger;
    }

    public LandmarkFeatures Extract(Image<Rgba32> image, FaceBoundingBox? face)
    {
        var rawModelPath = _options.Value.ModelPath;
        var modelPath = ResolveModelPath(rawModelPath);
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            _logger.LogError(
                "ONNX landmarks enabled but model path was not resolved. ConfiguredPath={ConfiguredPath}, ContentRoot={ContentRoot}, BaseDirectory={BaseDirectory}.",
                rawModelPath,
                _environment.ContentRootPath,
                AppContext.BaseDirectory);

            throw new FaceAnalysisException(
                "ANALYSIS_LANDMARK_MODEL_LOAD_FAILED",
                "Landmark model path is invalid or missing. Please check FaceAnalysis:OnnxLandmarks:ModelPath.");
        }

        try
        {
            using var session = _sessionFactory.Create(modelPath, _options.Value.ExecutionProvider);
            var inputName = session.InputMetadata.Keys.First();
            var tensor = OnnxImageTensorizer.CreateNchwTensor(image, _options.Value.InputWidth, _options.Value.InputHeight);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, tensor)
            };

            using var results = session.Run(inputs);

            if (!TryReadLandmarks(results, _options.Value.InputWidth, _options.Value.InputHeight, out var points, out var confidence))
            {
                var outputSummary = string.Join(
                    "; ",
                    results.Select(r =>
                    {
                        if (r.Value is Tensor<float> t)
                        {
                            var dims = string.Join("x", Enumerable.Range(0, t.Rank).Select(i => t.Dimensions[i].ToString()));
                            return $"{r.Name}:float[{dims}]";
                        }

                        return $"{r.Name}:{r.Value.GetType().Name}";
                    }));

                _logger.LogWarning(
                    "ONNX landmark output shape is unsupported for current parser. ModelType={ModelType}; Outputs={Outputs}.",
                    _options.Value.ModelType,
                    outputSummary);

                throw new FaceAnalysisException(
                    "ANALYSIS_LANDMARK_MODEL_OUTPUT_UNSUPPORTED",
                    "Landmark model output format is unsupported by the current parser.");
            }

            return BuildFeaturesFromKeypoints(points, confidence);
        }
        catch (Exception ex)
        {
            if (ex is FaceAnalysisException)
            {
                throw;
            }

            _logger.LogError(ex, "ONNX landmarks failed and cannot continue while OnnxLandmarks is enabled.");
            throw new FaceAnalysisException(
                "ANALYSIS_LANDMARK_MODEL_LOAD_FAILED",
                "Landmark model failed to load or run. Ensure the ONNX file is valid and compatible with runtime.");
        }
    }

    private static LandmarkFeatures BuildFeaturesFromKeypoints(IReadOnlyList<(double x, double y)> points, double confidence)
    {
        if (points.Count < 68)
        {
            return new LandmarkFeatures(
                JawWidthRatio: 0.5,
                ForeheadHeightRatio: 0.5,
                FaceElongation: 0.7,
                LandmarkConfidence: confidence,
                Yaw: 0.0,
                Pitch: 0.0);
        }

        var minX = points.Min(p => p.x);
        var maxX = points.Max(p => p.x);
        var minY = points.Min(p => p.y);
        var maxY = points.Max(p => p.y);

        var faceWidth = Math.Max(0.0001, maxX - minX);
        var faceHeight = Math.Max(0.0001, maxY - minY);

        var jawWidth = Distance(points[0], points[16]);
        var cheekWidth = Distance(points[3], points[13]);
        var foreheadWidth = Distance(points[19], points[24]);

        var jawWidthRatio = Clamp01(jawWidth / Math.Max(0.0001, cheekWidth));
        var foreheadHeightRatio = Clamp01(foreheadWidth / faceWidth);
        var faceElongation = Clamp01((faceHeight / faceWidth) / 2.0);

        var leftEye = Average(points[36], points[39]);
        var rightEye = Average(points[42], points[45]);
        var nose = points[30];
        var mouth = Average(points[48], points[54]);

        var midEyeX = (leftEye.x + rightEye.x) / 2.0;
        var eyeDistance = Math.Max(0.0001, Math.Abs(rightEye.x - leftEye.x));
        var yaw = Math.Clamp((nose.x - midEyeX) / (eyeDistance / 2.0), -1.0, 1.0);

        var eyeLineY = (leftEye.y + rightEye.y) / 2.0;
        var upper = Math.Max(0.0001, nose.y - eyeLineY);
        var lower = Math.Max(0.0001, mouth.y - nose.y);
        var pitch = Math.Clamp((upper / lower) - 1.0, -1.0, 1.0);

        return new LandmarkFeatures(
            JawWidthRatio: jawWidthRatio,
            ForeheadHeightRatio: foreheadHeightRatio,
            FaceElongation: faceElongation,
            LandmarkConfidence: Clamp01(confidence),
            Yaw: yaw,
            Pitch: pitch);
    }

    private static bool TryReadLandmarks(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        int inputWidth,
        int inputHeight,
        out IReadOnlyList<(double x, double y)> points,
        out double confidence)
    {
        points = [];
        confidence = 0.9;

        var tensors = results
            .Select(r => (r.Name, Tensor: r.Value as Tensor<float>))
            .Where(x => x.Tensor is not null)
            .Select(x => (x.Name, Tensor: x.Tensor!))
            .ToList();

        var landmarkTensor = tensors
            .Select(t => t.Tensor)
            .FirstOrDefault(t => IsLandmarkTensor(t));

        if (landmarkTensor is null)
        {
            return false;
        }

        var normalizedPoints = ParseLandmarkPoints(landmarkTensor, inputWidth, inputHeight, out var inferredConfidence);
        if (normalizedPoints.Count == 0)
        {
            return false;
        }

        var confidenceTensor = tensors
            .Select(t => t.Tensor)
            .FirstOrDefault(t => t != landmarkTensor && CouldBeConfidenceTensor(t));

        if (confidenceTensor is not null)
        {
            confidence = ReadConfidence(confidenceTensor);
        }
        else
        {
            confidence = inferredConfidence;
        }

        points = normalizedPoints;
        return true;
    }

    private static bool IsLandmarkTensor(Tensor<float> tensor)
    {
        if (tensor.Rank == 2 && tensor.Dimensions[0] == 1 && tensor.Dimensions[1] >= 136 && tensor.Dimensions[1] % 2 == 0)
        {
            return true;
        }

        if (tensor.Rank == 3 && tensor.Dimensions[0] == 1)
        {
            if (tensor.Dimensions[1] >= 68 && tensor.Dimensions[2] == 2)
            {
                return true;
            }

            if (tensor.Dimensions[1] == 2 && tensor.Dimensions[2] >= 68)
            {
                return true;
            }
        }

        // FAN-style heatmaps: [1, numLandmarks, heatmapH, heatmapW]
        if (tensor.Rank == 4
            && tensor.Dimensions[0] == 1
            && tensor.Dimensions[1] >= 68
            && tensor.Dimensions[2] >= 8
            && tensor.Dimensions[3] >= 8)
        {
            return true;
        }

        return false;
    }

    private static List<(double x, double y)> ParseLandmarkPoints(Tensor<float> tensor, int inputWidth, int inputHeight, out double confidence)
    {
        var points = new List<(double x, double y)>();
        confidence = 0.9;

        if (tensor.Rank == 2)
        {
            var n = tensor.Dimensions[1] / 2;
            for (var i = 0; i < n; i++)
            {
                var x = tensor[0, i * 2];
                var y = tensor[0, (i * 2) + 1];
                points.Add(NormalizePoint(x, y, inputWidth, inputHeight));
            }

            return points;
        }

        if (tensor.Rank == 3 && tensor.Dimensions[1] >= 68 && tensor.Dimensions[2] == 2)
        {
            var n = tensor.Dimensions[1];
            for (var i = 0; i < n; i++)
            {
                var x = tensor[0, i, 0];
                var y = tensor[0, i, 1];
                points.Add(NormalizePoint(x, y, inputWidth, inputHeight));
            }

            return points;
        }

        if (tensor.Rank == 3 && tensor.Dimensions[1] == 2 && tensor.Dimensions[2] >= 68)
        {
            var n = tensor.Dimensions[2];
            for (var i = 0; i < n; i++)
            {
                var x = tensor[0, 0, i];
                var y = tensor[0, 1, i];
                points.Add(NormalizePoint(x, y, inputWidth, inputHeight));
            }

            return points;
        }

        if (tensor.Rank == 4 && tensor.Dimensions[0] == 1)
        {
            var landmarkCount = tensor.Dimensions[1];
            var heatmapH = tensor.Dimensions[2];
            var heatmapW = tensor.Dimensions[3];

            double peakSum = 0;

            for (var l = 0; l < landmarkCount; l++)
            {
                float peak = float.MinValue;
                var peakX = 0;
                var peakY = 0;

                for (var y = 0; y < heatmapH; y++)
                {
                    for (var x = 0; x < heatmapW; x++)
                    {
                        var v = tensor[0, l, y, x];
                        if (v > peak)
                        {
                            peak = v;
                            peakX = x;
                            peakY = y;
                        }
                    }
                }

                // Heatmap peak coordinates normalized to input space.
                var nx = (peakX + 0.5) / heatmapW;
                var ny = (peakY + 0.5) / heatmapH;
                points.Add((Clamp01(nx), Clamp01(ny)));
                peakSum += peak;
            }

            if (landmarkCount > 0)
            {
                // Peaks may be logits or probabilities depending on export.
                var avgPeak = peakSum / landmarkCount;
                confidence = avgPeak is >= 0 and <= 1
                    ? Clamp01(avgPeak)
                    : Clamp01(1.0 / (1.0 + Math.Exp(-avgPeak)));
            }

            return points;
        }

        return points;
    }

    private static (double x, double y) NormalizePoint(float x, float y, int inputWidth, int inputHeight)
    {
        var nx = x;
        var ny = y;

        // Some models emit pixel coordinates, others emit normalized [0..1].
        if (nx > 1.5f || ny > 1.5f)
        {
            nx = inputWidth <= 0 ? nx : nx / inputWidth;
            ny = inputHeight <= 0 ? ny : ny / inputHeight;
        }

        return (Clamp01(nx), Clamp01(ny));
    }

    private static bool CouldBeConfidenceTensor(Tensor<float> tensor)
    {
        if (tensor.Rank == 1 && tensor.Dimensions[0] == 1)
        {
            return true;
        }

        if (tensor.Rank == 2 && tensor.Dimensions[0] == 1 && tensor.Dimensions[1] == 1)
        {
            return true;
        }

        return false;
    }

    private static double ReadConfidence(Tensor<float> tensor)
    {
        float raw;
        if (tensor.Rank == 1)
        {
            raw = tensor[0];
        }
        else
        {
            raw = tensor[0, 0];
        }

        if (raw is >= 0f and <= 1f)
        {
            return raw;
        }

        var score = 1.0 / (1.0 + Math.Exp(-raw));
        return Clamp01(score);
    }

    private static (double x, double y) Average((double x, double y) a, (double x, double y) b)
        => ((a.x + b.x) / 2.0, (a.y + b.y) / 2.0);

    private static double Distance((double x, double y) a, (double x, double y) b)
    {
        var dx = a.x - b.x;
        var dy = a.y - b.y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private string? ResolveModelPath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        if (Path.IsPathRooted(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var contentRootCandidate = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configuredPath));
        if (File.Exists(contentRootCandidate))
        {
            return contentRootCandidate;
        }

        var baseDirCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
        if (File.Exists(baseDirCandidate))
        {
            return baseDirCandidate;
        }

        return null;
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);
}
