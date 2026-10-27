using OpenTK.Graphics.OpenGL4;
using System.Numerics;

namespace Banandium;
public static class Shaders
{
    public static int Create(string vertex, string fragment)
    {
        int Compile(ShaderType type, string source)
        {
            int s = GL.CreateShader(type);
            GL.ShaderSource(s, source);
            GL.CompileShader(s);
            GL.GetShader(s, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0)
            {
                throw new InvalidOperationException(GL.GetShaderInfoLog(s));
            }

            return s;
        }

        int v = Compile(ShaderType.VertexShader, vertex), f = Compile(ShaderType.FragmentShader, fragment), p = GL.CreateProgram();
        GL.AttachShader(p, v);
        GL.AttachShader(p, f);
        GL.LinkProgram(p);
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, out int linked);
        GL.DeleteShader(v);
        GL.DeleteShader(f);
        if (linked == 0)
        {
            throw new InvalidOperationException(GL.GetProgramInfoLog(p));
        }

        return p;
    }

    public static void Matrix(int location, Matrix4x4 m)
    {
        GL.UniformMatrix4(location, 1, false, new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 });
    }
}

public enum DisplayMode
{
    SemiTransparent,
    Opaque,
    Disabled
}

public sealed class SceneRenderer : IDisposable
{
    private sealed class Batch
    {
        public SceneLayer Layer;
        public List<Vector3> Vertices;
        public int First;
        public int Count;
        public DisplayMode LastMode;
        public bool LastShaded;
        public Batch(SceneLayer layer, int first)
        {
            Layer = layer;
            First = first;
            Vertices = layer.Mesh.VisibleVertices();
            Count = Vertices.Count;
            LastMode = layer.Mode;
            LastShaded = layer.Shaded;
        }
    }

    private readonly int program, vao, vbo, ebo;
    private readonly List<Batch> batches = new List<Batch>();
    private Vector3[] centers = [];
    private uint[] order = [];
    private float[] vertices = [];
    private float[] triangleDepths = [];
    private int[] triangleLayers = [];
    private Vector3 lastEye = new Vector3(float.NaN);
    private Vector3 lastForward;
    private int sortedCount;

