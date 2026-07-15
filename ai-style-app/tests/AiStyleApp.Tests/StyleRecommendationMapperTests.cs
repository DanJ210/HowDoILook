using AiStyleApp.Worker.Services;

namespace AiStyleApp.Tests;

public class StyleRecommendationMapperTests
{
    private static StyleRankingInputs MakeInputs(
        double jaw = 0.5,
        double forehead = 0.5,
        double elongation = 0.5,
        double hair = 0.5,
        double beard = 0.0,
        double confidence = 0.8,
        string gender = "female",
        bool allowBeard = true)
        => new StyleRankingInputs(
            JawWidthRatio: jaw,
            ForeheadHeightRatio: forehead,
            FaceElongation: elongation,
            HairDensityEstimate: hair,
            BeardDensityEstimate: beard,
            AnalysisConfidence: confidence,
            Gender: gender,
            AllowBeardSuggestions: allowBeard);

    [Fact]
    public void Rank_FemaleMidpointInputs_ReturnsCatalogStylesExcludingBeard()
    {
        var results = StyleRecommendationMapper.Rank(MakeInputs());

        Assert.Equal(3, results.Count);
        Assert.DoesNotContain(results, r => r.StyleId == "short-boxed-beard");
    }

    [Fact]
    public void Rank_MaleHighBeardDensity_IncludesBeardEntry()
    {
        var results = StyleRecommendationMapper.Rank(MakeInputs(beard: 0.8, gender: "male"));

        Assert.Contains(results, r => r.StyleId == "short-boxed-beard");
    }

    [Fact]
    public void Rank_MaleHighBeardButAllowBeardFalse_ExcludesBeardEntry()
    {
        var results = StyleRecommendationMapper.Rank(MakeInputs(beard: 0.9, gender: "male", allowBeard: false));

        Assert.DoesNotContain(results, r => r.StyleId == "short-boxed-beard");
    }

    [Fact]
    public void Rank_MaleLowBeardDensity_ExcludesBeardEntry()
    {
        var results = StyleRecommendationMapper.Rank(MakeInputs(beard: 0.10, gender: "male"));

        Assert.DoesNotContain(results, r => r.StyleId == "short-boxed-beard");
    }

    [Fact]
    public void Rank_IsSortedByScoreDescending()
    {
        var results = StyleRecommendationMapper.Rank(MakeInputs());

        var scores = results.Select(r => r.Score).ToList();
        Assert.Equal(scores.OrderByDescending(s => s), scores);
    }

    [Fact]
    public void Rank_HighJawFavorsTexturedCrop()
    {
        var highJaw = StyleRecommendationMapper.Rank(MakeInputs(jaw: 0.95));
        var lowJaw = StyleRecommendationMapper.Rank(MakeInputs(jaw: 0.05));

        var cropHighJaw = highJaw.Single(r => r.StyleId == "textured-crop").Score;
        var cropLowJaw = lowJaw.Single(r => r.StyleId == "textured-crop").Score;

        Assert.True(cropHighJaw > cropLowJaw,
            $"Expected higher jaw to increase Textured Crop score but got {cropHighJaw} vs {cropLowJaw}");
    }

    [Fact]
    public void Rank_ScoresAreDeterministic()
    {
        var inputs = MakeInputs(jaw: 0.7, forehead: 0.4, elongation: 0.6, hair: 0.55, confidence: 0.85);

        var first = StyleRecommendationMapper.Rank(inputs);
        var second = StyleRecommendationMapper.Rank(inputs);

        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].StyleId, second[i].StyleId);
            Assert.Equal(first[i].Score, second[i].Score);
        }
    }

    [Fact]
    public void Rank_ScoresAreBoundedBetweenZeroAndOne()
    {
        var extreme = StyleRecommendationMapper.Rank(MakeInputs(jaw: 1.0, forehead: 1.0, elongation: 1.0, hair: 1.0, confidence: 1.0));

        foreach (var r in extreme)
        {
            Assert.InRange(r.Score, 0.0, 1.0);
        }
    }
}
