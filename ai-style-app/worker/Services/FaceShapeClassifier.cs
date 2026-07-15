namespace AiStyleApp.Worker.Services;

/// <summary>
/// The six canonical face shapes used for hairstyle and beard recommendation fitting.
/// </summary>
public enum FaceShape
{
    Unknown,
    Oval,
    Round,
    Square,
    Heart,
    Diamond,
    Oblong
}

/// <summary>
/// Classifies face shape from landmark-derived geometry features.
/// Thresholds are based on published barbering/styling geometry guides and intended as
/// a tunable starting point — update against labeled portrait data as it is collected.
/// </summary>
public static class FaceShapeClassifier
{
    /// <summary>
    /// Classifies the face shape from three normalized landmark ratios.
    /// All inputs are expected in the range [0, 1].
    /// </summary>
    /// <param name="jawWidthRatio">Jaw width relative to mid-face cheek width.</param>
    /// <param name="foreheadHeightRatio">Forehead width relative to overall face width.</param>
    /// <param name="faceElongation">Normalized height-to-width ratio (0.5 ≈ square, higher = longer).</param>
    public static FaceShape Classify(double jawWidthRatio, double foreheadHeightRatio, double faceElongation)
    {
        if (!IsNormalized(jawWidthRatio) || !IsNormalized(foreheadHeightRatio) || !IsNormalized(faceElongation))
        {
            return FaceShape.Unknown;
        }

        // Square: low elongation + very wide jaw line.
        if (faceElongation < 0.42 && jawWidthRatio > 0.92)
        {
            return FaceShape.Square;
        }

        // Round: low elongation + wide but not squared-off jaw.
        if (faceElongation < 0.47 && jawWidthRatio > 0.87)
        {
            return FaceShape.Round;
        }

        // Oblong: clearly longer than wide, without strong jaw narrowing.
        if (faceElongation > 0.57 && jawWidthRatio < 0.85)
        {
            return FaceShape.Oblong;
        }

        // Heart (inverted triangle): wide forehead, noticeably narrow jaw.
        if (foreheadHeightRatio > 0.82 && jawWidthRatio < 0.78)
        {
            return FaceShape.Heart;
        }

        // Diamond: narrow both at forehead and jaw (wide cheekbones).
        if (foreheadHeightRatio < 0.72 && jawWidthRatio < 0.80)
        {
            return FaceShape.Diamond;
        }

        // Oval: balanced proportions — the default when no other shape matches.
        return FaceShape.Oval;
    }

    private static bool IsNormalized(double value)
    {
        return double.IsFinite(value) && value >= 0d && value <= 1d;
    }
}
