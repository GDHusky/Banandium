using System.Numerics;

namespace Banandium;

public sealed class ActorCollision
{
    private readonly Dump dump;
    private readonly Dictionary<string, MeshData> actors = new Dictionary<string, MeshData>();
    private readonly Dictionary<string, MeshData> shapes = new Dictionary<string, MeshData>();
    public MeshData Mesh { get; } = new MeshData();
    public int LoadedActors { get; private set; }
    public int UnsupportedShapes { get; private set; }

    public ActorCollision(Dump dump)
    {
        this.dump = dump;
    }

    public void Load(Dictionary<string, object?> scenario)
    {
        foreach (string list in new[] { "ObjectList", "PlacementList" })
        {
            foreach (var entry in PhysicsParameters.Entries(scenario, list))
            {
                string actor = entry.GetValueOrDefault("ObjectName") as string ?? "";
                if (!actors.TryGetValue(actor, out MeshData? mesh))
                {
                    mesh = ReadActor(actor);
                    actors.Add(actor, mesh);
                }
                if (mesh.Triangles == 0) continue;
                Matrix4x4 transform = Dump.ReadTransform(entry);
                if (transform.GetDeterminant() == 0) continue;
                int first = Mesh.Vertices.Count;
                Append(Mesh, mesh, transform);
                Mesh.Parts.Add(new MeshPart(Mesh, first, Mesh.Vertices.Count - first, new SceneObject(actor)));
                LoadedActors++;
            }
        }
    }

    private MeshData ReadActor(string name)
    {
        var mesh = new MeshData();
        string? path = dump.Resolve($"Pack/Actor/{name}.pack.zs");
        if (path == null) return mesh;
        var parameters = new PhysicsParameters(dump, Bytes.Sarc(Bytes.Read(path)));
        var actor = parameters.Read($"Actor/{name}.al__Actor__ActorParam.bgyml");
        if (actor.GetValueOrDefault("Components") is not Dictionary<string, object?> components ||
            components.GetValueOrDefault("PhysicsParam") is not string reference) return mesh;
        var physics = parameters.Read(reference);
        if (physics.GetValueOrDefault("IsUseCollider") is false) return mesh;
        if (physics.GetValueOrDefault("ControllerSetPath") is not string controllerPath) return mesh;
        var controller = parameters.Read(controllerPath);
        var units = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var entry in PhysicsParameters.Entries(controller, "ControllerEntityNamePathAry"))
        {
            var settings = parameters.Read((string)entry["FilePath"]!);
            foreach (var unit in PhysicsParameters.Entries(settings, "RigidBodyControllerUnitAry"))
                units[(string)unit["Name"]!] = unit;
        }
        var shapePaths = new Dictionary<string, string>();
        foreach (var entry in PhysicsParameters.Entries(controller, "ShapeNamePathAry"))
            shapePaths[(string)entry["Name"]!] = (string)entry["FilePath"]!;

        foreach (var entry in PhysicsParameters.Entries(controller, "RigidBodyEntityNamePathAry"))
        {
            var body = parameters.Read((string)entry["FilePath"]!);
            string shapeName = body.GetValueOrDefault("ShapeName") as string ?? "";
            if (units.TryGetValue((string)entry["Name"]!, out var unit))
            {
                if (unit.GetValueOrDefault("UseDummyShape") is true) continue;
                if (unit.GetValueOrDefault("TrackingBoneName") is string bone && bone != "" ||
                    unit.GetValueOrDefault("IsTrackingEntity") is true)
                {
                    UnsupportedShapes++;
                    continue;
                }
                if (unit.GetValueOrDefault("ShapeName") is string replacement && replacement != "") shapeName = replacement;
            }
            if (shapePaths.TryGetValue(shapeName, out string? shapePath))
                ReadShapes(parameters.Read(shapePath), mesh);
        }
        return mesh;
    }

    private void ReadShapes(Dictionary<string, object?> settings, MeshData mesh)
    {
        if (settings.GetValueOrDefault("TrackingBoneName") is string bone && bone != "")
        {
            UnsupportedShapes++;
            return;
        }
        foreach (var pair in settings)
        {
            if (pair.Value is not List<object?> list) continue;
            foreach (object? value in list)
            {
                var shape = (Dictionary<string, object?>)value!;
                switch (pair.Key)
                {
                    case "PhshMesh":
                        string reference = (string)shape["PhshMeshPath"]!;
                        string path = reference.Replace("Work/", "").Replace(".phsh", ".Nin_Ounce_NVN2.bphsh.zs");
                        if (!shapes.TryGetValue(path, out MeshData? source))
                        {
                            source = new MeshData();
                            string? file = dump.Resolve(path);
                            if (file != null)
                            {
                                var reader = new Phive(Bytes.Read(file), source);
                                reader.ReadShapeFile();
                                UnsupportedShapes += reader.UnsupportedShapes;
                            }
                            else UnsupportedShapes++;
                            shapes.Add(path, source);
                        }
                        Append(mesh, source, Matrix4x4.Identity);
                        break;
                    case "Box":
                        mesh.Box(Matrix4x4.CreateScale(Vector(shape, "HalfExtents") * 2) * Matrix4x4.CreateTranslation(Vector(shape, "Center")));
                        break;
                    case "Sphere":
                        mesh.Sphere(Matrix4x4.CreateScale(Convert.ToSingle(shape["Radius"]) * 2) * Matrix4x4.CreateTranslation(Vector(shape, "Center")));
                        break;
                    default:
                        UnsupportedShapes++;
                        break;
                }
            }
        }
    }

    private static Vector3 Vector(Dictionary<string, object?> entry, string key)
    {
        if (entry.GetValueOrDefault(key) is not Dictionary<string, object?> value) return Vector3.Zero;
        return new Vector3(Convert.ToSingle(value["X"]), Convert.ToSingle(value["Y"]), Convert.ToSingle(value["Z"]));
    }

    private static void Append(MeshData target, MeshData source, Matrix4x4 transform)
    {
        for (int i = 0; i < source.Vertices.Count; i += 3)
            target.Triangle(source.Vertices[i], source.Vertices[i + 1], source.Vertices[i + 2], transform);
    }
}
