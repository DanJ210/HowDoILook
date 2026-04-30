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
        _client = new QueueClient(opts.ConnectionString, opts.QueueName);
    }

    public async Task PublishAsync<T>(T message, CancellationToken ct = default)
    {
        await _client.CreateIfNotExistsAsync(cancellationToken: ct);
        var json = JsonSerializer.Serialize(message);
        await _client.SendMessageAsync(json, cancellationToken: ct);
    }
}
