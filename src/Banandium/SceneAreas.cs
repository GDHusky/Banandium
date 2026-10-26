using System.Numerics;

namespace Banandium;

public sealed class SceneLayer
{
    public string Name { get; }
    public MeshData Mesh { get; } = new MeshData();
    public Vector3 Color { get; }
    public DisplayMode Mode = DisplayMode.Disabled;
    public bool Shaded = true;
    public List<SceneArea> Areas { get; } = new List<SceneArea>();

    public SceneLayer(string name, Vector3 color)
    {
        Name = name;
        Color = color;
    }
}

public sealed class SceneArea
{
    public string Name { get; }
    public string Family { get; }
    public bool IsIgnore { get; }
    public int Priority { get; }
    public Dictionary<string, object?> Fields { get; }
    public AreaCutout Shape { get; }
    public MeshData Mesh { get; set; } = new MeshData();

    public SceneArea(string name, string family, Dictionary<string, object?> fields, AreaCutout shape)
    {
        Name = name;
        Family = family;
        Fields = fields;
        Shape = shape;
        IsIgnore = fields.GetValueOrDefault("IsIgnoreArea") is true;
        Priority = Convert.ToInt32(fields.GetValueOrDefault("Priority", -1));
    }
}

public static class SceneAreas
{
    public static string GetFamily(string name)
    {
        //world end boundaries use a generic area object in odysseydecomp
        //https://github.com/MonsterDruide1/OdysseyDecomp
        string[] families = { "DeathArea", "ChangeLevelArea", "InvalidateInputFallArea",
            "WorldEndBorderArea", "PlayerRecoveryInvalidateArea", "PlayerRecoverySafetyPointArea", "WaterArea" };
        foreach (string family in families)
        {
            if (name == family || name.StartsWith(family + "_", StringComparison.Ordinal) ||
                name == family + "Cube" || name == family + "Cylinder")
            {
                return family;
            }
        }
        return "";
    }

    private static Vector3 GetColor(string family)
    {
        switch (family)
        {
            case "DeathArea": return new Vector3(1, 0.18f, 0.25f);
            case "ChangeLevelArea": return new Vector3(0.3f, 1, 0.4f);
            case "InvalidateInputFallArea": return new Vector3(1, 0.65f, 0.12f);
            case "WorldEndBorderArea": return new Vector3(0.8f, 0.35f, 1);
            case "PlayerRecoveryInvalidateArea": return new Vector3(1, 0.4f, 0.7f);
            case "PlayerRecoverySafetyPointArea": return new Vector3(0.7f, 1, 0.15f);
            case "WaterArea": return new Vector3(0.15f, 0.4f, 1);
            default: return Vector3.One;
        }
    }

    public static List<SceneLayer> Build(List<SceneArea> areas)
    {
        var layers = new SortedDictionary<string, SceneLayer>(StringComparer.Ordinal);
        for (int index = 0; index < areas.Count; index++)
        {
            SceneArea area = areas[index];
            if (!layers.TryGetValue(area.Name, out SceneLayer? layer))
            {
                layer = new SceneLayer(area.Name, GetColor(area.Family));
                if (area.Family == "DeathArea") layer.Mode = DisplayMode.SemiTransparent;
                layers.Add(area.Name, layer);
            }
            layer.Areas.Add(area);
            if (area.IsIgnore) continue;

            List<AreaCutout> cutouts = new List<AreaCutout>();
            if (area.Family == "DeathArea" || area.Family == "WaterArea")
            {
                for (int other = 0; other < areas.Count; other++)
                {
                    SceneArea candidate = areas[other];
                    if (!candidate.IsIgnore || candidate.Family != area.Family) continue;

                    //higher priority wins, with later placements winning ties
                    if (candidate.Priority > area.Priority ||
                        (candidate.Priority == area.Priority && other > index))
                    {
                        cutouts.Add(candidate.Shape);
                    }
                }
            }
            area.Mesh = area.Shape.Subtract(cutouts);
            layer.Mesh.Vertices.AddRange(area.Mesh.Vertices);
        }
        return new List<SceneLayer>(layers.Values);
    }

    public static float HitDistance(MeshData mesh, Vector3 origin, Vector3 direction)
    {
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < mesh.Vertices.Count; i += 3)
        {
            Vector3 a = mesh.Vertices[i];
            Vector3 edge1 = mesh.Vertices[i + 1] - a;
            Vector3 edge2 = mesh.Vertices[i + 2] - a;
            Vector3 cross = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, cross);
            if (MathF.Abs(determinant) < 0.000001f) continue;
            float inverse = 1 / determinant;
            Vector3 offset = origin - a;
            float u = Vector3.Dot(offset, cross) * inverse;
            if (u < 0 || u > 1) continue;
            Vector3 otherCross = Vector3.Cross(offset, edge1);
            float v = Vector3.Dot(direction, otherCross) * inverse;
            if (v < 0 || u + v > 1) continue;
            float distance = Vector3.Dot(edge2, otherCross) * inverse;
            if (distance >= 0 && distance < nearest) nearest = distance;
        }
        return nearest;
    }
}
