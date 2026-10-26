using Dear_ImGui_Sample.Backends;
using ImGuiNET;
using OpenTK.Windowing.Desktop;

namespace Banandium;

//https://github.com/NogginBops/ImGui.NET_OpenTK_Sample
public sealed class ImGuiController : IDisposable
{
    public ImGuiController(GameWindow window)
    {
        ImGui.CreateContext();
        ImGui.StyleColorsDark();
        ImguiImplOpenTK4.Init(window);
        ImguiImplOpenGL3.Init();
    }

    public void Begin(GameWindow window, float dt, bool flying)
    {
        var io = ImGui.GetIO();
        if (flying)
        {
            io.ConfigFlags |= ImGuiConfigFlags.NoMouse | ImGuiConfigFlags.NoMouseCursorChange;
        }
        else
        {
            io.ConfigFlags &= ~(ImGuiConfigFlags.NoMouse | ImGuiConfigFlags.NoMouseCursorChange);
        }
        ImguiImplOpenGL3.NewFrame();
        ImguiImplOpenTK4.NewFrame();
        io.DisplaySize = new(window.ClientSize.X, window.ClientSize.Y);
        io.DisplayFramebufferScale = new((float)window.FramebufferSize.X / window.ClientSize.X,
            (float)window.FramebufferSize.Y / window.ClientSize.Y);
        io.DeltaTime = Math.Max(dt, .0001f);
        ImGui.NewFrame();
    }

    public void End()
    {
        ImGui.Render();
        ImguiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());
    }

    public void Dispose()
    {
        ImguiImplOpenGL3.Shutdown();
        ImguiImplOpenTK4.Shutdown();
        ImGui.DestroyContext();
    }
}
