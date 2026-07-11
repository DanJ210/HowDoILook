using AiStyleApp.Worker.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiStyleApp.Tests;

public class FaceAnalysisQualityStageTests
{
    [Fact]
    public void Evaluate_HighDetailImage_DoesNotFailBlurGate()
    {
        using var image = new Image<Rgba32>(1024, 1024);

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

        var stage = new HeuristicFaceQualityStage();
        var result = stage.Evaluate(image);

        Assert.True(result.Passed, $"Expected quality pass but got {result.FailureCode} with blur {result.BlurScore}");
        Assert.NotEqual("ANALYSIS_QUALITY_TOO_BLURRY", result.FailureCode);
        Assert.True(result.BlurScore >= 0.08, $"Expected blur score above threshold but got {result.BlurScore}");
    }

    [Fact]
    public void Evaluate_FlatImage_FailsBlurGate()
    {
        using var image = new Image<Rgba32>(1024, 1024, new Rgba32(128, 128, 128));

        var stage = new HeuristicFaceQualityStage();
        var result = stage.Evaluate(image);

        Assert.False(result.Passed);
        Assert.Equal("ANALYSIS_QUALITY_TOO_BLURRY", result.FailureCode);
        Assert.True(result.BlurScore < 0.08, $"Expected blur score below threshold but got {result.BlurScore}");
    }
}
