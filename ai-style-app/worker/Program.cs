using AiStyleApp.Data;
using AiStyleApp.Worker;
using AiStyleApp.Worker.Handlers;
using AiStyleApp.Worker.Services;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<WorkerFeatureFlags>(builder.Configuration.GetSection("Features"));
builder.Services.Configure<FaceAnalysisThresholds>(builder.Configuration.GetSection("FaceAnalysis:Thresholds"));

// Database
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Replicate HTTP client
builder.Services.AddHttpClient<IReplicateWorkerClient, ReplicateWorkerClient>();
builder.Services.AddScoped<IFaceDetectorStage, HeuristicFaceDetectorStage>();
builder.Services.AddScoped<IFaceQualityStage, HeuristicFaceQualityStage>();
builder.Services.AddScoped<IFaceLandmarkModelStage, HeuristicFaceLandmarkModelStage>();
builder.Services.AddScoped<IFaceLandmarkStage, HeuristicFaceLandmarkStage>();
builder.Services.AddScoped<IFaceRegionEstimationStage, HeuristicFaceRegionEstimationStage>();
builder.Services.AddScoped<IFaceSegmentationStage, HeuristicFaceSegmentationStage>();
builder.Services.AddScoped<IRecommendationStage, RuleBasedRecommendationStage>();
builder.Services.AddScoped<IFaceAnalysisPipeline, FaceAnalysisPipeline>();

builder.Services.AddHostedService<JobWorker>();
builder.Services.AddScoped<StyleJobHandler>();
builder.Services.AddScoped<FaceAnalysisJobHandler>();
builder.Services.AddScoped<IMessageHandler, MessageRouterHandler>();

var host = builder.Build();
host.Run();

