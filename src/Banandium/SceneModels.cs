using System.Numerics;
using BfresLibrary;
using BfresLibrary.Helpers;
using BfresLibrary.GX2;
using Syroot.BinaryData;
using Vector4F = Syroot.Maths.Vector4F;

namespace Banandium;

public sealed class ModelMesh
{
    public float[] Vertices = [];
    public uint[] Indices = [];
    public ModelTexture? Color;
    public ModelTexture? Opacity;
    public GX2TexClamp WrapX;
    public GX2TexClamp WrapY;
    public Vector4 Tint = Vector4.One;
    public bool UsePlacementColor;
    public MeshPart Picking = null!;
}

public sealed record ModelInstance(ModelMesh Mesh, Matrix4x4 Transform, Vector4? MaterialColor, SceneObject Object)
{
    public float HitDistance(Vector3 origin, Vector3 direction)
    {
        if (Object.Hidden) return float.PositiveInfinity;
        Matrix4x4.Invert(Transform, out Matrix4x4 inverse);
        return Mesh.Picking.HitDistance(Vector3.Transform(origin, inverse), Vector3.TransformNormal(direction, inverse));
    }
}

public sealed class SceneModels
{
    public List<ModelInstance> Instances { get; } = new List<ModelInstance>();
    public int MissingModels { get; private set; }
    public int SkippedShapes { get; private set; }
    public int MissingTextures { get; private set; }
    private readonly Dump dump;
    private readonly Dictionary<(string Model, string Actor), List<ModelMesh>> models = new Dictionary<(string, string), List<ModelMesh>>();
    private readonly Dictionary<string, ModelTexture?> textures = new Dictionary<string, ModelTexture?>();
    private readonly ActorMaterials actorMaterials;
    private readonly MaterialPresets materialPresets;

    public SceneModels(Dump dump, string scene)
    {
        this.dump = dump;
        actorMaterials = new ActorMaterials(dump);
        materialPresets = new MaterialPresets(dump, scene);
    }

    public void Load(Dictionary<string, object?> scenario)
    {
        foreach (string listName in new[] { "ObjectList", "PlacementList" })
        {
            if (!scenario.TryGetValue(listName, out object? list)) continue;
            foreach (object? value in (List<object?>)list!)
            {
                var entry = (Dictionary<string, object?>)value!;
                //placement helper surfaces are not visible scenery
                if (entry.GetValueOrDefault("ParameterClassName") as string == "ManualPlacementSurface") continue;
                string name = entry.GetValueOrDefault("ModelName") as string ?? "";
                if (name.Length == 0) continue;
                string actor = entry.GetValueOrDefault("ObjectName") as string ?? name;
                if (!models.TryGetValue((name, actor), out List<ModelMesh>? meshes))
                {
                    meshes = ReadModel(name, actor);
                    models.Add((name, actor), meshes);
                }
                Matrix4x4 transform = Dump.ReadTransform(entry);
                if (transform.GetDeterminant() == 0) continue;
                string material = materialPresets.Resolve(entry);
                Vector4? color = material == "" ? null : actorMaterials.ReadColor(material);
                var item = new SceneObject(actor);
                foreach (ModelMesh mesh in meshes) Instances.Add(new ModelInstance(mesh, transform, color, item));
            }
        }
    }

