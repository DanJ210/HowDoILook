namespace AiStyleApp.Worker.Services;

/// <summary>
/// Identifies which face/hair feature is used as a scoring input.
/// Allows each catalog entry to declare its feature contributions by name.
/// </summary>
public enum StyleFeatureName
{
    HairDensity,
    JawWidthRatio,
    ForeheadHeightRatio,
    FaceElongation,
    BeardDensity,
    AnalysisConfidence
}

/// <summary>
/// A named feature contribution to a style's score.
/// Sign = 1.0 uses the feature value directly; Sign = -1.0 inverts it (equivalent to 1.0 - value).
/// </summary>
public record StyleFeatureWeight(StyleFeatureName Feature, double Sign = 1.0);

/// <summary>
/// A single entry in the style catalog describing how a style scores against face telemetry.
/// </summary>
public record StyleEntry(
    string StyleId,
    string StyleName,
    double Baseline,
    IReadOnlyList<StyleFeatureWeight> FeatureWeights,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Constraints,
    bool RequiresBeard = false,
    double BeardDensityThreshold = 0.0,
    // Per-shape score adjustment added on top of the feature-weight score.
    // Positive values favour this style for the given shape; negative values discourage it.
    // Shapes absent from this dict default to 0.0 (no adjustment).
    IReadOnlyDictionary<FaceShape, double>? ShapePriors = null
);

/// <summary>
/// Snapshot of the ranked feature inputs passed to the mapper.
/// </summary>
public record StyleRankingInputs(
    double JawWidthRatio,
    double ForeheadHeightRatio,
    double FaceElongation,
    double HairDensityEstimate,
    double BeardDensityEstimate,
    double AnalysisConfidence,
    string? Gender,
    bool AllowBeardSuggestions
);

/// <summary>
/// A scored and ranked style candidate produced by the mapper.
/// </summary>
public record RecommendationCandidate(
    string StyleId,
    string StyleName,
    double Score,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Constraints
);

/// <summary>
/// Maps face telemetry features to ranked style recommendations.
/// The catalog defines what styles exist and which features drive their scoring.
/// Scoring: score = Clamp01(Baseline + sum((featureValue - 0.5) * 0.15) for each weighted feature).
/// </summary>
public static class StyleRecommendationMapper
{
    /// <summary>
    /// The style catalog. Add, remove, or retune entries here to evolve recommendations
    /// without touching scoring logic.
    /// </summary>
    internal static readonly IReadOnlyList<StyleEntry> Catalog = new[]
    {
        new StyleEntry(
            StyleId: "textured-crop",
            StyleName: "Textured Crop",
            Baseline: 0.62,
            FeatureWeights: new[]
            {
                new StyleFeatureWeight(StyleFeatureName.HairDensity),
                new StyleFeatureWeight(StyleFeatureName.JawWidthRatio),
                new StyleFeatureWeight(StyleFeatureName.FaceElongation),
                new StyleFeatureWeight(StyleFeatureName.AnalysisConfidence)
            },
            Reasons: new[]
            {
                "Adds controlled texture without excess volume",
                "Works well when jaw definition is moderate to strong"
            },
            Constraints: new[] { "Best maintained with trims every 3-4 weeks" },
            ShapePriors: new Dictionary<FaceShape, double>
            {
                [FaceShape.Oval]    =  0.05,  // universally flattering
                [FaceShape.Square]  =  0.05,  // texture softens angular jaw
                [FaceShape.Heart]   =  0.05,  // works with tapered proportions
                [FaceShape.Diamond] =  0.08,  // adds crown dimension
                [FaceShape.Oblong]  =  0.0,
                [FaceShape.Round]   =  0.0
            }),

        new StyleEntry(
            StyleId: "classic-side-part",
            StyleName: "Classic Side Part",
            Baseline: 0.58,
            FeatureWeights: new[]
            {
                new StyleFeatureWeight(StyleFeatureName.HairDensity),
                new StyleFeatureWeight(StyleFeatureName.ForeheadHeightRatio, Sign: -1.0),
                new StyleFeatureWeight(StyleFeatureName.FaceElongation),
                new StyleFeatureWeight(StyleFeatureName.AnalysisConfidence)
            },
            Reasons: new[]
            {
                "Balanced shape for most face proportions",
                "Professional and versatile styling profile"
            },
            Constraints: new[] { "Needs light product for hold and direction" },
            ShapePriors: new Dictionary<FaceShape, double>
            {
                [FaceShape.Oval]    =  0.05,
                [FaceShape.Heart]   =  0.08,  // asymmetric part breaks up wide forehead
                [FaceShape.Oblong]  =  0.08,  // horizontal emphasis adds width
                [FaceShape.Round]   =  0.03,  // asymmetry helps
                [FaceShape.Square]  = -0.05,  // hard part emphasises angular jaw
                [FaceShape.Diamond] =  0.03
            }),

        new StyleEntry(
            StyleId: "short-quiff",
            StyleName: "Short Quiff",
            Baseline: 0.55,
            FeatureWeights: new[]
            {
                new StyleFeatureWeight(StyleFeatureName.HairDensity),
                new StyleFeatureWeight(StyleFeatureName.ForeheadHeightRatio),
                new StyleFeatureWeight(StyleFeatureName.FaceElongation, Sign: -1.0),
                new StyleFeatureWeight(StyleFeatureName.AnalysisConfidence)
            },
            Reasons: new[]
            {
                "Adds vertical emphasis to balance wider lower face",
                "Keeps sides controlled while retaining top movement"
            },
            Constraints: new[] { "Requires blow-dry or styling routine for shape" },
            ShapePriors: new Dictionary<FaceShape, double>
            {
                [FaceShape.Round]   =  0.10,  // height counters width — strong fit
                [FaceShape.Oval]    =  0.03,
                [FaceShape.Square]  =  0.0,
                [FaceShape.Heart]   = -0.05,  // top volume emphasises already-wide forehead
                [FaceShape.Diamond] = -0.03,
                [FaceShape.Oblong]  = -0.08   // adds height to an already-long face
            }),

        new StyleEntry(
            StyleId: "short-boxed-beard",
            StyleName: "Short Boxed Beard Pairing",
            Baseline: 0.50,
            FeatureWeights: new[]
            {
                new StyleFeatureWeight(StyleFeatureName.BeardDensity),
                new StyleFeatureWeight(StyleFeatureName.JawWidthRatio),
                new StyleFeatureWeight(StyleFeatureName.ForeheadHeightRatio, Sign: -1.0),
                new StyleFeatureWeight(StyleFeatureName.AnalysisConfidence)
            },
            Reasons: new[]
            {
                "Facial hair density supports a clean short beard contour",
                "Can reinforce jaw framing when paired with short sides"
            },
            Constraints: new[] { "Optional recommendation and can be skipped" },
            RequiresBeard: true,
            BeardDensityThreshold: 0.25,
            ShapePriors: new Dictionary<FaceShape, double>
            {
                [FaceShape.Heart]   =  0.08,  // adds jaw width to balance forehead
                [FaceShape.Diamond] =  0.08,  // adds jaw definition
                [FaceShape.Round]   =  0.05,  // structure defines jawline
                [FaceShape.Oval]    =  0.03,
                [FaceShape.Oblong]  =  0.03,
                [FaceShape.Square]  = -0.05   // over-emphasises angular jaw
            })
    };

