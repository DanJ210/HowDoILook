using AiStyleApp.Worker.Handlers;
using Azure.Storage.Queues;

namespace AiStyleApp.Worker;

public class JobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<JobWorker> _logger;

    public JobWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<JobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = _config["Queue:ConnectionString"]
            ?? throw new InvalidOperationException("Queue:ConnectionString is not configured.");
        var legacyQueueName = ResolveQueueName(_config["Queue:QueueName"], "style-jobs");
        var faceAnalysisQueueName = ResolveQueueName(_config["Queue:FaceAnalysisQueueName"], legacyQueueName);
        var styleQueueName = ResolveQueueName(_config["Queue:StyleQueueName"], legacyQueueName);

        var queueClients = new List<(string QueueName, QueueClient Client)>
        {
            (faceAnalysisQueueName, new QueueClient(connectionString, faceAnalysisQueueName))
        };

        if (!string.Equals(styleQueueName, faceAnalysisQueueName, StringComparison.OrdinalIgnoreCase))
        {
            queueClients.Add((styleQueueName, new QueueClient(connectionString, styleQueueName)));
        }

        // Retry queue connection on startup (e.g. Azurite may not be ready yet)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var (_, client) in queueClients)
                {
                    await client.CreateIfNotExistsAsync(cancellationToken: stoppingToken);
                }

                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Queue not reachable yet, retrying in 5s: {Message}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation(
            "Worker started. Polling queue(s): {QueueNames}.",
            string.Join(", ", queueClients.Select(x => x.QueueName)));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedAny = false;
                foreach (var (queueName, client) in queueClients)
                {
                    processedAny = await TryProcessMessageAsync(queueName, client, stoppingToken) || processedAny;
                }

                if (!processedAny)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Queue poll error, retrying in 10s: {Message}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task<bool> TryProcessMessageAsync(string queueName, QueueClient client, CancellationToken stoppingToken)
    {
        var response = await client.ReceiveMessageAsync(cancellationToken: stoppingToken);
        var message = response?.Value;
        if (message is null)
        {
            return false;
        }

        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler>();

        try
        {
            await handler.HandleAsync(message.Body.ToString(), stoppingToken);
            await client.DeleteMessageAsync(message.MessageId, message.PopReceipt, stoppingToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process message {MessageId} from queue {QueueName}.",
                message.MessageId,
                queueName);
            return false;
        }
    }

    private static string ResolveQueueName(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
