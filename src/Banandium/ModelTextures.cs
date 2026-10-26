using AssetRipper.TextureDecoder.Astc;
using AssetRipper.TextureDecoder.Rgb.Formats;
using BfresLibrary.Swizzling;
using Syroot.NintenTools.NSW.Bntx;
using Syroot.NintenTools.NSW.Bntx.GFX;

namespace Banandium;

public sealed record ModelTexture(int Width, int Height, byte[] Pixels, bool Srgb = false);

public static class ModelTextures
{
    //https://github.com/KillzXGaming/BfresLibrary
    //https://github.com/AssetRipper/AssetRipper.TextureDecoder
    public static ModelTexture? Read(string path)
    {
        byte[] data = Bytes.Read(path);
        using var stream = new MemoryStream(data);
        var file = new BntxFile(stream);
        Texture texture = file.Textures[0];
        if (texture.ArrayLength != 1 || texture.Depth != 1) return null;

        string format = texture.Format.ToString();
        bool astc = format.StartsWith("ASTC_");
        uint blockWidth = 1, blockHeight = 1, blockSize = 4;
        if (astc)
        {
            string[] dimensions = format.Split('_')[1].Split('x');
            blockWidth = uint.Parse(dimensions[0]);
            blockHeight = uint.Parse(dimensions[1]);
            blockSize = 16;
        }
        else if (texture.Format == SurfaceFormat.R8_UNORM) blockSize = 1;
        else if (texture.Format != SurfaceFormat.R8_G8_B8_A8_UNORM &&
                 texture.Format != SurfaceFormat.R8_G8_B8_A8_SRGB) return null;

        byte[] tiled = texture.TextureData[0][0];
        bool linear = texture.TileMode == TileMode.LinearAligned;
        if (!linear && data[0x20] == 'O')
        {
            //ounce textures swap address bits 5 and 6 within each gob
            byte[] reordered = new byte[tiled.Length];
            for (int i = 0; i < tiled.Length; i++)
            {
                int address = (i & ~96) | ((i & 32) << 1) | ((i & 64) >> 1);
                reordered[i] = tiled[address];
            }
            tiled = reordered;
        }
        byte[] blocks = TegraX1Swizzle.deswizzle(texture.Width, texture.Height, 1,
            blockWidth, blockHeight, 1, 1, blockSize, linear ? 1u : 0u,
            (int)texture.BlockHeightLog2, tiled);
        int width = (int)texture.Width, height = (int)texture.Height;
        byte[] pixels;
        if (astc)
        {
            AstcDecoder.DecodeASTC<ColorRGBA<byte>, byte>(blocks, width, height,
                (int)blockWidth, (int)blockHeight, out pixels);
        }
        else
        {
            pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                int row = y * width * (int)blockSize;
                for (int x = 0; x < width; x++)
                {
                    int source = row + x * (int)blockSize;
                    int target = (y * width + x) * 4;
                    for (int c = 0; c < 4; c++)
                        pixels[target + c] = blockSize == 1 ? blocks[source] : blocks[source + c];
                }
            }
        }
        ChannelType[] channels = { texture.ChannelRed, texture.ChannelGreen, texture.ChannelBlue, texture.ChannelAlpha };
        byte[] pixel = new byte[4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            Array.Copy(pixels, i, pixel, 0, 4);
            for (int c = 0; c < 4; c++) pixels[i + c] = SelectChannel(pixel, channels[c]);
        }
        return new ModelTexture(width, height, pixels, format.EndsWith("_SRGB"));
    }

    private static byte SelectChannel(byte[] pixel, ChannelType channel)
    {
        switch (channel)
        {
            case ChannelType.Zero: return 0;
            case ChannelType.One: return 255;
            case ChannelType.Red: return pixel[0];
            case ChannelType.Green: return pixel[1];
            case ChannelType.Blue: return pixel[2];
            case ChannelType.Alpha: return pixel[3];
            default: throw new InvalidDataException("Unknown texture channel");
        }
    }
}
