using AiStyleApp.Worker.Services;

namespace AiStyleApp.Tests;

public class FaceShapeClassifierTests
{
    [Theory]
    [InlineData(0.95, 0.80, 0.40, FaceShape.Square)]    // very wide jaw, low elongation
    [InlineData(0.90, 0.78, 0.44, FaceShape.Round)]     // wide jaw, low elongation (not quite square)
    [InlineData(0.75, 0.78, 0.62, FaceShape.Oblong)]    // narrow jaw, clearly elongated
    [InlineData(0.70, 0.88, 0.50, FaceShape.Heart)]     // wide forehead, narrow jaw
    [InlineData(0.75, 0.68, 0.50, FaceShape.Diamond)]   // narrow forehead and jaw
    [InlineData(0.82, 0.80, 0.50, FaceShape.Oval)]      // balanced — default
    public void Classify_ReturnsExpectedShape(double jaw, double forehead, double elongation, FaceShape expected)
    {
        var result = FaceShapeClassifier.Classify(jaw, forehead, elongation);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Classify_SquareTakesPrecedenceOverRound_WhenBothThresholdsExceeded()
    {
        // jaw > 0.92 AND elongation < 0.42 should be Square, not Round.
        var result = FaceShapeClassifier.Classify(jawWidthRatio: 0.94, foreheadHeightRatio: 0.78, faceElongation: 0.39);
        Assert.Equal(FaceShape.Square, result);
    }
}

public class StyleRecommendationMapperShapePriorTests
{
    private static StyleRankingInputs MidpointInputs(string gender = "female") =>
        new StyleRankingInputs(
            JawWidthRatio: 0.5,
            ForeheadHeightRatio: 0.5,
            FaceElongation: 0.5,
            HairDensityEstimate: 0.5,
            BeardDensityEstimate: 0.0,
            AnalysisConfidence: 0.8,
            Gender: gender,
            AllowBeardSuggestions: true);

    [Fact]
    public void Rank_RoundFace_QuiffScoresHigherThanForOval()
    {
        // Round face should get a +0.10 prior on Quiff vs +0.03 for Oval.
        var round = StyleRecommendationMapper.Rank(MidpointInputs() with
        {
            JawWidthRatio = 0.90,
            FaceElongation = 0.44  // triggers Round classification
        });

        var oval = StyleRecommendationMapper.Rank(MidpointInputs() with
        {
            JawWidthRatio = 0.82,
            FaceElongation = 0.50  // triggers Oval
        });

        var quiffRound = round.Single(r => r.StyleId == "short-quiff").Score;
        var quiffOval = oval.Single(r => r.StyleId == "short-quiff").Score;

        Assert.True(quiffRound > quiffOval,
            $"Expected Quiff to score higher for Round ({quiffRound}) than Oval ({quiffOval})");
    }

    [Fact]
    public void Rank_OblongFace_QuiffScoresLowerThanForRound()
    {
        // Oblong face penalises Quiff (-0.08); Round boosts it (+0.10).
        var oblong = StyleRecommendationMapper.Rank(MidpointInputs() with
        {
            JawWidthRatio = 0.78,
            FaceElongation = 0.62  // triggers Oblong
        });

        var round = StyleRecommendationMapper.Rank(MidpointInputs() with
        {
            JawWidthRatio = 0.90,
            FaceElongation = 0.44  // triggers Round
        });

        var quiffOblong = oblong.Single(r => r.StyleId == "short-quiff").Score;
        var quiffRound = round.Single(r => r.StyleId == "short-quiff").Score;

        Assert.True(quiffOblong < quiffRound,
            $"Expected Quiff to score lower for Oblong ({quiffOblong}) than Round ({quiffRound})");
    }

    [Fact]
    public void Rank_ShapePriors_AreStillBoundedBetweenZeroAndOne()
    {
        // Drive toward shapes with extreme priors and confirm no out-of-range scores.
        var extreme = StyleRecommendationMapper.Rank(MidpointInputs() with
        {
            JawWidthRatio = 1.0,
            ForeheadHeightRatio = 1.0,
            FaceElongation = 1.0,
            HairDensityEstimate = 1.0,
            AnalysisConfidence = 1.0
        });

        foreach (var r in extreme)
        {
            Assert.InRange(r.Score, 0.0, 1.0);
        }
    }
}
