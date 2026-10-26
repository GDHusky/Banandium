using System.Numerics;

namespace Banandium;
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length >= 2 && (args[0] == "--inspect" || args[0] == "--audit"))
            {
                return InspectScenes(args);
            }

            using Viewer viewer = new Viewer(args);
            viewer.Run();
            return viewer.ExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int InspectScenes(string[] args)
    {
        Dump dump = new Dump(args[1]);
        Console.WriteLine($"{dump.Roots.Count} romfs layers; {dump.Scenes.Count} scenes");
        List<string> names = new List<string>();
        if (args[0] == "--audit")
        {
            names.AddRange(dump.Scenes.Keys);
        }
        else
        {
            for (int i = 2; i < args.Length; i++)
            {
                names.Add(args[i]);
            }
        }

        int failed = 0;
        int loaded = 0;
        int withCollision = 0;
        foreach (string name in names)
        {
            try
            {
                SceneData scene = dump.Load(name);
                loaded++;
                if (scene.Collision.Triangles > 0)
                {
                    withCollision++;
                }

                Console.WriteLine($"{name}: {scene.Collision.Triangles:N0} collision triangles; {scene.DeathAreas} death areas");

                PrintBounds(scene.Collision);
            }
            catch (Exception exception)
            {
                failed++;
                Console.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"Loaded {loaded}; with static collision {withCollision}; failed {failed}");
        if (failed > 0)
        {
            return 1;
        }

        return 0;
    }

    private static void PrintBounds(MeshData mesh)
    {
        if (mesh.Vertices.Count == 0)
        {
            return;
        }

        Vector3 minimum = mesh.Vertices[0];
        Vector3 maximum = mesh.Vertices[0];
        foreach (Vector3 vertex in mesh.Vertices)
        {
            minimum = Vector3.Min(minimum, vertex);
            maximum = Vector3.Max(maximum, vertex);
        }

        Console.WriteLine($"  bounds: {minimum} .. {maximum}");
    }
}
