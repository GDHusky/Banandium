using System.Numerics;

namespace Banandium;

public sealed class ViewFrustum
{
    private readonly Vector4[] planes = new Vector4[6];

    public void Update(Matrix4x4 matrix)
    {
        Vector4 x = new Vector4(matrix.M11, matrix.M21, matrix.M31, matrix.M41);
        Vector4 y = new Vector4(matrix.M12, matrix.M22, matrix.M32, matrix.M42);
        Vector4 z = new Vector4(matrix.M13, matrix.M23, matrix.M33, matrix.M43);
        Vector4 w = new Vector4(matrix.M14, matrix.M24, matrix.M34, matrix.M44);
        planes[0] = w + x;
        planes[1] = w - x;
        planes[2] = w + y;
        planes[3] = w - y;
        planes[4] = w + z;
        planes[5] = w - z;
        for (int i = 0; i < planes.Length; i++)
        {
            Vector4 plane = planes[i];
            float length = new Vector3(plane.X, plane.Y, plane.Z).Length();
            if (length > 0) planes[i] /= length;
        }
    }

    public bool Intersects(Vector3 minimum, Vector3 maximum)
    {
        foreach (Vector4 plane in planes)
        {
            Vector3 corner = new Vector3(plane.X >= 0 ? maximum.X : minimum.X,
                plane.Y >= 0 ? maximum.Y : minimum.Y, plane.Z >= 0 ? maximum.Z : minimum.Z);
            if (plane.X * corner.X + plane.Y * corner.Y + plane.Z * corner.Z + plane.W < -.01f)
                return false;
        }
        return true;
    }
}
