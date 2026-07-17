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
    private readonly IConfiguration _configuration;

    public WorkerQueuePublisher(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task PublishAsync(StyleJob job, CancellationToken ct = default)
    {
        var connectionString = _configuration["Queue:ConnectionString"]
            ?? throw new InvalidOperationException("Queue:ConnectionString is not configured.");
        var queueName = _configuration["Queue:QueueName"] ?? "style-jobs";

        var client = new QueueClient(connectionString, queueName);
        await client.CreateIfNotExistsAsync(cancellationToken: ct);

        var messageBody = JsonSerializer.Serialize(job);
        await client.SendMessageAsync(messageBody, cancellationToken: ct);
    }
}