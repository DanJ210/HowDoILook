using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiStyleApp.Worker.Services.Onnx;

public class OnnxFaceDetectorStage : IFaceDetectorStage
{
    private readonly IOnnxSessionFactory _sessionFactory;
    private readonly IOptions<OnnxFaceDetectionOptions> _options;
    private readonly HeuristicFaceDetectorStage _fallback;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<OnnxFaceDetectorStage> _logger;

    public OnnxFaceDetectorStage(
        IOnnxSessionFactory sessionFactory,
        IOptions<OnnxFaceDetectionOptions> options,
        HeuristicFaceDetectorStage fallback,
        IHostEnvironment environment,
        ILogger<OnnxFaceDetectorStage> logger)
    {
        _sessionFactory = sessionFactory;
        _options = options;
        _fallback = fallback;
        _environment = environment;
        _logger = logger;
    }

    public FaceDetectionResult Detect(Image<Rgba32> image)
    {
        var rawModelPath = _options.Value.ModelPath;
        var modelPath = ResolveModelPath(rawModelPath);
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            _logger.LogWarning(
                "ONNX face detection enabled but model path was not resolved. ConfiguredPath={ConfiguredPath}, ContentRoot={ContentRoot}, BaseDirectory={BaseDirectory}. Falling back to heuristic detector.",
                rawModelPath,
                _environment.ContentRootPath,
                AppContext.BaseDirectory);

            return _fallback.Detect(image) with
            {
                Notes = "fallback:missing-onnx-model:path=" + (rawModelPath ?? "<empty>")
            };
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

            List<(FaceBoundingBox box, double confidence)> detections;
            var parsed = string.Equals(_options.Value.ModelType, "SCRFD", StringComparison.OrdinalIgnoreCase)
                ? TryParseScrfdDetections(
                    results,
                    image.Width,
                    image.Height,
                    _options.Value.InputWidth,
                    _options.Value.InputHeight,
                    _options.Value.ScoreThreshold,
                    out detections)
                : TryParseCommonDetections(results, image.Width, image.Height, _options.Value.ScoreThreshold, out detections);

            if (!parsed)
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
                    "ONNX face detection output shape is unsupported for current parser. ModelType={ModelType}; Outputs={Outputs}. Falling back to heuristic detector.",
                    _options.Value.ModelType,
                    outputSummary);
                return _fallback.Detect(image) with { Notes = "fallback:unsupported-onnx-output" };
            }

            detections = ApplyNms(detections, _options.Value.NmsIouThreshold);

            if (detections.Count == 0)
            {
                _logger.LogWarning(
                    "ONNX face detection found no faces for image {Width}x{Height}; falling back to heuristic detector.",
                    image.Width,
                    image.Height);

                return _fallback.Detect(image) with
                {
                    Notes = "fallback:onnx-no-detections"
                };
            }

            if (detections.Count > 1)
            {
                _logger.LogWarning(
                    "ONNX face detection found {Count} faces for image {Width}x{Height}; rejecting multi-face uploads.",
                    detections.Count,
                    image.Width,
                    image.Height);

                var primary = detections.OrderByDescending(d => d.confidence).First();
                return new FaceDetectionResult(
                    FaceCount: detections.Count,
                    PrimaryFace: primary.box,
                    PrimaryFaceConfidence: primary.confidence,
                    FailureCode: null,
                    FailureMessage: null,
                    Model: "onnx-face-detector",
                    ModelVersion: "v1",
                    Notes: "onnx-multiple-detections");
            }

            var primary = detections.OrderByDescending(d => d.confidence).First();
            return new FaceDetectionResult(
                FaceCount: detections.Count,
                PrimaryFace: primary.box,
                PrimaryFaceConfidence: primary.confidence,
                FailureCode: null,
                FailureMessage: null,
                Model: "onnx-face-detector",
                ModelVersion: "v1",
                Notes: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ONNX face detection failed. Falling back to heuristic detector.");
            return _fallback.Detect(image) with { Notes = "fallback:onnx-exception:" + ex.GetType().Name };
        }
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

    private static bool TryParseScrfdDetections(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        int imageWidth,
        int imageHeight,
        int inputWidth,
        int inputHeight,
        double scoreThreshold,
        out List<(FaceBoundingBox box, double confidence)> detections)
    {
        detections = [];

        if (TryParseScrfdFlatDetections(
            results,
            imageWidth,
            imageHeight,
            inputWidth,
            inputHeight,
            scoreThreshold,
            out detections))
        {
            return true;
        }

        var tensors = results
            .Select(r => r.Value)
            .OfType<Tensor<float>>()
            .Where(t => t.Rank == 4 && t.Dimensions[0] == 1)
            .ToList();

        if (tensors.Count == 0)
        {
            return false;
        }

        var grouped = new Dictionary<(int h, int w), (Tensor<float>? score, Tensor<float>? bbox)>();
        foreach (var tensor in tensors)
        {
            var channels = tensor.Dimensions[1];
            var h = tensor.Dimensions[2];
            var w = tensor.Dimensions[3];

            if (!grouped.TryGetValue((h, w), out var stage))
            {
                stage = (null, null);
            }

            if ((channels == 1 || channels == 2) && stage.score is null)
            {
                stage.score = tensor;
            }
            else if (channels == 4 && stage.bbox is null)
            {
                stage.bbox = tensor;
            }

            grouped[(h, w)] = stage;
        }

        var foundStage = false;
        foreach (var ((h, w), stage) in grouped)
        {
            if (stage.score is null || stage.bbox is null)
            {
                continue;
            }

            foundStage = true;
            var scoreTensor = stage.score;
            var bboxTensor = stage.bbox;

            var strideX = (double)inputWidth / w;
            var strideY = (double)inputHeight / h;

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var scoreRaw = scoreTensor.Dimensions[1] == 1
                        ? scoreTensor[0, 0, y, x]
                        : scoreTensor[0, 1, y, x];

                    var score = NormalizeScore(scoreRaw);
                    if (score < scoreThreshold)
                    {
                        continue;
                    }

                    var l = bboxTensor[0, 0, y, x] * strideX;
                    var t = bboxTensor[0, 1, y, x] * strideY;
                    var r = bboxTensor[0, 2, y, x] * strideX;
                    var b = bboxTensor[0, 3, y, x] * strideY;

                    var cx = (x + 0.5) * strideX;
                    var cy = (y + 0.5) * strideY;

                    var x1 = cx - l;
                    var y1 = cy - t;
                    var x2 = cx + r;
                    var y2 = cy + b;

                    var scaleX = (double)imageWidth / inputWidth;
                    var scaleY = (double)imageHeight / inputHeight;

                    var left = Math.Clamp((int)Math.Round(x1 * scaleX), 0, imageWidth - 1);
                    var top = Math.Clamp((int)Math.Round(y1 * scaleY), 0, imageHeight - 1);
                    var right = Math.Clamp((int)Math.Round(x2 * scaleX), left + 1, imageWidth);
                    var bottom = Math.Clamp((int)Math.Round(y2 * scaleY), top + 1, imageHeight);

                    detections.Add((
                        new FaceBoundingBox(left, top, right - left, bottom - top),
                        score));
                }
            }
        }

        return foundStage;
    }

    private static bool TryParseScrfdFlatDetections(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        int imageWidth,
        int imageHeight,
        int inputWidth,
        int inputHeight,
        double scoreThreshold,
        out List<(FaceBoundingBox box, double confidence)> detections)
    {
        detections = [];

        var scoreByStride = new Dictionary<int, Tensor<float>>();
        var bboxByStride = new Dictionary<int, Tensor<float>>();

        foreach (var output in results)
        {
            if (output.Value is not Tensor<float> tensor || tensor.Rank != 3 || tensor.Dimensions[0] != 1)
            {
                continue;
            }

            if (!TryParseStride(output.Name, out var stride))
            {
                continue;
            }

            var lastDim = tensor.Dimensions[2];
            if (lastDim == 1 || lastDim == 2)
            {
                scoreByStride[stride] = tensor;
            }
            else if (lastDim == 4)
            {
                bboxByStride[stride] = tensor;
            }
        }

        var commonStrides = scoreByStride.Keys.Intersect(bboxByStride.Keys).OrderBy(s => s).ToList();
        if (commonStrides.Count == 0)
        {
            return false;
        }

        foreach (var stride in commonStrides)
        {
            var scoreTensor = scoreByStride[stride];
            var bboxTensor = bboxByStride[stride];

            var n = scoreTensor.Dimensions[1];
            if (bboxTensor.Dimensions[1] != n)
            {
                continue;
            }

            var gridW = Math.Max(1, inputWidth / stride);
            var gridH = Math.Max(1, inputHeight / stride);
            var locationCount = gridW * gridH;
            var anchorsPerLocation = n >= locationCount && n % locationCount == 0
                ? Math.Max(1, n / locationCount)
                : 1;

            for (var i = 0; i < n; i++)
            {
                var scoreRaw = scoreTensor.Dimensions[2] == 1
                    ? scoreTensor[0, i, 0]
                    : scoreTensor[0, i, 1];

                var score = NormalizeScore(scoreRaw);
                if (score < scoreThreshold)
                {
                    continue;
                }

                var cellIndex = i / anchorsPerLocation;
                var x = cellIndex % gridW;
                var y = cellIndex / gridW;
                if (y < 0 || y >= gridH)
                {
                    continue;
                }

                var l = bboxTensor[0, i, 0] * stride;
                var t = bboxTensor[0, i, 1] * stride;
                var r = bboxTensor[0, i, 2] * stride;
                var b = bboxTensor[0, i, 3] * stride;

                var cx = (x + 0.5) * stride;
                var cy = (y + 0.5) * stride;

                var x1 = cx - l;
                var y1 = cy - t;
                var x2 = cx + r;
                var y2 = cy + b;

                var scaleX = (double)imageWidth / inputWidth;
                var scaleY = (double)imageHeight / inputHeight;

                var left = Math.Clamp((int)Math.Round(x1 * scaleX), 0, imageWidth - 1);
                var top = Math.Clamp((int)Math.Round(y1 * scaleY), 0, imageHeight - 1);
                var right = Math.Clamp((int)Math.Round(x2 * scaleX), left + 1, imageWidth);
                var bottom = Math.Clamp((int)Math.Round(y2 * scaleY), top + 1, imageHeight);

                detections.Add((
                    new FaceBoundingBox(left, top, right - left, bottom - top),
                    score));
            }
        }

        return true;
    }

    private static bool TryParseStride(string outputName, out int stride)
    {
        stride = 0;
        var separator = outputName.LastIndexOf('_');
        if (separator < 0 || separator == outputName.Length - 1)
        {
            return false;
        }

        return int.TryParse(outputName[(separator + 1)..], out stride) && stride > 0;
    }

    private static bool TryParseCommonDetections(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        int imageWidth,
        int imageHeight,
        double scoreThreshold,
        out List<(FaceBoundingBox box, double confidence)> detections)
    {
        detections = [];

        // Common detector output shape: [1, N, 6] => x1, y1, x2, y2, score, class
        var candidate = results.FirstOrDefault(r => r.Value is Tensor<float>);
        if (candidate?.Value is not Tensor<float> tensor)
        {
            return false;
        }

        if (tensor.Rank != 3 || tensor.Dimensions[0] != 1 || tensor.Dimensions[2] < 5)
        {
            return false;
        }

        var rows = tensor.Dimensions[1];
        for (var i = 0; i < rows; i++)
        {
            var score = tensor[0, i, 4];
            if (score < scoreThreshold)
            {
                continue;
            }

            var x1 = tensor[0, i, 0];
            var y1 = tensor[0, i, 1];
            var x2 = tensor[0, i, 2];
            var y2 = tensor[0, i, 3];

            // If coordinates look normalized, map to image pixels.
            var normalized = x2 <= 1.5f && y2 <= 1.5f && x1 >= 0f && y1 >= 0f;
            if (normalized)
            {
                x1 *= imageWidth;
                x2 *= imageWidth;
                y1 *= imageHeight;
                y2 *= imageHeight;
            }

            var left = Math.Clamp((int)Math.Round(Math.Min(x1, x2)), 0, imageWidth - 1);
            var top = Math.Clamp((int)Math.Round(Math.Min(y1, y2)), 0, imageHeight - 1);
            var right = Math.Clamp((int)Math.Round(Math.Max(x1, x2)), left + 1, imageWidth);
            var bottom = Math.Clamp((int)Math.Round(Math.Max(y1, y2)), top + 1, imageHeight);

            detections.Add((
                new FaceBoundingBox(left, top, right - left, bottom - top),
                score));
        }

        return true;
    }

    private static List<(FaceBoundingBox box, double confidence)> ApplyNms(
        List<(FaceBoundingBox box, double confidence)> detections,
        double iouThreshold)
    {
        var sorted = detections
            .OrderByDescending(d => d.confidence)
            .ToList();

        var kept = new List<(FaceBoundingBox box, double confidence)>();
        while (sorted.Count > 0)
        {
            var current = sorted[0];
            kept.Add(current);
            sorted.RemoveAt(0);

            sorted = sorted
                .Where(candidate => ComputeIou(current.box, candidate.box) < iouThreshold)
                .ToList();
        }

        return kept;
    }

    private static double ComputeIou(FaceBoundingBox a, FaceBoundingBox b)
    {
        var ax2 = a.X + a.Width;
        var ay2 = a.Y + a.Height;
        var bx2 = b.X + b.Width;
        var by2 = b.Y + b.Height;

        var interLeft = Math.Max(a.X, b.X);
        var interTop = Math.Max(a.Y, b.Y);
        var interRight = Math.Min(ax2, bx2);
        var interBottom = Math.Min(ay2, by2);

        var interWidth = Math.Max(0, interRight - interLeft);
        var interHeight = Math.Max(0, interBottom - interTop);
        var interArea = interWidth * interHeight;

        var areaA = Math.Max(1, a.Width * a.Height);
        var areaB = Math.Max(1, b.Width * b.Height);
        var union = areaA + areaB - interArea;

        return union <= 0 ? 0 : (double)interArea / union;
    }

    private static double NormalizeScore(float rawScore)
    {
        if (rawScore is >= 0f and <= 1f)
        {
            return rawScore;
        }

        var score = 1.0 / (1.0 + Math.Exp(-rawScore));
        return Math.Clamp(score, 0.0, 1.0);
    }
}
