namespace AiStyleApp.Data.Queue;

public static class StyleJobPipelineMode
{
    public const string HairOnly = "HairOnly";
    public const string BeardOnly = "BeardOnly";
    public const string HairThenBeard = "HairThenBeard";
}

public static class StyleJobStage
{
    public const string Queued = "Queued";
    public const string Hair = "Hair";
    public const string Beard = "Beard";
}

public static class StyleJobRouting
{
    public static string DeterminePipelineMode(
        string? haircut,
        string? hairColor,
        string? beardStyle,
        string? beardColor,
        string? gender)
    {
        var hasBeardChange = HasMeaningfulSelection(beardStyle) || HasMeaningfulSelection(beardColor);
        if (!AllowsBeard(gender) || !hasBeardChange)
        {
            return StyleJobPipelineMode.HairOnly;
        }

        var hasHairChange = HasMeaningfulSelection(haircut) || HasMeaningfulSelection(hairColor);
        return hasHairChange
            ? StyleJobPipelineMode.HairThenBeard
            : StyleJobPipelineMode.BeardOnly;
    }

    public static bool AllowsBeard(string? gender)
        => string.Equals(gender, "male", StringComparison.OrdinalIgnoreCase);

    public static bool HasMeaningfulSelection(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && !string.Equals(value.Trim(), "No change", StringComparison.OrdinalIgnoreCase);
}
