namespace Banandium;

public sealed class PhysicsParameters
{
    private readonly Dump dump;
    private readonly Dictionary<string, byte[]> archive;

    public PhysicsParameters(Dump dump, Dictionary<string, byte[]> archive)
    {
        this.dump = dump;
        this.archive = archive;
    }

    public Dictionary<string, object?> Read(string reference)
    {
        string path = reference.Replace("Work/", "").Replace(".gyml", ".bgyml");
        if (!archive.TryGetValue(path, out byte[]? data))
        {
            string? file = dump.Resolve(path);
            if (file == null) return new Dictionary<string, object?>();
            data = Bytes.Read(file);
        }
        var entry = (Dictionary<string, object?>)new Byml(data).Read()!;
        if (entry.GetValueOrDefault("$parent") is not string parent) return entry;
        var result = Read(parent);
        Merge(result, entry);
        return result;
    }

    private static void Merge(Dictionary<string, object?> target, Dictionary<string, object?> source)
    {
        foreach (var pair in source)
        {
            if (pair.Value is Dictionary<string, object?> child &&
                target.GetValueOrDefault(pair.Key) is Dictionary<string, object?> inherited)
                Merge(inherited, child);
            else if (pair.Key.EndsWith("NamePathAry") && pair.Value is List<object?> entries &&
                target.GetValueOrDefault(pair.Key) is List<object?> original)
            {
                foreach (object? value in entries)
                {
                    var entry = (Dictionary<string, object?>)value!;
                    int index = -1;
                    for (int i = 0; i < original.Count; i++)
                        if (((Dictionary<string, object?>)original[i]!).GetValueOrDefault("Name") as string == entry.GetValueOrDefault("Name") as string)
                            index = i;
                    if (index < 0) original.Add(entry);
                    else original[index] = entry;
                }
            }
            else target[pair.Key] = pair.Value;
        }
    }

    public static List<Dictionary<string, object?>> Entries(Dictionary<string, object?> source, string key)
    {
        var result = new List<Dictionary<string, object?>>();
        if (source.GetValueOrDefault(key) is List<object?> list)
            foreach (object? value in list) result.Add((Dictionary<string, object?>)value!);
        return result;
    }
}
