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
        var queueName = _config["Queue:QueueName"] ?? "style-jobs";

        var client = new QueueClient(connectionString, queueName);

        // Retry queue connection on startup (e.g. Azurite may not be ready yet)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await client.CreateIfNotExistsAsync(cancellationToken: stoppingToken);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Queue not reachable yet, retrying in 5s: {Message}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("Worker started. Polling queue '{QueueName}'.", queueName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await client.ReceiveMessageAsync(cancellationToken: stoppingToken);
                var message = response?.Value;

                if (message is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler>();

                try
                {
                    await handler.HandleAsync(message.Body.ToString(), stoppingToken);
                    await client.DeleteMessageAsync(message.MessageId, message.PopReceipt, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process message {MessageId}.", message.MessageId);
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
}
