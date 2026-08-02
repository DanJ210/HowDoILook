using System.Reflection;
using AiStyleApp.Worker.Handlers;

namespace AiStyleApp.Tests;

public class ReplicateHairSchemaAlignmentTests
{
    // Snapshot source: https://replicate.com/flux-kontext-apps/change-haircut/api/schema (captured 2026-07-22)
    private static readonly HashSet<string> ExpectedHaircuts = new(StringComparer.Ordinal)
    {
        "No change", "Random", "Straight", "Wavy", "Curly", "Bob", "Pixie Cut", "Layered", "Messy Bun", "High Ponytail",
        "Low Ponytail", "Braided Ponytail", "French Braid", "Dutch Braid", "Fishtail Braid", "Space Buns", "Top Knot",
        "Undercut", "Mohawk", "Crew Cut", "Faux Hawk", "Slicked Back", "Side-Parted", "Center-Parted", "Blunt Bangs",
        "Side-Swept Bangs", "Shag", "Lob", "Angled Bob", "A-Line Bob", "Asymmetrical Bob", "Graduated Bob", "Inverted Bob",
        "Layered Shag", "Choppy Layers", "Razor Cut", "Perm", "Ombré", "Straightened", "Soft Waves", "Glamorous Waves",
        "Hollywood Waves", "Finger Waves", "Tousled", "Feathered", "Pageboy", "Pigtails", "Pin Curls", "Rollerset",
        "Twist Out", "Bantu Knots", "Dreadlocks", "Cornrows", "Box Braids", "Crochet Braids", "Double Dutch Braids",
        "French Fishtail Braid", "Waterfall Braid", "Rope Braid", "Heart Braid", "Halo Braid", "Crown Braid", "Braided Crown",
        "Bubble Braid", "Bubble Ponytail", "Ballerina Braids", "Milkmaid Braids", "Bohemian Braids", "Flat Twist",
        "Crown Twist", "Twisted Bun", "Twisted Half-Updo", "Twist and Pin Updo", "Chignon", "Simple Chignon", "Messy Chignon",
        "French Twist", "French Twist Updo", "French Roll", "Updo", "Messy Updo", "Knotted Updo", "Ballerina Bun",
        "Banana Clip Updo", "Beehive", "Bouffant", "Hair Bow", "Half-Up Top Knot", "Half-Up, Half-Down",
        "Messy Bun with a Headband", "Messy Bun with a Scarf", "Messy Fishtail Braid", "Sideswept Pixie", "Mohawk Fade",
        "Zig-Zag Part", "Victory Rolls"
    };

    private static readonly HashSet<string> ExpectedHairColors = new(StringComparer.Ordinal)
    {
        "No change", "Random", "Blonde", "Brunette", "Black", "Dark Brown", "Medium Brown", "Light Brown", "Auburn", "Copper",
        "Red", "Strawberry Blonde", "Platinum Blonde", "Silver", "White", "Blue", "Purple", "Pink", "Green", "Blue-Black",
        "Golden Blonde", "Honey Blonde", "Caramel", "Chestnut", "Mahogany", "Burgundy", "Jet Black", "Ash Brown", "Ash Blonde",
        "Titanium", "Rose Gold"
    };

    [Fact]
    public void AllowedHaircuts_MatchReplicateSchemaSnapshot()
    {
        var actual = GetPrivateStaticHashSet("AllowedHaircuts");
        AssertSetEquals(ExpectedHaircuts, actual, "haircut");
    }

    [Fact]
    public void AllowedHairColors_MatchReplicateSchemaSnapshot()
    {
        var actual = GetPrivateStaticHashSet("AllowedHairColors");
        AssertSetEquals(ExpectedHairColors, actual, "hair_color");
    }

    private static HashSet<string> GetPrivateStaticHashSet(string fieldName)
    {
        var field = typeof(StyleJobHandler).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var value = field!.GetValue(null);
        Assert.IsType<HashSet<string>>(value);

        return (HashSet<string>)value!;
    }

    private static void AssertSetEquals(HashSet<string> expected, HashSet<string> actual, string schemaField)
    {
        var missing = expected.Where(x => !actual.Contains(x)).ToArray();
        var extra = actual.Where(x => !expected.Contains(x)).ToArray();

        var message =
            $"Replicate schema mismatch for '{schemaField}'. Missing: [{string.Join(", ", missing)}]. Extra: [{string.Join(", ", extra)}].";

        Assert.True(missing.Length == 0 && extra.Length == 0, message);
    }
}
