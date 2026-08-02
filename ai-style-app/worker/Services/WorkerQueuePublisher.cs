using System.Text.Json;
using AiStyleApp.Data.Queue;
using Azure.Storage.Queues;

namespace AiStyleApp.Worker.Services;

public interface IWorkerQueuePublisher
{
    Task PublishAsync(StyleJob job, CancellationToken ct = default);
}

public class WorkerQueuePublisher : IWorkerQueuePublisher
{
    private readonly QueueClient _client;
    private readonly SemaphoreSlim _ensureQueueLock = new(1, 1);
    private volatile bool _queueEnsured;

    public WorkerQueuePublisher(IConfiguration configuration)
    {
        var connectionString = configuration["Queue:ConnectionString"]
            ?? throw new InvalidOperationException("Queue:ConnectionString is not configured.");
        var queueName = ResolveQueueName(
            configuration["Queue:StyleQueueName"],
            configuration["Queue:QueueName"],
            "style-jobs");

        _client = new QueueClient(connectionString, queueName);
    }

    public async Task PublishAsync(StyleJob job, CancellationToken ct = default)
    {
        await EnsureQueueExistsAsync(ct);

        var messageBody = JsonSerializer.Serialize(job);
        await _client.SendMessageAsync(messageBody, cancellationToken: ct);
    }

    private async Task EnsureQueueExistsAsync(CancellationToken ct)
    {
        if (_queueEnsured)
        {
            return;
        }

        await _ensureQueueLock.WaitAsync(ct);
        try
        {
            if (_queueEnsured)
            {
                return;
            }

            await _client.CreateIfNotExistsAsync(cancellationToken: ct);
            _queueEnsured = true;
        }
        finally
        {
            _ensureQueueLock.Release();
        }
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