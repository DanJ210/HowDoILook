namespace AiStyleApp.Worker.Services;

public class WorkerFeatureFlags
{
    public bool OnnxFaceDetection { get; set; }
    public bool OnnxLandmarks { get; set; }
    public bool OnnxRegionEstimation { get; set; }
}

public class FaceAnalysisThresholds
{
    public double MinFaceDetectionConfidence { get; set; } = 0.75;
    public double MinLandmarkConfidence { get; set; } = 0.70;
}

public class OnnxFaceDetectionOptions
{
    public string? ModelPath { get; set; }
    public string ModelType { get; set; } = "SCRFD";
    public int InputWidth { get; set; } = 640;
    public int InputHeight { get; set; } = 640;
    public double ScoreThreshold { get; set; } = 0.60;
    public double NmsIouThreshold { get; set; } = 0.40;
    public string ExecutionProvider { get; set; } = "CPU";
}

public class OnnxLandmarkOptions
{
    public string? ModelPath { get; set; }
    public string ModelType { get; set; } = "FAN2_68";
    public int InputWidth { get; set; } = 256;
    public int InputHeight { get; set; } = 256;
    public string ExecutionProvider { get; set; } = "CPU";
}
