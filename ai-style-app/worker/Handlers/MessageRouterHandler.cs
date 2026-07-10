using System.Text.Json;

namespace AiStyleApp.Worker.Handlers;

public class MessageRouterHandler : IMessageHandler
{
    private readonly StyleJobHandler _styleJobHandler;
    private readonly FaceAnalysisJobHandler _faceAnalysisJobHandler;
    private readonly ILogger<MessageRouterHandler> _logger;

    public MessageRouterHandler(
        StyleJobHandler styleJobHandler,
        FaceAnalysisJobHandler faceAnalysisJobHandler,
        ILogger<MessageRouterHandler> logger)
    {
        _styleJobHandler = styleJobHandler;
        _faceAnalysisJobHandler = faceAnalysisJobHandler;
        _logger = logger;
    }

    public async Task HandleAsync(string messageBody, CancellationToken cancellationToken)
    {
        var jobType = TryReadJobType(messageBody);

        if (string.Equals(jobType, "face-analysis", StringComparison.OrdinalIgnoreCase))
        {
            await _faceAnalysisJobHandler.HandleAsync(messageBody, cancellationToken);
            return;
        }

        await _styleJobHandler.HandleAsync(messageBody, cancellationToken);
    }

    private string? TryReadJobType(string messageBody)
    {
        try
        {
            using var document = JsonDocument.Parse(messageBody);
            var root = document.RootElement;

            if (root.TryGetProperty("JobType", out var pascalCase) && pascalCase.ValueKind == JsonValueKind.String)
            {
                return pascalCase.GetString();
            }

            if (root.TryGetProperty("jobType", out var camelCase) && camelCase.ValueKind == JsonValueKind.String)
            {
                return camelCase.GetString();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse queue message for job type routing; defaulting to style handler.");
        }

        return null;
    }
}
