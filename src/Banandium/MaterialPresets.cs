namespace Banandium;

public sealed class MaterialPresets
{
    private readonly Dictionary<string, object?> presets = new Dictionary<string, object?>();
    private readonly Dictionary<string, byte[]> presetFiles;
    private readonly Dictionary<string, byte[]> natureFiles;

    public MaterialPresets(Dump dump, string scene)
    {
        presetFiles = ReadArchive(dump.Resolve("Voxel/VoxelMaterialPreset.alsarc.zs"));
        natureFiles = ReadArchive(dump.Resolve("Voxel/VoxelMaterialNatureList.alsarc.zs"));
        Dictionary<string, byte[]> archive = ReadArchive(dump.Scenes[scene]);
        var parameter = ReadEntry(archive, $"Stage/SceneParam/{scene}.al__SceneParam.bgyml");
        if (parameter.GetValueOrDefault("Components") is not Dictionary<string, object?> components) return;
        string reference = components.GetValueOrDefault("VoxelMaterialPreset") as string ?? "";
        var settings = ReadEntry(archive, ResourcePath(reference));
        if (settings.GetValueOrDefault("VoxelMaterialPresetList") is Dictionary<string, object?> list)
            presets = list;
    }

    public string Resolve(Dictionary<string, object?> placement)
    {
        string type = placement.GetValueOrDefault("MaterialType") as string ?? "";
        if (type == "") return "";
        string key = placement.GetValueOrDefault("MaterialPresetKeyName") as string ?? "Default";
        string reference = presets.GetValueOrDefault(key) as string ?? "";
        var preset = ReadEntry(presetFiles, Path.GetFileName(ResourcePath(reference)));
        string nature = preset.GetValueOrDefault(type) as string ?? "";
        var settings = ReadEntry(natureFiles, Path.GetFileName(ResourcePath(nature)));
        if (settings.GetValueOrDefault("MaterialList") is not List<object?> materials) return "";
        int slot = Convert.ToInt32(placement.GetValueOrDefault("MaterialTypeSlotId") ?? 0);
        if (slot < 0 || slot >= materials.Count) return "";
        return materials[slot] as string ?? "";
    }

    private static Dictionary<string, byte[]> ReadArchive(string? path)
    {
        return path == null ? new Dictionary<string, byte[]>() : Bytes.Sarc(Bytes.Read(path));
    }

    private static Dictionary<string, object?> ReadEntry(Dictionary<string, byte[]> archive, string path)
    {
        if (!archive.TryGetValue(path, out byte[]? data)) return new Dictionary<string, object?>();
        var entry = (Dictionary<string, object?>)new Byml(data).Read()!;
        if (entry.GetValueOrDefault("$parent") is not string parent) return entry;
        string parentPath = ResourcePath(parent);
        if (!archive.ContainsKey(parentPath)) parentPath = Path.GetFileName(parentPath);
        var inherited = ReadEntry(archive, parentPath);
        foreach (var pair in entry)
        {
            if (pair.Value is Dictionary<string, object?> child &&
                inherited.GetValueOrDefault(pair.Key) is Dictionary<string, object?> original)
            {
                foreach (var value in child) original[value.Key] = value.Value;
            }
            else inherited[pair.Key] = pair.Value;
        }
        return inherited;
    }

    private static string ResourcePath(string reference)
    {
        if (reference.StartsWith("Work/")) reference = reference.Substring(5);
        return reference.Replace(".gyml", ".bgyml");
    }
}
