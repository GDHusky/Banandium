using System.Numerics;
using System.Text.RegularExpressions;

namespace Banandium;
public sealed class Dump
{
    public List<string> Roots { get; } = new List<string>();
    public SortedDictionary<string, string> Scenes { get; } = new SortedDictionary<string, string>(StringComparer.Ordinal);

    public Dump(string folder)
    {
        folder = Path.GetFullPath(folder);
        FindRomfsFolders(folder, 6);
        if (Roots.Count == 0)
        {
            throw new InvalidDataException("No extracted romfs found. Select romfs or a dump folder containing base/romfs.");
        }

        Roots.Sort(CompareLayers);
        FindScenes();
        if (Scenes.Count == 0)
        {
            throw new InvalidDataException("No scene parameter packs found.");
        }
    }

    private void FindRomfsFolders(string directory, int depth)
    {
        string packFolder = Path.Combine(directory, "Pack");
        string physicsFolder = Path.Combine(directory, "Phive");
        if (Directory.Exists(packFolder) && Directory.Exists(physicsFolder))
        {
            Roots.Add(directory);
            return;
        }

        if (depth == 0)
        {
            return;
        }

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
            {
                FindRomfsFolders(child, depth - 1);
            }
        }
    }

    private void FindScenes()
    {
        foreach (string root in Roots)
        {
            string directory = Path.Combine(root, "Pack", "SceneParam");
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string path in Directory.EnumerateFiles(directory, "*.pack.zs"))
            {
                string filename = Path.GetFileName(path);
                string sceneName = filename.Substring(0, filename.Length - ".pack.zs".Length);
                Scenes[sceneName] = path;
            }
        }
    }

    private static (int Priority, long Version, string Path) GetLayerOrder(string path)
    {
        string normalizedPath = path.Replace('\\', '/');
        Match match = Regex.Match(normalizedPath, @"/updates/(\d+)/");
        if (match.Success)
        {
            return (1, long.Parse(match.Groups[1].Value), normalizedPath);
        }

        if (normalizedPath.Contains("/dlc/"))
        {
            return (2, 0, normalizedPath);
        }

        return (0, 0, normalizedPath);
    }

    private static int CompareLayers(string first, string second)
    {
        return GetLayerOrder(first).CompareTo(GetLayerOrder(second));
    }

    public string? Resolve(string relativePath)
    {
        string localPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        //later layers override files from earlier layers
        for (int i = Roots.Count - 1; i >= 0; i--)
        {
            string path = Path.Combine(Roots[i], localPath);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static Vector3 ReadVector(Dictionary<string, object?> entry, string name, Vector3 fallback)
    {
        if (!entry.TryGetValue(name, out object? value))
        {
            return fallback;
        }

        Dictionary<string, object?> vector = (Dictionary<string, object?>)value!;
        return new Vector3(Convert.ToSingle(vector["X"]), Convert.ToSingle(vector["Y"]), Convert.ToSingle(vector["Z"]));
    }

    private static Matrix4x4 ReadTransform(Dictionary<string, object?> entry)
    {
        Vector3 rotation = ReadVector(entry, "Rotate", Vector3.Zero) * (MathF.PI / 180);
        Vector3 scale = ReadVector(entry, "Scale", Vector3.One);
        Vector3 position = ReadVector(entry, "Translate", Vector3.Zero);
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationX(rotation.X) * Matrix4x4.CreateRotationY(rotation.Y) * Matrix4x4.CreateRotationZ(rotation.Z) * Matrix4x4.CreateTranslation(position);
    }

    public SceneData Load(string name)
    {
        if (!Scenes.ContainsKey(name))
        {
            throw new InvalidDataException("Unknown scene");
        }

        Match match = Regex.Match(name, @"^(.*)Scenario(\d+)$");
        if (!match.Success)
        {
            throw new InvalidDataException("Unrecognized scene/scenario name");
        }

        string stageName = match.Groups[1].Value;
        int scenarioIndex = int.Parse(match.Groups[2].Value) - 1;
        MeshData collision = new MeshData();
        MeshData death = new MeshData();
        MeshData ignoredAreas = new MeshData();
        List<SceneArea> areas = new List<SceneArea>();
        int areaCount = 0;
        int ignoredCount = 0;
        string? placementPath = Resolve($"StageData/{stageName}.alsarc.zs");
        if (placementPath != null)
        {
            Dictionary<string, byte[]> archive = Bytes.Sarc(Bytes.Read(placementPath));
            byte[] data = archive[stageName + ".byml"];
            var scenarios = (List<object?>)new Byml(data).Read()!;
            var scenario = (Dictionary<string, object?>)scenarios[scenarioIndex]!;
            if (scenario.TryGetValue("AreaList", out object? list))
            {
                foreach (object? value in (List<object?>)list!)
                {
                    Dictionary<string, object?> entry = (Dictionary<string, object?>)value!;
                    string objectName = entry.GetValueOrDefault("ObjectName") as string ?? "";
                    string family = SceneAreas.GetFamily(objectName);
                    if (family == "")
                    {
                        continue;
                    }

                    string model = entry.GetValueOrDefault("ModelName") as string ?? "";
                    if (model != "AreaCubeCenter" && model != "AreaCylinder" && model != "AreaSphere")
                    {
                        continue;
                    }

                    Matrix4x4 transform = ReadTransform(entry);
                    if (transform.GetDeterminant() == 0)
                    {
                        continue;
                    }
                    var area = new SceneArea(objectName, family, entry, new AreaCutout(model, transform));
                    areas.Add(area);
                    if (family == "DeathArea")
                    {
                        areaCount++;
                        if (area.IsIgnore)
                        {
                            ignoredCount++;
                            ignoredAreas.Vertices.AddRange(area.Shape.CreateSurface().Vertices);
                        }
                    }
                }
            }
        }

        List<SceneLayer> layers = SceneAreas.Build(areas);
        foreach (SceneLayer layer in layers)
        {
            if (SceneAreas.GetFamily(layer.Name) == "DeathArea")
            {
                death.Vertices.AddRange(layer.Mesh.Vertices);
            }
        }

        string? collisionPath = Resolve($"Phive/StaticCompoundBody/{name}.Nin_Ounce_NVN2.bphsc.zs");
        if (collisionPath != null)
        {
            Phive reader = new Phive(Bytes.Read(collisionPath), collision);
            reader.Read();
        }

        return new SceneData(name, collision, death, areaCount)
        {
            AreaLayers = layers,
            IgnoreAreas = ignoredAreas,
            IgnoreAreaCount = ignoredCount
        };
    }
}
