using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AiStyleApp.Worker.Services.Onnx;

public static class OnnxImageTensorizer
{
    public static DenseTensor<float> CreateNchwTensor(Image<Rgba32> image, int width, int height)
    {
        using var resized = image.Clone(ctx => ctx.Resize(width, height));

        var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = resized[x, y];
                tensor[0, 0, y, x] = pixel.R / 255f;
                tensor[0, 1, y, x] = pixel.G / 255f;
                tensor[0, 2, y, x] = pixel.B / 255f;
            }
        }

        return tensor;
    }
}
