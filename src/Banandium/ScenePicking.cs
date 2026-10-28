using System.Numerics;

namespace Banandium;

public sealed class SceneObject
{
    public string Name { get; }
    public bool Hidden;
    public bool ForceVisible;
    public SceneArea? Area;
    public bool IsAreaPlacement;
    public SceneTrigger? Trigger;
    public Dictionary<string, object?> Fields { get; }
    public List<SceneLink> Links { get; } = new List<SceneLink>();
    public List<SceneLink> Incoming { get; } = new List<SceneLink>();
    public string Key { get { return SceneConditions.Key(Fields); } }
    public bool IsSwitch { get { return Name == "StageSwitch" || Name == "StageSwitchSave"; } }
    public bool HasConditionFields { get; }
    public bool HasConditions;
    public bool IsConditional { get { return HasConditions && ControllingSwitch != null; } }
    public SceneObject? ControllingSwitch
    {
        get
        {
            if (IsSwitch) return Key != "" ? this : null;
            SceneObject? result = null;
            foreach (SceneLink link in Links)
            {
                if (!SceneConditions.IsConditionLink(link)) continue;
                if (result != null && result != link.Target) return null;
                result = link.Target;
            }
            return result != null && result.Key != "" ? result : null;
        }
    }
    public ConditionState Condition;
    public bool Filtered;
    public bool Inactive;
    public bool Visible { get { return !Hidden && !Filtered && !Inactive; } }
    public Vector3 Minimum = new Vector3(float.PositiveInfinity);
    public Vector3 Maximum = new Vector3(float.NegativeInfinity);
    public bool HasBounds { get { return float.IsFinite(Minimum.X); } }
    public bool HasCollision;
    public bool HasModel;
    public bool HasPosition { get { return Fields.GetValueOrDefault("Translate") is Dictionary<string, object?>; } }
    public Vector3 Position { get { return Dump.ReadTransform(Fields).Translation; } }

    public void Include(Vector3 point)
    {
        Minimum = Vector3.Min(Minimum, point);
        Maximum = Vector3.Max(Maximum, point);
    }

    public SceneObject(string name, Dictionary<string, object?>? fields = null)
    {
        Name = name;
        Fields = fields ?? new Dictionary<string, object?>();
        foreach (var field in Fields)
        {
            if (field.Key.Contains("Condition", StringComparison.Ordinal) && field.Value != null && Convert.ToString(field.Value) != "")
                HasConditionFields = true;
        }
        HasConditions = HasConditionFields;
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
            sceneObject.Include(mesh.Vertices[i]);
        }
    }

    public float HitDistance(Vector3 origin, Vector3 direction)
    {
        if (!Object.Visible || Count == 0) return float.PositiveInfinity;
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
