using System.Numerics;

namespace Banandium;

//clips surfaces against a convex box or cylinder
public sealed class AreaCutout
{
    private readonly string model;
    private readonly Matrix4x4 transform;
    private readonly Matrix4x4 inverse;
    private readonly List<Plane> planes = new List<Plane>();

    public AreaCutout(string model, Matrix4x4 placement)
    {
        this.model = model;
        transform = AreaShapes.GetMeshTransform(model, placement);
        if (!Matrix4x4.Invert(transform, out inverse))
        {
            throw new InvalidDataException("Ignore area has a zero scale.");
        }

        planes.Add(new Plane(Vector3.UnitY, -0.5f));
        planes.Add(new Plane(-Vector3.UnitY, -0.5f));
        if (model == "AreaCubeCenter")
        {
            planes.Add(new Plane(Vector3.UnitX, -0.5f));
            planes.Add(new Plane(-Vector3.UnitX, -0.5f));
            planes.Add(new Plane(Vector3.UnitZ, -0.5f));
            planes.Add(new Plane(-Vector3.UnitZ, -0.5f));
        }
        else if (model == "AreaCylinder")
        {
            const int sides = 64;
            float distance = 0.5f * MathF.Cos(MathF.PI / sides);
            for (int side = 0; side < sides; side++)
            {
                float angle = (side + 0.5f) * MathF.Tau / sides;
                Vector3 normal = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
                planes.Add(new Plane(normal, -distance));
            }
        }
        else if (model == "AreaSphere")
        {
            planes.Clear();
            var sphere = new MeshData();
            sphere.Sphere(Matrix4x4.Identity);
            for (int i = 0; i < sphere.Vertices.Count; i += 3)
            {
                Vector3 a = sphere.Vertices[i];
                Vector3 b = sphere.Vertices[i + 1];
                Vector3 c = sphere.Vertices[i + 2];
                Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                if (Vector3.Dot(normal, a) < 0) normal = -normal;
                planes.Add(new Plane(normal, -Vector3.Dot(normal, a)));
            }
        }
        else
        {
            throw new InvalidDataException("Unsupported ignore shape: " + model);
        }
    }

    public void Apply(MeshData mesh, bool keepSharedBoundary = false)
    {
        MeshData result = new MeshData();
        for (int triangle = 0; triangle < mesh.Vertices.Count; triangle += 3)
        {
            List<Vector3> inside = new List<Vector3>();
            for (int corner = 0; corner < 3; corner++)
            {
                inside.Add(Vector3.Transform(mesh.Vertices[triangle + corner], inverse));
            }

            bool separate = false;
            foreach (Plane plane in planes)
            {
                Vector3 faceNormal = Vector3.Cross(inside[1] - inside[0], inside[2] - inside[0]);
                if (keepSharedBoundary && IsOnPlane(inside, plane) &&
                    Vector3.Dot(faceNormal, plane.Normal) * inverse.GetDeterminant() > 0)
                {
                    separate = true;
                    break;
                }
                if (DistanceToPlane(plane, inside[0]) > 0 &&
                    DistanceToPlane(plane, inside[1]) > 0 &&
                    DistanceToPlane(plane, inside[2]) > 0)
                {
                    separate = true;
                    break;
                }
            }
            if (separate)
            {
                result.Vertices.Add(mesh.Vertices[triangle]);
                result.Vertices.Add(mesh.Vertices[triangle + 1]);
                result.Vertices.Add(mesh.Vertices[triangle + 2]);
                continue;
            }

            //only the remaining inside piece needs testing against later planes
            foreach (Plane plane in planes)
            {
                if (inside.Count < 3)
                {
                    break;
                }

                List<Vector3> outside = new List<Vector3>();
                List<Vector3> nextInside = new List<Vector3>();
                Split(inside, plane, nextInside, outside);
                for (int corner = 1; corner + 1 < outside.Count; corner++)
                {
                    result.Triangle(outside[0], outside[corner], outside[corner + 1], transform);
                }
                inside = nextInside;
            }
        }

        mesh.Vertices.Clear();
        mesh.Vertices.AddRange(result.Vertices);
    }

