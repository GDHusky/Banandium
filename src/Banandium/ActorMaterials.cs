using System.Numerics;

namespace Banandium;

public sealed class ActorMaterials
{
    private readonly Dump dump;
    private readonly Dictionary<string, Vector4?> colors = new Dictionary<string, Vector4?>();

    public ActorMaterials(Dump dump)
    {
        this.dump = dump;
    }

    public Dictionary<string, Vector4> Read(string actor)
    {
        var result = new Dictionary<string, Vector4>();
        string? path = dump.Resolve($"Pack/Actor/{actor}.pack.zs");
        if (path == null) return result;
        Dictionary<string, byte[]> archive = Bytes.Sarc(Bytes.Read(path));
        string parameter = $"Actor/{actor}.al__Actor__ActorParam.bgyml";
        string fitting = "";
        while (archive.TryGetValue(parameter, out byte[]? data))
        {
            var entry = (Dictionary<string, object?>)new Byml(data).Read()!;
            if (entry.GetValueOrDefault("Components") is Dictionary<string, object?> components &&
                components.GetValueOrDefault("MaterialFittingParam") is string reference)
            {
                fitting = ResourcePath(reference);
                break;
            }
            parameter = ResourcePath(entry.GetValueOrDefault("$parent") as string ?? "");
        }
        ReadFitting(archive, fitting, result);
        return result;
    }

    private void ReadFitting(Dictionary<string, byte[]> archive, string path, Dictionary<string, Vector4> result)
    {
        if (!archive.TryGetValue(path, out byte[]? data)) return;
        var entry = (Dictionary<string, object?>)new Byml(data).Read()!;
        if (entry.GetValueOrDefault("$parent") is string parent)
            ReadFitting(archive, ResourcePath(parent), result);
        if (entry.GetValueOrDefault("TargetMaterialList") is not List<object?> targets) return;
        foreach (object? value in targets)
        {
            var target = (Dictionary<string, object?>)value!;
            if (target.GetValueOrDefault("DisableTextureFitting") is true) continue;
            string name = target.GetValueOrDefault("FittingAllMaterial") is true ? "*" : (string)target["MaterialName"]!;
            string reference = target.GetValueOrDefault("VoxelMaterial") as string ?? "";
            Vector4? color = ReadColor(reference);
            if (color.HasValue) result[name] = color.Value;
        }
    }

    public Vector4? ReadColor(string reference)
    {
        string name = Path.GetFileName(reference).Split('.')[0];
        if (colors.TryGetValue(name, out Vector4? color)) return color;
        string? path = dump.Resolve($"Voxel/VoxelMaterial/{name}.vmat.zs");
        if (path != null)
        {
            Dictionary<string, byte[]> archive = Bytes.Sarc(Bytes.Read(path));
            if (archive.TryGetValue(name + ".al__VoxelMaterial.bgyml", out byte[]? data))
            {
                var material = (Dictionary<string, object?>)new Byml(data).Read()!;
                string shader = Path.GetFileName(ResourcePath(material.GetValueOrDefault("ShaderType") as string ?? ""));
                if (archive.TryGetValue(shader, out byte[]? shaderData))
                {
                    var settings = (Dictionary<string, object?>)new Byml(shaderData).Read()!;
                    if (settings.GetValueOrDefault("AverageTextureValues") is Dictionary<string, object?> average)
                    {
                        //the stored average is linear color and its alpha is not opacity
                        color = new Vector4(Convert.ToSingle(average["R"]), Convert.ToSingle(average["G"]),
                            Convert.ToSingle(average["B"]), 1);
                    }
                }
            }
        }
        colors[name] = color;
        return color;
    }

    private static string ResourcePath(string reference)
    {
        if (reference.StartsWith("Work/")) reference = reference.Substring(5);
        return reference.Replace(".gyml", ".bgyml");
    }
}
