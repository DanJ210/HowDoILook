using AiStyleApp.Worker.Services;
using AiStyleApp.Worker.Services.Onnx;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiStyleApp.Tests;

public class OnnxFaceLandmarkStageTests
{
    [Fact]
    public void Extract_InvalidModelFile_ThrowsExplicitLoadFailedCode()
    {
        var modelPath = CreateInvalidModelFile();
        using var image = CreateTestImage();

        var stage = new OnnxFaceLandmarkStage(
            new OnnxSessionFactory(),
            Options.Create(new OnnxLandmarkOptions
            {
                ModelPath = modelPath,
                ExecutionProvider = "CPU"
            }),
            new TestHostEnvironment(),
            NullLogger<OnnxFaceLandmarkStage>.Instance);

        var exception = Assert.Throws<FaceAnalysisException>(() => stage.Extract(image, null));
        Assert.Equal("ANALYSIS_LANDMARK_MODEL_LOAD_FAILED", exception.Code);
    }

    private static string CreateInvalidModelFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "howdoilook-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var modelPath = Path.Combine(directory, "invalid-landmark.onnx");
        File.WriteAllText(modelPath, "this is not a valid onnx model");
        return modelPath;
    }

    private static Image<Rgba32> CreateTestImage()
    {
        var image = new Image<Rgba32>(1024, 1024);

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var cell = ((x / 8) + (y / 8)) % 2 == 0;
                image[x, y] = cell
                    ? new Rgba32(220, 220, 220)
                    : new Rgba32(40, 40, 40);
            }
        }

        return image;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = nameof(OnnxFaceLandmarkStageTests);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
