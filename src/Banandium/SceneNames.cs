namespace Banandium;

public static class SceneNames
{
    private static readonly Dictionary<string, string> CommonNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        { "Ground", "Ingot Isle (Intro)" },
        { "Under", "Ingot Isle Cave-In" },
        { "Lake", "Lagoon Layer" },
        { "Hill", "Hilltop Layer" },
        { "Wild", "Canyon Layer" },
        { "Branch", "The Divide" },
        { "Ice", "Freezer Layer" },
        { "Forest", "Forest Layer" },
        { "Fort", "The Junction" },
        { "Resort", "Resort Layer" },
        { "Storm", "Tempest Layer" },
        { "Arena", "Landfill Layer" },
        { "Race", "Racing Layer" },
        { "Desert", "Radiance Layer" },
        { "Sound", "Groove Layer" },
        { "Melt", "Feast Layer" },
        { "KeepOut", "Forbidden Layer" },
        { "Core", "Planet Core" },
        { "City", "NDC (Surface)" },
        { "Island", "DK Isle" }
    };

    public static string GetCommonName(string name)
    {
        foreach (var pair in CommonNames)
        {
            if (!name.StartsWith(pair.Key, StringComparison.Ordinal)) continue;
            if (name.Length == pair.Key.Length) return pair.Value;
            if (!char.IsUpper(name[pair.Key.Length])) continue;
            string suffix = name.Substring(pair.Key.Length);
            if (suffix.StartsWith("LevelStage", StringComparison.Ordinal)) suffix = suffix.Substring(10);
            return suffix.Length == 0 ? pair.Value : pair.Value + " " + suffix;
        }
        return name;
    }
}
