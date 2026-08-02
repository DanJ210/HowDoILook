using AiStyleApp.Worker.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiStyleApp.Tests;

public class FaceSegmentationStageTests
{
    [Fact]
    public void Extract_LowerFaceSignal_ReportsBeardDensityAcrossBothPaths()
    {
        using var image = new Image<Rgba32>(100, 100, new Rgba32(255, 255, 255));
        for (var pixelY = 62; pixelY < 92; pixelY++)
        {
            for (var pixelX = 25; pixelX < 75; pixelX++)
            {
                image[pixelX, pixelY] = new Rgba32(0, 0, 0);
            }
        }

        var segmentation = new HeuristicFaceSegmentationStage().Extract(image);
        var regionEstimation = new HeuristicFaceRegionEstimationStage().Extract(
            image,
            new FaceBoundingBox(20, 10, 60, 80));

        Assert.True(segmentation.BeardDensityEstimate > 0.9);
        Assert.Equal(segmentation.BeardDensityEstimate, regionEstimation.BeardDensityEstimate);
    }
}