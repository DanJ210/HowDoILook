namespace AiStyleApp.Worker.Handlers;

public interface IMessageHandler
{
    Task HandleAsync(string messageBody, CancellationToken cancellationToken);
}
