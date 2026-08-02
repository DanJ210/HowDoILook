namespace AiStyleApp.Api.Infrastructure;

public class QueueOptions
{
    public const string Section = "Queue";
    public string ConnectionString { get; init; } = string.Empty;
    public string QueueName { get; init; } = "style-jobs";
    public string FaceAnalysisQueueName { get; init; } = "analysis-jobs";
    public string StyleQueueName { get; init; } = "style-jobs";
}