    public MeshData CreateSurface()
    {
        MeshData mesh = new MeshData();
        if (model == "AreaCylinder")
        {
            mesh.Cylinder(transform);
        }
        else if (model == "AreaSphere")
        {
            mesh.Sphere(transform);
        }
        else
        {
            mesh.Box(transform);
        }
        Vector3 center = Vector3.Transform(Vector3.Zero, transform);
        for (int triangle = 0; triangle < mesh.Vertices.Count; triangle += 3)
        {
            Vector3 a = mesh.Vertices[triangle];
            Vector3 b = mesh.Vertices[triangle + 1];
            Vector3 c = mesh.Vertices[triangle + 2];
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3 - center) < 0)
            {
                mesh.Vertices[triangle + 1] = c;
                mesh.Vertices[triangle + 2] = b;
            }
        }
        return mesh;
    }

    private static bool IsOnPlane(List<Vector3> polygon, Plane plane)
    {
        foreach (Vector3 vertex in polygon)
        {
            if (MathF.Abs(Plane.DotCoordinate(plane, vertex)) > 0.00001f)
            {
                return false;
            }
        }
        return true;
    }

    private void KeepInside(MeshData mesh)
    {
        MeshData result = new MeshData();
        for (int triangle = 0; triangle < mesh.Vertices.Count; triangle += 3)
        {
            List<Vector3> polygon = new List<Vector3>();
            for (int corner = 0; corner < 3; corner++)
            {
                polygon.Add(Vector3.Transform(mesh.Vertices[triangle + corner], inverse));
            }
            foreach (Plane plane in planes)
            {
                if (polygon.Count < 3)
                {
                    break;
                }
                if (IsOnPlane(polygon, plane))
                {
                    polygon.Clear();
                    break;
                }
                List<Vector3> inside = new List<Vector3>();
                List<Vector3> outside = new List<Vector3>();
                Split(polygon, plane, inside, outside);
                polygon = inside;
            }
            for (int corner = 1; corner + 1 < polygon.Count; corner++)
            {
                result.Triangle(polygon[0], polygon[corner], polygon[corner + 1], transform);
            }
        }
        mesh.Vertices.Clear();
        mesh.Vertices.AddRange(result.Vertices);
    }

    public MeshData Subtract(List<AreaCutout> cutouts)
    {
        MeshData result = CreateSurface();
        foreach (AreaCutout cutout in cutouts)
        {
            cutout.Apply(result);
        }

        for (int index = 0; index < cutouts.Count; index++)
        {
            AreaCutout cutout = cutouts[index];
            MeshData walls = cutout.CreateSurface();
            KeepInside(walls);
            for (int other = 0; other < cutouts.Count; other++)
            {
                if (other != index)
                {
                    //keep shared faces only on the first cutter that owns them
                    cutouts[other].Apply(walls, other > index);
                }
            }

            //the cavity faces point into the empty space, towards the cutter's center
            Vector3 center = Vector3.Transform(Vector3.Zero, cutout.transform);
            for (int triangle = 0; triangle < walls.Vertices.Count; triangle += 3)
            {
                Vector3 a = walls.Vertices[triangle];
                Vector3 b = walls.Vertices[triangle + 1];
                Vector3 c = walls.Vertices[triangle + 2];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(normal, (a + b + c) / 3 - center) > 0)
                {
                    result.Triangle(a, c, b, Matrix4x4.Identity);
                }
                else
                {
                    result.Triangle(a, b, c, Matrix4x4.Identity);
                }
            }
        }
        return result;
    }

    private static float DistanceToPlane(Plane plane, Vector3 point)
    {
        float distance = Plane.DotCoordinate(plane, point);
        if (MathF.Abs(distance) < 0.00001f)
        {
            return 0;
        }
        return distance;
    }

    private static void Split(List<Vector3> polygon, Plane plane,
        List<Vector3> inside, List<Vector3> outside)
    {
        Vector3 previous = polygon[polygon.Count - 1];
        float previousDistance = DistanceToPlane(plane, previous);
        foreach (Vector3 current in polygon)
        {
            float distance = DistanceToPlane(plane, current);
            bool previousInside = previousDistance <= 0;
            bool currentInside = distance <= 0;
            if (previousInside != currentInside)
            {
                float fraction = previousDistance / (previousDistance - distance);
                Vector3 crossing = Vector3.Lerp(previous, current, fraction);
                inside.Add(crossing);
                outside.Add(crossing);
            }

            if (currentInside)
            {
                inside.Add(current);
            }
            else
            {
                outside.Add(current);
            }
            previous = current;
            previousDistance = distance;
        }
    }
}