    /// <summary>
    /// Returns up to 5 ranked recommendation candidates for the given inputs.
    /// </summary>
    public static IReadOnlyList<RecommendationCandidate> Rank(StyleRankingInputs inputs)
    {
        var faceShape = FaceShapeClassifier.Classify(
            inputs.JawWidthRatio,
            inputs.ForeheadHeightRatio,
            inputs.FaceElongation);

        return Catalog
            .Where(entry => IsEligible(entry, inputs))
            .Select(entry => ComputeScore(entry, inputs, faceShape))
            .OrderByDescending(c => c.Score)
            .Take(5)
            .ToList();
    }

    private static bool IsEligible(StyleEntry entry, StyleRankingInputs inputs)
    {
        if (!entry.RequiresBeard)
        {
            return true;
        }

        return inputs.AllowBeardSuggestions
            && string.Equals(inputs.Gender, "male", StringComparison.OrdinalIgnoreCase)
            && inputs.BeardDensityEstimate > entry.BeardDensityThreshold;
    }

    private static RecommendationCandidate ComputeScore(StyleEntry entry, StyleRankingInputs inputs, FaceShape faceShape)
    {
        var featureAdjustment = entry.FeatureWeights.Sum(w => (GetFeatureValue(w, inputs) - 0.5) * 0.15);
        var shapePrior = entry.ShapePriors is not null && entry.ShapePriors.TryGetValue(faceShape, out var prior)
            ? prior
            : 0.0;
        var score = Math.Round(Math.Clamp(entry.Baseline + featureAdjustment + shapePrior, 0.0, 1.0), 3);

        return new RecommendationCandidate(
            StyleId: entry.StyleId,
            StyleName: entry.StyleName,
            Score: score,
            Reasons: entry.Reasons,
            Constraints: entry.Constraints);
    }

    private static double GetFeatureValue(StyleFeatureWeight weight, StyleRankingInputs inputs)
    {
        var raw = weight.Feature switch
        {
            StyleFeatureName.HairDensity => inputs.HairDensityEstimate,
            StyleFeatureName.JawWidthRatio => inputs.JawWidthRatio,
            StyleFeatureName.ForeheadHeightRatio => inputs.ForeheadHeightRatio,
            StyleFeatureName.FaceElongation => inputs.FaceElongation,
            StyleFeatureName.BeardDensity => inputs.BeardDensityEstimate,
            StyleFeatureName.AnalysisConfidence => inputs.AnalysisConfidence,
            _ => 0.5
        };

        return weight.Sign >= 0 ? raw : 1.0 - raw;
    }
}