    public SceneRenderer()
    {
        program = Shaders.Create("""
        #version 330 core
        layout(location=0) in vec3 position;
        layout(location=1) in vec3 normal;
        layout(location=2) in vec3 tint;
        layout(location=3) in float shaded;
        uniform mat4 matrix;
        out vec3 surfaceNormal;
        out vec3 worldPosition;
        flat out vec3 surfaceColor;
        flat out float useShading;
        void main() {
            gl_Position = matrix * vec4(position, 1);
            surfaceNormal = normal;
            worldPosition = position;
            surfaceColor = tint;
            useShading = shaded;
        }
        """, """
        #version 330 core
        in vec3 surfaceNormal;
        in vec3 worldPosition;
        flat in vec3 surfaceColor;
        flat in float useShading;
        uniform float alpha;
        uniform vec3 eye;
        uniform vec3 lightDirection;
        out vec4 color;
        void main() {
            float light = 1.0;
            if (useShading > .5) {
                vec3 normal = normalize(surfaceNormal);
                if (dot(normal, eye - worldPosition) < 0.0) normal = -normal;
                light = .18 + .82 * max(dot(normal, lightDirection), 0.0);
            }
            color = vec4(surfaceColor * light, alpha);
        }
        """);
        vao = GL.GenVertexArray();
        vbo = GL.GenBuffer();
        ebo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        for (int attribute = 0; attribute < 4; attribute++) GL.EnableVertexAttribArray(attribute);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 40, 0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 40, 12);
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, 40, 24);
        GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, 40, 36);
        GL.BindVertexArray(0);
    }

    public void Upload(SceneData scene)
    {
        batches.Clear();
        var collision = new SceneLayer("Collision", new Vector3(.38f, .72f, .83f));
        collision.Mesh.Vertices.AddRange(scene.Collision.VisibleVertices());
        batches.Add(new Batch(collision, 0));
        int count = collision.Mesh.Vertices.Count;
        batches.Add(new Batch(scene.ActorCollision, count));
        count += batches[^1].Count;
        foreach (SceneLayer layer in scene.AreaLayers)
        {
            batches.Add(new Batch(layer, count));
            count += batches[^1].Count;
        }
        foreach (var group in scene.Triggers.Values)
        {
            foreach (SceneTrigger trigger in group)
            {
                if (trigger.Layer == null) continue;
                batches.Add(new Batch(trigger.Layer, count));
                count += batches[^1].Count;
            }
        }
        vertices = new float[count * 10];
        centers = new Vector3[count / 3];
        triangleLayers = new int[count / 3];
        order = new uint[count];
        triangleDepths = new float[count / 3];
        for (int layerIndex = 0; layerIndex < batches.Count; layerIndex++)
        {
            Batch batch = batches[layerIndex];
            var mesh = batch.Vertices;
            for (int i = 0; i < mesh.Count; i += 3)
            {
                Vector3 normal = Vector3.Normalize(Vector3.Cross(mesh[i + 1] - mesh[i], mesh[i + 2] - mesh[i]));
                int triangle = (batch.First + i) / 3;
                centers[triangle] = (mesh[i] + mesh[i + 1] + mesh[i + 2]) / 3;
                triangleLayers[triangle] = layerIndex;
                for (int corner = 0; corner < 3; corner++)
                {
                    int offset = (batch.First + i + corner) * 10;
                    Vector3 point = mesh[i + corner];
                    Vector3 tint = batch.Layer.Color;
                    vertices[offset] = point.X;
                    vertices[offset + 1] = point.Y;
                    vertices[offset + 2] = point.Z;
                    vertices[offset + 3] = normal.X;
                    vertices[offset + 4] = normal.Y;
                    vertices[offset + 5] = normal.Z;
                    vertices[offset + 6] = tint.X;
                    vertices[offset + 7] = tint.Y;
                    vertices[offset + 8] = tint.Z;
                    vertices[offset + 9] = batch.Layer.Shaded ? 1 : 0;
                }
            }
        }
        UploadVertices();
        lastEye = new Vector3(float.NaN);
    }

    private void UploadVertices()
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
    }

    private int CompareDepth(int first, int second)
    {
        return triangleDepths[second].CompareTo(triangleDepths[first]);
    }

    public void Draw(Matrix4x4 matrix, Vector3 eye, Vector3 forward, DisplayMode collision, bool collisionShaded)
    {
        if (batches.Count == 0) return;
        batches[0].Layer.Mode = collision;
        batches[0].Layer.Shaded = collisionShaded;
        bool modesChanged = false;
        bool shadingChanged = false;
        foreach (Batch batch in batches)
        {
            if (batch.Layer.Mode != batch.LastMode) modesChanged = true;
            if (batch.Layer.Shaded != batch.LastShaded)
            {
                shadingChanged = true;
                for (int i = batch.First; i < batch.First + batch.Count; i++)
                    vertices[i * 10 + 9] = batch.Layer.Shaded ? 1 : 0;
            }
            batch.LastMode = batch.Layer.Mode;
            batch.LastShaded = batch.Layer.Shaded;
        }
        if (shadingChanged) UploadVertices();
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Lequal);
        GL.DepthMask(true);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.Blend);
        GL.UseProgram(program);
        Shaders.Matrix(GL.GetUniformLocation(program, "matrix"), matrix);
        GL.Uniform3(GL.GetUniformLocation(program, "eye"), eye.X, eye.Y, eye.Z);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));
        Vector3 light = Vector3.Normalize(-forward + up * .8f - right * .6f);
        GL.Uniform3(GL.GetUniformLocation(program, "lightDirection"), light.X, light.Y, light.Z);
        GL.BindVertexArray(vao);
        GL.Uniform1(GL.GetUniformLocation(program, "alpha"), 1f);
        foreach (Batch batch in batches)
        {
            if (batch.Layer.Mode == DisplayMode.Opaque)
                GL.DrawArrays(PrimitiveType.Triangles, batch.First, batch.Count);
        }
        if (modesChanged || eye != lastEye || forward != lastForward)
        {
            List<int> triangles = new List<int>();
            for (int triangle = 0; triangle < centers.Length; triangle++)
            {
                if (batches[triangleLayers[triangle]].Layer.Mode == DisplayMode.SemiTransparent)
                    triangles.Add(triangle);
                triangleDepths[triangle] = Vector3.Dot(centers[triangle] - eye, forward);
            }
            triangles.Sort(CompareDepth);
            sortedCount = 0;
            foreach (int triangle in triangles)
            {
                order[sortedCount++] = (uint)triangle * 3;
                order[sortedCount++] = (uint)triangle * 3 + 1;
                order[sortedCount++] = (uint)triangle * 3 + 2;
            }
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, sortedCount * sizeof(uint), order, BufferUsageHint.StreamDraw);
            lastEye = eye;
            lastForward = forward;
        }
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.DepthMask(false);
        GL.Uniform1(GL.GetUniformLocation(program, "alpha"), .28f);
        GL.DrawElements(PrimitiveType.Triangles, sortedCount, DrawElementsType.UnsignedInt, 0);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    public void Dispose()
    {
        GL.DeleteBuffer(vbo);
        GL.DeleteBuffer(ebo);
        GL.DeleteVertexArray(vao);
        GL.DeleteProgram(program);
    }
}
