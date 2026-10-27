using System.Numerics;

namespace Banandium;

public static class AreaShapes
{
    public static Matrix4x4 GetMeshTransform(string model, Matrix4x4 placement)
    {
        //base-origin cylinder reference from odysseydecomp
        //https://github.com/MonsterDruide1/OdysseyDecomp
        if (model == "AreaCylinder")
        {
            return Matrix4x4.CreateScale(10, 5, 10) *
                Matrix4x4.CreateTranslation(0, 2.5f, 0) * placement;
        }
        if (model == "AreaCubeCenter" || model == "AreaSphere")
        {
            return Matrix4x4.CreateScale(10) * placement;
        }
        throw new InvalidDataException("Unsupported area shape: " + model);
    }
}

public sealed class MeshData
{
    public List<Vector3> Vertices { get; } = new List<Vector3>();

    public List<MeshPart> Parts { get; } = new List<MeshPart>();

    public List<Vector3> VisibleVertices()
    {
        if (Parts.Count == 0) return Vertices;
        var visible = new List<Vector3>();
        foreach (MeshPart part in Parts)
        {
            if (!part.Object.Visible) continue;
            for (int i = part.First; i < part.First + part.Count; i++)
                visible.Add(Vertices[i]);
        }
        return visible;
    }

    public int Triangles
    {
        get { return Vertices.Count / 3; }
    }

    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Matrix4x4 transform)
    {
        a = Vector3.Transform(a, transform);
        b = Vector3.Transform(b, transform);
        c = Vector3.Transform(c, transform);
        if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c))
        {
            throw new InvalidDataException("Non-finite geometry");
        }
        if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-14f)
        {
            return;
        }
        Vertices.Add(a);
        Vertices.Add(b);
        Vertices.Add(c);
    }

    private static bool IsFinite(Vector3 vertex)
    {
        return float.IsFinite(vertex.X) && float.IsFinite(vertex.Y) && float.IsFinite(vertex.Z);
    }

    public void Sphere(Matrix4x4 transform)
    {
        const int rings = 16;
        const int sides = 32;
        for (int ring = 0; ring < rings; ring++)
        {
            float bottom = -MathF.PI / 2 + ring * MathF.PI / rings;
            float top = -MathF.PI / 2 + (ring + 1) * MathF.PI / rings;
            for (int side = 0; side < sides; side++)
            {
                float left = side * MathF.Tau / sides;
                float right = (side + 1) * MathF.Tau / sides;
                Vector3 a = SpherePoint(bottom, left);
                Vector3 b = SpherePoint(bottom, right);
                Vector3 c = SpherePoint(top, left);
                Vector3 d = SpherePoint(top, right);
                Triangle(a, c, b, transform);
                Triangle(b, c, d, transform);
            }
        }
    }

    private static Vector3 SpherePoint(float latitude, float longitude)
    {
        return new Vector3(MathF.Cos(latitude) * MathF.Cos(longitude), MathF.Sin(latitude),
            MathF.Cos(latitude) * MathF.Sin(longitude)) * 0.5f;
    }

    public void Cylinder(Matrix4x4 transform)
    {
        const int sides = 64;
        Vector3 bottomCenter = new Vector3(0, -0.5f, 0);
        Vector3 topCenter = new Vector3(0, 0.5f, 0);
        for (int i = 0; i < sides; i++)
        {
            float firstAngle = i * MathF.Tau / sides;
            float secondAngle = (i + 1) * MathF.Tau / sides;
            Vector3 bottomLeft = new Vector3(0.5f * MathF.Cos(firstAngle), -0.5f, 0.5f * MathF.Sin(firstAngle));
            Vector3 bottomRight = new Vector3(0.5f * MathF.Cos(secondAngle), -0.5f, 0.5f * MathF.Sin(secondAngle));
            Vector3 topLeft = bottomLeft + Vector3.UnitY;
            Vector3 topRight = bottomRight + Vector3.UnitY;

            Triangle(bottomLeft, bottomRight, topLeft, transform);
            Triangle(bottomRight, topRight, topLeft, transform);
            Triangle(bottomCenter, bottomRight, bottomLeft, transform);
            Triangle(topCenter, topLeft, topRight, transform);
        }
    }

    public void Box(Matrix4x4 transform)
    {
        Vector3[] corners =
        {
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3( 0.5f, -0.5f, -0.5f),
            new Vector3( 0.5f,  0.5f, -0.5f),
            new Vector3(-0.5f,  0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f,  0.5f),
            new Vector3( 0.5f, -0.5f,  0.5f),
            new Vector3( 0.5f,  0.5f,  0.5f),
            new Vector3(-0.5f,  0.5f,  0.5f)
        };
        //two triangles for each of the six faces
        int[] indices =
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            3, 7, 6, 3, 6, 2,
            0, 4, 7, 0, 7, 3,
            1, 2, 6, 1, 6, 5
        };
        for (int i = 0; i < indices.Length; i += 3)
        {
            Triangle(corners[indices[i]], corners[indices[i + 1]], corners[indices[i + 2]], transform);
        }
    }
}

public sealed class SceneData
{
    public string Name { get; }
    public MeshData Collision { get; }
    public MeshData Death { get; }
    public int DeathAreas { get; }
    public MeshData IgnoreAreas { get; set; } = new MeshData();
    public int IgnoreAreaCount { get; set; }
    public List<SceneLayer> AreaLayers { get; set; } = new List<SceneLayer>();
    public SceneLayer ActorCollision { get; } = new SceneLayer("Actor collision", new Vector3(1, .65f, .15f)) { Mode = DisplayMode.SemiTransparent };
    public SceneConditions Conditions { get; set; } = new SceneConditions();
    public List<SceneArea> Areas { get; set; } = new List<SceneArea>();
    public SortedDictionary<string, List<SceneTrigger>> Triggers { get; set; } = new SortedDictionary<string, List<SceneTrigger>>(StringComparer.Ordinal);
    public int CollisionActors { get; set; }
    public int UnsupportedActorShapes { get; set; }

    public SceneData(string name, MeshData collision, MeshData death, int deathAreas)
    {
        Name = name;
        Collision = collision;
        Death = death;
        DeathAreas = deathAreas;
    }
}
