using System.Buffers.Binary;
using System.Numerics;

namespace Banandium;
//read-only decoder for the sdkv20220100 tagfiles observed in this dump
public sealed class Phive
{
    readonly Bytes reader;
    readonly Dictionary<string, (int Start, int End)> chunks = [];
    readonly Dictionary<int, string> objects = [];
    readonly MeshData mesh;
    int dataStart;
    public Phive(byte[] bytes, MeshData output)
    {
        reader = new(bytes);
        mesh = output;
    }

    void ReadChunks(int start, int end)
    {
        for (int offset = start; offset < end;)
        {
            int size = (int)(BinaryPrimitives.ReadUInt32BigEndian(reader.Data.AsSpan(offset, 4)) & 0x3fffffff);
            if (size < 8 || size > end - offset)
            {
                throw new InvalidDataException("Invalid Havok chunk");
            }

            string name = reader.Text(offset + 4, 4);
            chunks[name] = (offset + 8, offset + size);
            if (name is "TAG0" or "TYPE" or "INDX")
            {
                ReadChunks(offset + 8, offset + size);
            }

            offset += size;
        }
    }

    int ReadPackedInteger(ref int offset)
    {
        int v = reader.U8(offset++);
        if (v < 0x80)
        {
            return v;
        }

        int count;
        if (v < 0xc0)
        {
            count = 1;
        }
        else if (v < 0xe0)
        {
            count = 2;
        }
        else if (v < 0xf0)
        {
            count = 3;
        }
        else
        {
            throw new InvalidDataException("Unsupported Havok varint");
        }

        v &= (1 << (7 - count)) - 1;
        for (int j = 0; j < count; j++)
        {
            v = (v << 8) | reader.U8(offset++);
        }

        return v;
    }

    Vector3 ReadVector(int offset)
    {
        return new(reader.F32(offset), reader.F32(offset + 4), reader.F32(offset + 8));
    }

    public void Read()
    {
        ReadTagfile(reader.I32(0x30));
    }

    public void ReadShapeFile()
    {
        ReadTagfile(reader.I32(0x0c));
    }

    public int UnsupportedShapes { get; private set; }

    private void ReadTagfile(int tag)
    {
        if (reader.Text(0, 5) != "Phive")
        {
            throw new InvalidDataException("Expected Phive file");
        }

        if (tag < 0 || tag > reader.Data.Length - 8 || reader.Text(tag + 4, 4) != "TAG0")
        {
            throw new InvalidDataException("Unsupported physics header");
        }

        int size = (int)(BinaryPrimitives.ReadUInt32BigEndian(reader.Data.AsSpan(tag, 4)) & 0x3fffffff);
        ReadChunks(tag, checked(tag + size));
        if (reader.Text(chunks["SDKV"].Start, 8) != "20220100")
        {
            throw new InvalidDataException("Unsupported Havok SDK version");
        }

        dataStart = chunks["DATA"].Start;
        var typeStrings = chunks["TST1"];
        var strings = reader.Text(typeStrings.Start, typeStrings.End - typeStrings.Start).Split('\0');
        int cursor = chunks["TNA1"].Start, count = ReadPackedInteger(ref cursor);
        var types = new List<string>
        {
            "null"
        };
        for (int j = 1; j < count; j++)
        {
            types.Add(strings[ReadPackedInteger(ref cursor)]);
            int parameterCount = ReadPackedInteger(ref cursor);
            for (int k = 0; k < parameterCount; k++)
            {
                ReadPackedInteger(ref cursor);
                ReadPackedInteger(ref cursor);
            }
        }

        var item = chunks["ITEM"];
        for (int offset = item.Start + 12; offset < item.End; offset += 12)
        {
            int type = (int)(reader.U32(offset) & 0xffffff);
            int objectOffset = checked(dataStart + reader.I32(offset + 4));
            if (type < types.Count && types[type].StartsWith("hknp") && reader.I32(offset + 8) == 1)
            {
                objects.TryAdd(objectOffset, types[type]);
            }
        }

        ReadShape(dataStart, Matrix4x4.Identity, new HashSet<int>(), 0);
    }

