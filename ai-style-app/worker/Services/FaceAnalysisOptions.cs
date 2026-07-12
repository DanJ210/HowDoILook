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
