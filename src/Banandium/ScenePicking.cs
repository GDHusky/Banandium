using System.Numerics;

namespace Banandium;

public sealed class SceneObject
{
    public string Name { get; }
    public bool Hidden;
    public SceneArea? Area;

    public SceneObject(string name)
    {
        Name = name;
    }
}

public sealed class MeshPart
{
    public SceneObject Object { get; }
    public int First { get; }
    public int Count { get; }
    private readonly MeshData mesh;
    private readonly Vector3 minimum;
    private readonly Vector3 maximum;

    public MeshPart(MeshData mesh, int first, int count, SceneObject sceneObject)
    {
        this.mesh = mesh;
        First = first;
        Count = count;
        Object = sceneObject;
        minimum = new Vector3(float.PositiveInfinity);
        maximum = new Vector3(float.NegativeInfinity);
        for (int i = first; i < first + count; i++)
        {
            minimum = Vector3.Min(minimum, mesh.Vertices[i]);
            maximum = Vector3.Max(maximum, mesh.Vertices[i]);
        }
    }

    public float HitDistance(Vector3 origin, Vector3 direction)
    {
        if (Object.Hidden || Count == 0) return float.PositiveInfinity;
        float near = 0;
        float far = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            if (direction[axis] == 0)
            {
                if (origin[axis] < minimum[axis] || origin[axis] > maximum[axis])
                    return float.PositiveInfinity;
                continue;
            }
            float a = (minimum[axis] - origin[axis]) / direction[axis];
            float b = (maximum[axis] - origin[axis]) / direction[axis];
            near = MathF.Max(near, MathF.Min(a, b));
            far = MathF.Min(far, MathF.Max(a, b));
            if (near > far) return float.PositiveInfinity;
        }
        return SceneAreas.HitDistance(mesh, origin, direction, First, Count);
    }
}