    void ReadShape(int offset, Matrix4x4 world, HashSet<int> stack, int depth)
    {
        if (depth > 32 || !stack.Add(offset))
        {
            throw new InvalidDataException("Cyclic compound shape");
        }

        string type = objects[offset];

        switch (type)
        {
            case "hknpCompoundShape":
                int first = reader.Relative64(offset + 72);
                int instanceCount = reader.Count(offset + 80, 1_000_000);
                for (int j = 0; j < instanceCount; j++)
                {
                    int entryOffset = checked(first + j * 64);
                    if (reader.I64(entryOffset + 48) == 0)
                    {
                        continue;
                    }

                    var rotation = new Quaternion(reader.F32(entryOffset), reader.F32(entryOffset + 4), reader.F32(entryOffset + 8), reader.F32(entryOffset + 12));
                    var local = Matrix4x4.CreateScale(ReadVector(entryOffset + 32)) * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation)) * Matrix4x4.CreateTranslation(ReadVector(entryOffset + 16));
                    ReadShape(reader.Relative64(entryOffset + 48), local * world, stack, depth + 1);
                }

                break;
            case "hknpMeshShape":
                ReadMesh(offset, world);
                break;
            case "hknpBoxShape":
            case "hknpConvexShape":
            case "hknpConvexPolytopeShape":
                ReadHull(offset, world);
                break;
            default:
                UnsupportedShapes++;
                break;
        }

        stack.Remove(offset);
    }

    //https://github.com/nvnprogram/PhiveConverter
    void ReadMesh(int offset, Matrix4x4 world)
    {
        Vector3 step = ReadVector(offset + 0x60);
        int sections = reader.Relative32(offset + 0x90);
        int sectionCount = reader.Count(offset + 0x94, 100_000);
        for (int j = 0; j < sectionCount; j++)
        {
            int sectionOffset = sections + j * 64, primitiveOffset = reader.Relative32(sectionOffset + 8), primitiveCount = reader.Count(sectionOffset + 12, 1_000_000), vertexOffset = reader.Relative32(sectionOffset + 16);
            //bananza stores the vertex buffer length in bytes int_max section
            bool unquantized = reader.I32(sectionOffset + 32) == int.MaxValue && reader.I32(sectionOffset + 36) == int.MaxValue && reader.I32(sectionOffset + 40) == int.MaxValue;
            int stride = unquantized ? 12 : 6, vertexBytes = reader.Count(sectionOffset + 20);
            Vector3 Vertex(byte id)
            {
                if ((id + 1) * stride > vertexBytes)
                {
                    throw new InvalidDataException("Collision vertex index outside section");
                }

                int v = vertexOffset + id * stride;
                if (unquantized)
                {
                    return ReadVector(v);
                }

                return new Vector3((reader.U16(v) + (long)reader.I32(sectionOffset + 32)) * step.X, (reader.U16(v + 2) + (long)reader.I32(sectionOffset + 36)) * step.Y, (reader.U16(v + 4) + (long)reader.I32(sectionOffset + 40)) * step.Z);
            }

            for (int k = 0; k < primitiveCount; k++)
            {
                int entryOffset = primitiveOffset + k * 4;
                byte a = reader.U8(entryOffset), bb = reader.U8(entryOffset + 1), c = reader.U8(entryOffset + 2), d = reader.U8(entryOffset + 3);
                mesh.Triangle(Vertex(a), Vertex(bb), Vertex(c), world);
                if (d != c && d != a)
                {
                    mesh.Triangle(Vertex(a), Vertex(c), Vertex(d), world);
                }
            }
        }
    }

    void ReadHull(int offset, Matrix4x4 world)
    {
        int hullOffset = offset + 64, vertexOffset = reader.Relative32(hullOffset), faces = reader.Relative32(hullOffset + 16), faceCount = reader.Count(hullOffset + 20, 100_000), indices = reader.Relative32(hullOffset + 24);
        for (int j = 0; j < faceCount; j++)
        {
            int face = faces + j * 4, first = reader.U16(face), n = reader.U8(face + 2);
            for (int k = 1; k + 1 < n; k++)
            {
                mesh.Triangle(ReadVector(vertexOffset + 16 * reader.U8(indices + first)), ReadVector(vertexOffset + 16 * reader.U8(indices + first + k)), ReadVector(vertexOffset + 16 * reader.U8(indices + first + k + 1)), world);
            }
        }
    }
}

