using System.Buffers.Binary;
using System.Text;
using ZstdSharp;

namespace Banandium;
public sealed class Bytes
{
    public byte[] Data;
    public Bytes(byte[] data)
    {
        Data = data;
    }

    public byte U8(int offset)
    {
        return Data[offset];
    }

    public ushort U16(int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(offset, 2));
    }

    public uint U32(int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(offset, 4));
    }

    public int I32(int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan(offset, 4));
    }

    public long I64(int offset)
    {
        return BinaryPrimitives.ReadInt64LittleEndian(Data.AsSpan(offset, 8));
    }

    public float F32(int offset)
    {
        return BitConverter.Int32BitsToSingle(I32(offset));
    }

    public string Text(int offset, int length)
    {
        return Encoding.UTF8.GetString(Data.AsSpan(offset, length));
    }

    public string CString(int offset)
    {
        int end = Array.IndexOf(Data, (byte)0, offset);
        if (end < 0)
        {
            throw new InvalidDataException("Unterminated string");
        }

        return Text(offset, end - offset);
    }

    public int Relative32(int offset)
    {
        return checked(offset + I32(offset));
    }

    public int Relative64(int offset)
    {
        return checked(offset + (int)I64(offset));
    }

    public int Count(int offset, int limit = 10_000_000)
    {
        int length = I32(offset);
        if (length < 0 || length > limit)
        {
            throw new InvalidDataException("Invalid array count");
        }

        return length;
    }

    public static byte[] Read(string path)
    {
        var data = File.ReadAllBytes(path);
        if (!path.EndsWith(".zs", StringComparison.OrdinalIgnoreCase))
        {
            return data;
        }

        using var decoder = new Decompressor();
        return decoder.Unwrap(data, 512 * 1024 * 1024).ToArray();
    }

    //https://github.com/EPD-Libraries/SarcLibrary
    public static Dictionary<string, byte[]> Sarc(byte[] data)
    {
        var archive = SarcLibrary.Sarc.FromBinary(data);
        var files = new Dictionary<string, byte[]>();
        foreach (var file in archive)
        {
            files.Add(file.Key, file.Value.ToArray());
        }
        return files;
    }
}

//https://github.com/EPD-Libraries/BymlLibrary
public sealed class Byml
{
    private readonly byte[] data;

    public Byml(byte[] data)
    {
        this.data = data;
    }

    public object? Read()
    {
        return ReadValue(BymlLibrary.Byml.FromBinary(data));
    }

    private static object? ReadValue(BymlLibrary.Byml node)
    {
        if (node.Value is BymlLibrary.Nodes.Containers.BymlMap map)
        {
            var result = new Dictionary<string, object?>();
            foreach (var entry in map)
            {
                result.Add(entry.Key, ReadValue(entry.Value));
            }
            return result;
        }
        if (node.Value is BymlLibrary.Nodes.Containers.BymlArray array)
        {
            var result = new List<object?>();
            foreach (var entry in array)
            {
                result.Add(ReadValue(entry));
            }
            return result;
        }
        return node.Value;
    }
}
