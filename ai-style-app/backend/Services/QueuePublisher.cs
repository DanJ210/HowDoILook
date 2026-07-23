using AiStyleApp.Api.Infrastructure;
using AiStyleApp.Data.Queue;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace AiStyleApp.Api.Services;

public interface IQueuePublisher
{
    Task PublishAsync<T>(T message, CancellationToken ct = default);
}

public class QueuePublisher : IQueuePublisher
{
    private readonly QueueClient _analysisClient;
    private readonly QueueClient _styleClient;

    public QueuePublisher(IOptions<QueueOptions> options)
    {
        var opts = options.Value;
        var analysisQueueName = ResolveQueueName(opts.FaceAnalysisQueueName, opts.QueueName, "analysis-jobs");
        var styleQueueName = ResolveQueueName(opts.StyleQueueName, opts.QueueName, "style-jobs");

        _analysisClient = new QueueClient(opts.ConnectionString, analysisQueueName);
        _styleClient = new QueueClient(opts.ConnectionString, styleQueueName);
    }

    public async Task PublishAsync<T>(T message, CancellationToken ct = default)
    {
        var client = ResolveQueueClient(message);
        await client.CreateIfNotExistsAsync(cancellationToken: ct);
        var json = JsonSerializer.Serialize(message);
        await client.SendMessageAsync(json, cancellationToken: ct);
    }

    private QueueClient ResolveQueueClient<T>(T message)
    {
        if (message is StyleJob styleJob)
        {
            return string.Equals(styleJob.JobType, "face-analysis", StringComparison.OrdinalIgnoreCase)
                ? _analysisClient
                : _styleClient;
        }

        return _analysisClient;
    }

    private static string ResolveQueueName(string? preferred, string? fallback, string defaultName)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return defaultName;
    }
}
