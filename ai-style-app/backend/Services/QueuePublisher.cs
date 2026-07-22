using AiStyleApp.Api.Infrastructure;
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
    private readonly QueueClient _client;

    public QueuePublisher(IOptions<QueueOptions> options)
    {
        var opts = options.Value;
        var queueName = ResolveQueueName(opts.FaceAnalysisQueueName, opts.QueueName, "analysis-jobs");
        _client = new QueueClient(opts.ConnectionString, queueName);
    }

    public async Task PublishAsync<T>(T message, CancellationToken ct = default)
    {
        await _client.CreateIfNotExistsAsync(cancellationToken: ct);
        var json = JsonSerializer.Serialize(message);
        await _client.SendMessageAsync(json, cancellationToken: ct);
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
