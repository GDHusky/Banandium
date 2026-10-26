using System.Numerics;
using BfresLibrary.GX2;
using OpenTK.Graphics.OpenGL4;

namespace Banandium;

public sealed class ModelRenderer : IDisposable
{
    private sealed record MeshBuffers(int Vao, int Vbo, int Ebo);
    private readonly int program;
    private readonly Dictionary<ModelMesh, MeshBuffers> meshes = new Dictionary<ModelMesh, MeshBuffers>();
    private readonly Dictionary<ModelTexture, int> textures = new Dictionary<ModelTexture, int>();
    private readonly int white;
    private SceneModels? scene;

    public ModelRenderer()
    {
        program = Shaders.Create("""
        #version 330 core
        layout(location=0) in vec3 position;
        layout(location=1) in vec3 normal;
        layout(location=2) in vec2 uv;
        layout(location=3) in vec4 vertexColor;
        uniform mat4 matrix;
        uniform mat4 model;
        out vec2 texCoord;
        out vec3 surfaceNormal;
        out vec4 surfaceTint;
        void main() {
            gl_Position = matrix * model * vec4(position, 1);
            surfaceNormal = transpose(inverse(mat3(model))) * normal;
            texCoord = uv;
            surfaceTint = vertexColor;
        }
        """, """
        #version 330 core
        in vec2 texCoord;
        in vec3 surfaceNormal;
        in vec4 surfaceTint;
        uniform sampler2D albedo;
        uniform sampler2D opacity;
        uniform vec4 tint;
        out vec4 color;
        void main() {
            vec4 sampleColor = texture(albedo, texCoord);
            if (sampleColor.a * tint.a * surfaceTint.a * texture(opacity, texCoord).r < 0.5) discard;
            vec3 normal = normalize(surfaceNormal);
            if (!gl_FrontFacing) normal = -normal;
            float light = 0.55 + 0.45 * max(dot(normal, normalize(vec3(0.4, 1, 0.3))), 0.0);
            color = vec4(pow(max(sampleColor.rgb * tint.rgb * surfaceTint.rgb * light, vec3(0)), vec3(1.0 / 2.2)), 1);
        }
        """);
        white = UploadTexture(new ModelTexture(1, 1, new byte[] {255, 255, 255, 255}));
    }

    public void Upload(SceneModels? models)
    {
        Clear();
        scene = models;
        if (scene == null) return;
        foreach (ModelInstance instance in scene.Instances)
        {
            ModelMesh mesh = instance.Mesh;
            if (meshes.ContainsKey(mesh)) continue;
            int vao = GL.GenVertexArray(), vbo = GL.GenBuffer(), ebo = GL.GenBuffer();
            meshes.Add(mesh, new MeshBuffers(vao, vbo, ebo));
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, mesh.Vertices.Length * sizeof(float), mesh.Vertices, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, mesh.Indices.Length * sizeof(uint), mesh.Indices, BufferUsageHint.StaticDraw);
            for (int i = 0; i < 4; i++) GL.EnableVertexAttribArray(i);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 48, 0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 48, 12);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 48, 24);
            GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, 48, 32);
            foreach (ModelTexture? texture in new[] { mesh.Color, mesh.Opacity })
                if (texture != null && !textures.ContainsKey(texture)) textures.Add(texture, UploadTexture(texture));
        }
        GL.BindVertexArray(0);
    }

    private static int UploadTexture(ModelTexture texture)
    {
        int id = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, id);
        GL.TexImage2D(TextureTarget.Texture2D, 0, texture.Srgb ? PixelInternalFormat.Srgb8Alpha8 : PixelInternalFormat.Rgba8, texture.Width,
            texture.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, texture.Pixels);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        return id;
    }

    private static TextureWrapMode Wrap(GX2TexClamp mode)
    {
        if (mode == GX2TexClamp.Wrap) return TextureWrapMode.Repeat;
        if (mode == GX2TexClamp.Mirror) return TextureWrapMode.MirroredRepeat;
        return TextureWrapMode.ClampToEdge;
    }

    public void Draw(Matrix4x4 matrix)
    {
        if (scene == null) return;
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Lequal);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.CullFace);
        GL.Enable(EnableCap.PolygonOffsetFill);
        GL.PolygonOffset(1f, 1f);
        GL.UseProgram(program);
        Shaders.Matrix(GL.GetUniformLocation(program, "matrix"), matrix);
        GL.Uniform1(GL.GetUniformLocation(program, "albedo"), 0);
        GL.Uniform1(GL.GetUniformLocation(program, "opacity"), 1);
        foreach (ModelInstance instance in scene.Instances)
        {
            if (instance.Object.Hidden) continue;
            ModelMesh mesh = instance.Mesh;
            Vector4 tint = mesh.Tint;
            if (mesh.UsePlacementColor && instance.MaterialColor.HasValue) tint = instance.MaterialColor.Value;
            GL.Uniform4(GL.GetUniformLocation(program, "tint"), tint.X, tint.Y, tint.Z, tint.W);
            Shaders.Matrix(GL.GetUniformLocation(program, "model"), instance.Transform);
            GL.BindVertexArray(meshes[mesh].Vao);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, mesh.Color == null ? white : textures[mesh.Color]);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)Wrap(mesh.WrapX));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)Wrap(mesh.WrapY));
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, mesh.Opacity == null ? white : textures[mesh.Opacity]);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)Wrap(mesh.WrapX));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)Wrap(mesh.WrapY));
            GL.DrawElements(PrimitiveType.Triangles, mesh.Indices.Length, DrawElementsType.UnsignedInt, 0);
        }
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Disable(EnableCap.PolygonOffsetFill);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    private void Clear()
    {
        scene = null;
        foreach (MeshBuffers mesh in meshes.Values)
        {
            GL.DeleteBuffer(mesh.Vbo);
            GL.DeleteBuffer(mesh.Ebo);
            GL.DeleteVertexArray(mesh.Vao);
        }
        foreach (int texture in textures.Values) GL.DeleteTexture(texture);
        meshes.Clear();
        textures.Clear();
    }

    public void Dispose()
    {
        Clear();
        GL.DeleteTexture(white);
        GL.DeleteProgram(program);
    }
}