    //https://github.com/KillzXGaming/BfresLibrary
    private List<ModelMesh> ReadModel(string name, string actor)
    {
        var meshes = new List<ModelMesh>();
        string? path = dump.Resolve($"Model/{name}.{name}.bfres.zs");
        if (path == null)
        {
            MissingModels++;
            return meshes;
        }
        using var stream = new MemoryStream(Bytes.Read(path));
        var file = new ResFile(stream);
        Dictionary<string, Vector4> fittedColors = actorMaterials.Read(actor);
        foreach (Model model in file.Models.Values)
        {
            foreach (Shape shape in model.Shapes.Values)
            {
                if (shape.VertexSkinCount != 0 || shape.Meshes.Count == 0 ||
                    shape.Meshes[0].PrimitiveType != GX2PrimitiveType.Triangles)
                {
                    SkippedShapes++;
                    continue;
                }
                var buffer = new VertexBufferHelper(model.VertexBuffers[shape.VertexBufferIndex], ByteOrder.LittleEndian);
                Vector4F[] positions = buffer["_p0"].Data;
                Vector4F[]? normals = FindAttribute(buffer, "_n0");
                Vector4F[]? uv = FindAttribute(buffer, "_u0");
                Material material = model.Materials[shape.MaterialIndex];
                if (!material.Visible) continue;
                Vector4F[]? colors = null;
                if (OptionEnabled(material, "enable_vertex_color") || OptionEnabled(material, "enable_vtx_color0"))
                    colors = FindAttribute(buffer, "_c0");
                Matrix4x4 bone = BoneTransform(model.Skeleton, shape.BoneIndex);
                Matrix4x4.Invert(bone, out Matrix4x4 inverse);
                Matrix4x4 normalMatrix = Matrix4x4.Transpose(inverse);
                var mesh = new ModelMesh();
                mesh.Vertices = new float[positions.Length * 12];
                for (int i = 0; i < positions.Length; i++)
                {
                    Vector3 p = Vector3.Transform(new Vector3(positions[i].X, positions[i].Y, positions[i].Z), bone);
                    Vector3 n = normals == null ? Vector3.UnitY : new Vector3(normals[i].X, normals[i].Y, normals[i].Z);
                    n = Vector3.Normalize(Vector3.TransformNormal(n, normalMatrix));
                    int offset = i * 12;
                    mesh.Vertices[offset] = p.X;
                    mesh.Vertices[offset + 1] = p.Y;
                    mesh.Vertices[offset + 2] = p.Z;
                    mesh.Vertices[offset + 3] = n.X;
                    mesh.Vertices[offset + 4] = n.Y;
                    mesh.Vertices[offset + 5] = n.Z;
                    mesh.Vertices[offset + 6] = uv == null ? 0 : uv[i].X;
                    mesh.Vertices[offset + 7] = uv == null ? 0 : uv[i].Y;
                    mesh.Vertices[offset + 8] = colors == null ? 1 : colors[i].X;
                    mesh.Vertices[offset + 9] = colors == null ? 1 : colors[i].Y;
                    mesh.Vertices[offset + 10] = colors == null ? 1 : colors[i].Z;
                    mesh.Vertices[offset + 11] = colors == null ? 1 : colors[i].W;
                }
                Mesh sourceMesh = shape.Meshes[0];
                var indices = new List<uint>();
                foreach (uint index in sourceMesh.GetIndices()) indices.Add(index + sourceMesh.FirstVertex);
                mesh.Indices = indices.ToArray();
                var picking = new MeshData();
                foreach (uint index in mesh.Indices)
                {
                    int offset = (int)index * 12;
                    picking.Vertices.Add(new Vector3(mesh.Vertices[offset], mesh.Vertices[offset + 1], mesh.Vertices[offset + 2]));
                }
                mesh.Picking = new MeshPart(picking, 0, picking.Vertices.Count, new SceneObject(shape.Name));
                if (material.ShaderParams.TryGetValue("basecolor_const", out ShaderParam tint) && tint.DataValue is float[] color)
                    mesh.Tint = new Vector4(color[0], color[1], color[2], color[3]);
                mesh.Color = ReadTexture(material, "_a0");
                mesh.Opacity = ReadTexture(material, "_o0");
                mesh.UsePlacementColor = material.Samplers.ContainsKey("_va0") && mesh.Color == null;
                if (material.Samplers.ContainsKey("_va0") &&
                    (fittedColors.TryGetValue(material.Name, out Vector4 fitted) || fittedColors.TryGetValue("*", out fitted)))
                {
                    mesh.Color = null;
                    mesh.Tint = fitted;
                }
                if (material.Samplers.ContainsKey("_a0"))
                {
                    mesh.WrapX = material.Samplers["_a0"].TexSampler.ClampX;
                    mesh.WrapY = material.Samplers["_a0"].TexSampler.ClampY;
                }
                meshes.Add(mesh);
            }
        }
        return meshes;
    }

    private static bool OptionEnabled(Material material, string name)
    {
        return material.ShaderAssign.ShaderOptions.ContainsKey(name) && material.ShaderAssign.ShaderOptions[name] == "True";
    }

    private ModelTexture? ReadTexture(Material material, string sampler)
    {
        if (material.ShaderAssign.SamplerAssigns.ContainsKey(sampler))
            sampler = material.ShaderAssign.SamplerAssigns[sampler];
        int index = material.Samplers.IndexOf(sampler);
        if (index < 0) return null;
        string name = material.TextureRefs[index].Name;
        if (textures.TryGetValue(name, out ModelTexture? texture)) return texture;
        string? path = dump.Resolve($"Tex/{name}.bntx.zs");
        texture = path == null ? null : ModelTextures.Read(path);
        if (texture == null) MissingTextures++;
        textures.Add(name, texture);
        return texture;
    }

    private static Vector4F[]? FindAttribute(VertexBufferHelper buffer, string name)
    {
        foreach (VertexBufferHelperAttrib attribute in buffer.Attributes)
            if (attribute.Name == name) return attribute.Data;
        return null;
    }

    private static Matrix4x4 BoneTransform(Skeleton skeleton, int index)
    {
        Matrix4x4 transform = Matrix4x4.Identity;
        while (index >= 0)
        {
            Bone bone = skeleton.Bones[index];
            Vector4F r = bone.Rotation;
            Matrix4x4 rotation;
            if (bone.FlagsRotation == BoneFlagsRotation.Quaternion)
                rotation = Matrix4x4.CreateFromQuaternion(new Quaternion(r.X, r.Y, r.Z, r.W));
            else
                rotation = Matrix4x4.CreateRotationX(r.X) * Matrix4x4.CreateRotationY(r.Y) * Matrix4x4.CreateRotationZ(r.Z);
            transform *= Matrix4x4.CreateScale(bone.Scale.X, bone.Scale.Y, bone.Scale.Z) * rotation *
                Matrix4x4.CreateTranslation(bone.Position.X, bone.Position.Y, bone.Position.Z);
            index = bone.ParentIndex;
        }
        return transform;
    }
}
