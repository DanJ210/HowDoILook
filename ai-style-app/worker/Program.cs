using AiStyleApp.Data;
using AiStyleApp.Worker;
using AiStyleApp.Worker.Handlers;
using AiStyleApp.Worker.Services.Onnx;
using AiStyleApp.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<WorkerFeatureFlags>(builder.Configuration.GetSection("Features"));
builder.Services.Configure<FaceAnalysisThresholds>(builder.Configuration.GetSection("FaceAnalysis:Thresholds"));
builder.Services.Configure<OnnxFaceDetectionOptions>(builder.Configuration.GetSection("FaceAnalysis:OnnxFaceDetection"));
builder.Services.Configure<OnnxLandmarkOptions>(builder.Configuration.GetSection("FaceAnalysis:OnnxLandmarks"));

// Database
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Replicate HTTP client
builder.Services.AddHttpClient<IReplicateWorkerClient, ReplicateWorkerClient>();
builder.Services.AddSingleton<HeuristicFaceDetectorStage>();
builder.Services.AddSingleton<IOnnxSessionFactory, OnnxSessionFactory>();
builder.Services.AddScoped<OnnxFaceDetectorStage>();
builder.Services.AddScoped<IFaceDetectorStage>(sp =>
{
    var features = sp.GetRequiredService<IOptions<WorkerFeatureFlags>>().Value;
    return features.OnnxFaceDetection
        ? sp.GetRequiredService<OnnxFaceDetectorStage>()
        : sp.GetRequiredService<HeuristicFaceDetectorStage>();
});
builder.Services.AddScoped<IFaceQualityStage, HeuristicFaceQualityStage>();
builder.Services.AddSingleton<HeuristicFaceLandmarkModelStage>();
builder.Services.AddScoped<OnnxFaceLandmarkStage>();
builder.Services.AddScoped<IFaceLandmarkModelStage>(sp =>
{
    var features = sp.GetRequiredService<IOptions<WorkerFeatureFlags>>().Value;
    return features.OnnxLandmarks
        ? sp.GetRequiredService<OnnxFaceLandmarkStage>()
        : sp.GetRequiredService<HeuristicFaceLandmarkModelStage>();
});
builder.Services.AddScoped<IFaceLandmarkStage, HeuristicFaceLandmarkStage>();
builder.Services.AddScoped<IFaceRegionEstimationStage, HeuristicFaceRegionEstimationStage>();
builder.Services.AddScoped<IFaceSegmentationStage, HeuristicFaceSegmentationStage>();
builder.Services.AddScoped<IRecommendationStage, RuleBasedRecommendationStage>();
builder.Services.AddScoped<IFaceAnalysisPipeline, FaceAnalysisPipeline>();
builder.Services.AddSingleton<IWorkerQueuePublisher, WorkerQueuePublisher>();
builder.Services.AddSingleton<IMetricsLogger, MetricsLogger>();

builder.Services.AddHostedService<JobWorker>();
builder.Services.AddScoped<StyleJobHandler>();
builder.Services.AddScoped<FaceAnalysisJobHandler>();
builder.Services.AddScoped<IMessageHandler, MessageRouterHandler>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("WorkerStartup");
    var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
    var features = scope.ServiceProvider.GetRequiredService<IOptions<WorkerFeatureFlags>>().Value;
    var onnxFace = scope.ServiceProvider.GetRequiredService<IOptions<OnnxFaceDetectionOptions>>().Value;
    var onnxLandmarks = scope.ServiceProvider.GetRequiredService<IOptions<OnnxLandmarkOptions>>().Value;

    logger.LogInformation(
        "Worker startup config: Environment={EnvironmentName}, ContentRoot={ContentRoot}, OnnxFaceDetection={OnnxFaceDetection}, FaceModelPath={FaceModelPath}, FaceModelType={FaceModelType}, FaceInput={FaceInputWidth}x{FaceInputHeight}, FaceExecutionProvider={FaceExecutionProvider}, OnnxLandmarks={OnnxLandmarks}, LandmarkModelPath={LandmarkModelPath}, LandmarkModelType={LandmarkModelType}, LandmarkInput={LandmarkInputWidth}x{LandmarkInputHeight}, LandmarkExecutionProvider={LandmarkExecutionProvider}",
        env.EnvironmentName,
        env.ContentRootPath,
        features.OnnxFaceDetection,
        onnxFace.ModelPath,
        onnxFace.ModelType,
        onnxFace.InputWidth,
        onnxFace.InputHeight,
        onnxFace.ExecutionProvider,
        features.OnnxLandmarks,
        onnxLandmarks.ModelPath,
        onnxLandmarks.ModelType,
        onnxLandmarks.InputWidth,
        onnxLandmarks.InputHeight,
        onnxLandmarks.ExecutionProvider);
}

host.Run();

