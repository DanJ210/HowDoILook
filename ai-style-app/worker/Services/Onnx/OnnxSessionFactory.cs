using Microsoft.ML.OnnxRuntime;

namespace AiStyleApp.Worker.Services.Onnx;

public interface IOnnxSessionFactory
{
    InferenceSession Create(string modelPath, string executionProvider);
}

public class OnnxSessionFactory : IOnnxSessionFactory
{
    public InferenceSession Create(string modelPath, string executionProvider)
    {
        using var options = new SessionOptions();

        // CPU is the default provider and is always available.
        if (string.Equals(executionProvider, "CPU", StringComparison.OrdinalIgnoreCase))
        {
            return new InferenceSession(modelPath, options);
        }

        // If a non-CPU provider is requested, fallback to CPU unless the runtime supports it.
        return new InferenceSession(modelPath, options);
    }
}
