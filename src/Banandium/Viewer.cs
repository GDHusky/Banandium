using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Drawing.Imaging;
using System.Numerics;
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;

namespace Banandium;
public sealed class Viewer : GameWindow
{
    private ImGuiController ui = null !;
    private SceneRenderer renderer = null !;
    private Dump? dump;
    private SceneData? scene;
    private Task<Dump>? indexing;
    private Task<SceneData>? loading;
    private readonly string[] arguments;
    private string pendingDumpPath = "";
    private string pendingSceneName = "";
    private string startupScene = "";
    private string selectedScene = "";
    private string sceneFilter = "";
    private string errorMessage = "";
    private bool openSceneDialog;
    private bool sceneDialogVisible;
    private bool openErrorDialog;
    private DisplayMode collisionMode = DisplayMode.Opaque;
    private DisplayMode smokeAreaMode = DisplayMode.SemiTransparent;
    private bool collisionShaded = true;
    private bool smokeAreaShaded = true;
    private Vector3 cameraPosition = new Vector3(30, 20, 30);
    private float cameraYaw = -135;
    private float cameraPitch = -20;
    private float cameraSpeed = 30;
    private bool lookingAround;
    private SceneArea? selectedArea;
    private bool showAreaDetails;
    private int smokeFrames;
    public int ExitCode { get; private set; }

    public Viewer(string[] args) : base(new GameWindowSettings { UpdateFrequency = 120 }, CreateWindowSettings())
    {
        arguments = args;
    }

    private static NativeWindowSettings CreateWindowSettings()
    {
        return new NativeWindowSettings
        {
            ClientSize = new OpenTK.Mathematics.Vector2i(1440, 900),
            Title = "Banandium",
            API = ContextAPI.OpenGL,
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core
        };
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        VSync = VSyncMode.On;
        ui = new ImGuiController(this);
        renderer = new SceneRenderer();
        if (arguments.Length > 0 && !arguments[0].StartsWith("--"))
        {
            if (arguments.Length > 1 && !arguments[1].StartsWith("--"))
            {
                startupScene = arguments[1];
            }

            OpenDump(arguments[0]);
        }

        Console.WriteLine($"OpenGL {GL.GetString(StringName.Version)} / {GL.GetString(StringName.Renderer)}");
    }

    private void OpenDump(string path)
    {
        errorMessage = "";
        pendingDumpPath = path;
        indexing = Task.Run(ReadDump);
    }

    private Dump ReadDump()
    {
        return new Dump(pendingDumpPath);
    }

    private void LoadScene(string name)
    {
        if (dump == null || loading != null)
        {
            return;
        }

        errorMessage = "";
        pendingSceneName = name;
        loading = Task.Run(ReadScene);
    }

    private SceneData ReadScene()
    {
        return dump!.Load(pendingSceneName);
    }

    private void CheckLoadingTasks()
    {
        if (indexing != null && indexing.IsCompleted)
        {
            try
            {
                dump = indexing.GetAwaiter().GetResult();
                scene = null;
                selectedArea = null;
                showAreaDetails = false;
                selectedScene = "";
                sceneFilter = "";
                renderer.Upload(new SceneData("", new MeshData(), new MeshData(), 0));
                Title = "Banandium";
                if (startupScene != "")
                {
                    LoadScene(startupScene);
                    startupScene = "";
                }
                else
                {
                    openSceneDialog = true;
                }
            }
            catch (Exception exception)
            {
                ShowError(exception);
            }

            indexing = null;
        }

        if (loading != null && loading.IsCompleted)
        {
            try
            {
                SceneData loadedScene = loading.GetAwaiter().GetResult();
                renderer.Upload(loadedScene);
                scene = loadedScene;
                selectedArea = null;
                showAreaDetails = false;
                PlaceCameraAtScene();
                Title = "Banandium - " + scene.Name;
            }
            catch (Exception exception)
            {
                ShowError(exception);
            }

            loading = null;
        }
    }

    private void ShowError(Exception exception)
    {
        errorMessage = exception.Message;
        openErrorDialog = true;
        Console.Error.WriteLine(exception);
    }

    private void PlaceCameraAtScene()
    {
        if (scene == null)
        {
            return;
        }

        List<Vector3> vertices = new List<Vector3>();
        if (scene.Collision.Vertices.Count > 0)
        {
            vertices.AddRange(scene.Collision.Vertices);
        }
        else
        {
            vertices.AddRange(scene.Death.Vertices);
            vertices.AddRange(scene.IgnoreAreas.Vertices);
        }

        if (vertices.Count == 0)
        {
            return;
        }

        Vector3 minimum = vertices[0];
        Vector3 maximum = vertices[0];
        foreach (Vector3 vertex in vertices)
        {
            minimum = Vector3.Min(minimum, vertex);
            maximum = Vector3.Max(maximum, vertex);
        }

        Vector3 center = (minimum + maximum) / 2;
        float radius = Math.Max(5, (maximum - minimum).Length() * 0.5f);
        cameraYaw = -135;
        cameraPitch = -25;
        cameraPosition = center - GetCameraDirection() * radius * 2;
        cameraSpeed = Math.Max(5, radius * 0.25f);
    }

    private Vector3 GetCameraDirection()
    {
        float yawRadians = cameraYaw * MathF.PI / 180;
        float pitchRadians = cameraPitch * MathF.PI / 180;
        Vector3 direction = new Vector3(MathF.Cos(yawRadians) * MathF.Cos(pitchRadians), MathF.Sin(pitchRadians), MathF.Sin(yawRadians) * MathF.Cos(pitchRadians));
        return Vector3.Normalize(direction);
    }

    private void ReleaseMouse()
    {
        lookingAround = false;
        CursorState = CursorState.Normal;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (sceneDialogVisible || ImGui.GetIO().WantCaptureMouse)
        {
            return;
        }

        if (IsFocused && scene != null)
        {
            cameraSpeed = Math.Clamp(cameraSpeed * MathF.Pow(1.15f, e.OffsetY), 0.1f, 10000);
        }
    }

    protected override void OnUpdateFrame(FrameEventArgs e)
    {
        base.OnUpdateFrame(e);
        CheckLoadingTasks();
        if (!IsFocused || sceneDialogVisible || KeyboardState.IsKeyPressed(Keys.Escape))
        {
            ReleaseMouse();
        }

        if (!IsFocused || sceneDialogVisible || ImGui.GetIO().WantCaptureKeyboard)
        {
            return;
        }

        if (scene != null && !lookingAround && MouseState.IsButtonPressed(MouseButton.Right) && !ImGui.GetIO().WantCaptureMouse)
        {
            lookingAround = true;
            CursorState = CursorState.Grabbed;
        }

        if (!MouseState.IsButtonDown(MouseButton.Right))
        {
            ReleaseMouse();
        }

        if (lookingAround)
        {
            cameraYaw += MouseState.Delta.X * 0.12f;
            cameraPitch = Math.Clamp(cameraPitch - MouseState.Delta.Y * 0.12f, -89, 89);
        }

        if (scene == null)
        {
            return;
        }

        Vector3 forward = GetCameraDirection();
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 movement = Vector3.Zero;
        if (KeyboardState.IsKeyDown(Keys.W))
        {
            movement += forward;
        }

        if (KeyboardState.IsKeyDown(Keys.S))
        {
            movement -= forward;
        }

        if (KeyboardState.IsKeyDown(Keys.D))
        {
            movement += right;
        }

        if (KeyboardState.IsKeyDown(Keys.A))
        {
            movement -= right;
        }

        if (movement.LengthSquared() > 0)
        {
            float elapsed = (float)Math.Min(e.Time, 0.1);
            cameraPosition += Vector3.Normalize(movement) * cameraSpeed * elapsed;
        }
    }

    private void DrawDisplayModeMenu(string label, ref DisplayMode mode, ref bool shaded)
    {
        if (!ImGui.BeginMenu(label))
        {
            return;
        }

        if (ImGui.MenuItem("Semi-transparent", "", mode == DisplayMode.SemiTransparent))
        {
            mode = DisplayMode.SemiTransparent;
        }

        if (ImGui.MenuItem("Opaque", "", mode == DisplayMode.Opaque))
        {
            mode = DisplayMode.Opaque;
        }

        if (ImGui.MenuItem("Disabled", "", mode == DisplayMode.Disabled))
        {
            mode = DisplayMode.Disabled;
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Shaded", "", shaded))
        {
            shaded = !shaded;
        }

        ImGui.EndMenu();
    }

    private void PickArea()
    {
        if (scene == null || lookingAround || sceneDialogVisible ||
            ImGui.GetIO().WantCaptureMouse || !ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            return;
        }
        Vector2 mouse = ImGui.GetMousePos();
        Vector3 forward = GetCameraDirection();
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));
        float x = (2 * mouse.X / ClientSize.X - 1) * ClientSize.X / ClientSize.Y;
        float y = 1 - 2 * mouse.Y / ClientSize.Y;
        Vector3 direction = Vector3.Normalize(forward + (right * x + up * y) * MathF.Tan(MathF.PI / 6));
        float nearest = float.PositiveInfinity;
        if (collisionMode == DisplayMode.Opaque)
        {
            nearest = SceneAreas.HitDistance(scene.Collision, cameraPosition, direction);
        }
        selectedArea = null;
        foreach (SceneLayer layer in scene.AreaLayers)
        {
            if (layer.Mode == DisplayMode.Disabled) continue;
            foreach (SceneArea area in layer.Areas)
            {
                float distance = SceneAreas.HitDistance(area.Mesh, cameraPosition, direction);
                if (distance < nearest)
                {
                    nearest = distance;
                    selectedArea = area;
                }
            }
        }
        showAreaDetails = selectedArea != null;
    }

    private void DrawAreaDetails()
    {
        if (!showAreaDetails || selectedArea == null) return;
        ImGui.SetNextWindowSize(new Vector2(440, 420), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Area details", ref showAreaDetails))
        {
            ImGui.TextUnformatted(selectedArea.Name);
            ImGui.Separator();
            string[] importantFields = { "ParameterClassName", "ModelName", "Priority", "IsIgnoreArea",
                "Translate", "Rotate", "Scale", "StageName", "IntervalIndex" };
            foreach (string name in importantFields)
            {
                if (selectedArea.Fields.TryGetValue(name, out object? value))
                {
                    ImGui.TextWrapped(name + ": " + FormatAreaField(value));
                }
            }
            if (selectedArea.Fields.GetValueOrDefault("Links") is Dictionary<string, object?> links)
            {
                ImGui.Separator();
                ImGui.TextUnformatted("Links");
                if (links.Count == 0) ImGui.TextDisabled("None");
                foreach (var link in links)
                {
                    if (!ImGui.TreeNode(link.Key)) continue;
                    if (link.Value is List<object?> targets)
                    {
                        foreach (object? target in targets)
                        {
                            if (target is not Dictionary<string, object?> fields) continue;
                            ImGui.TextWrapped("ObjectName: " + fields.GetValueOrDefault("ObjectName"));
                            ImGui.TextWrapped("Id: " + fields.GetValueOrDefault("Id"));
                            ImGui.Separator();
                        }
                    }
                    ImGui.TreePop();
                }
            }
            if (ImGui.CollapsingHeader("All placement fields"))
            {
                foreach (var field in selectedArea.Fields)
                {
                    if (field.Key == "Links") continue;
                    ImGui.TextWrapped(field.Key + ": " + FormatAreaField(field.Value));
                }
            }
            ImGui.TextWrapped("Authored placement. Switch states and priorities are not simulated.");
        }
        ImGui.End();
    }

    private static string FormatAreaField(object? value)
    {
        if (value == null) return "null";
        if (value is string text) return text;
        if (value is Dictionary<string, object?> vector && vector.ContainsKey("X") &&
            vector.ContainsKey("Y") && vector.ContainsKey("Z"))
        {
            return $"X: {Convert.ToSingle(vector["X"]):0.###}, Y: {Convert.ToSingle(vector["Y"]):0.###}, Z: {Convert.ToSingle(vector["Z"]):0.###}";
        }
        return System.Text.Json.JsonSerializer.Serialize(value);
    }

    private void ChooseDumpFolder()
    {
        ReleaseMouse();
        using FolderBrowserDialog dialog = new FolderBrowserDialog
        {
            Description = "Select your extracted game dump or romfs folder",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            OpenDump(dialog.SelectedPath);
        }
    }

    private void DrawMenuBar()
    {
        if (!ImGui.BeginMainMenuBar())
        {
            return;
        }

        bool busy = indexing != null || loading != null;
        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Open dump...", "", false, !busy))
            {
                ChooseDumpFolder();
            }

            if (ImGui.MenuItem("Exit"))
            {
                Close();
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("View"))
        {
            if (ImGui.MenuItem("Change scene...", "", false, dump != null && !busy))
            {
                openSceneDialog = true;
            }

            ImGui.Separator();
            DrawDisplayModeMenu("Collision", ref collisionMode, ref collisionShaded);
            if (scene != null)
            {
                ImGui.Separator();
                foreach (SceneLayer layer in scene.AreaLayers)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(layer.Color, 1));
                    DrawDisplayModeMenu(layer.Name, ref layer.Mode, ref layer.Shaded);
                    ImGui.PopStyleColor();
                }
            }
            ImGui.EndMenu();
        }

        ImGui.EndMainMenuBar();
    }

    private void DrawSceneDialog()
    {
        if (openSceneDialog)
        {
            ReleaseMouse();
            sceneDialogVisible = true;
            if (scene != null)
            {
                selectedScene = scene.Name;
            }

            ImGui.OpenPopup("Change scene");
            openSceneDialog = false;
        }

        ImGui.SetNextWindowSize(new Vector2(590, 540), ImGuiCond.Appearing);
        ImGui.SetNextWindowPos(new Vector2(ClientSize.X / 2f, ClientSize.Y / 2f), ImGuiCond.Appearing, new Vector2(0.5f));
        if (!ImGui.BeginPopupModal("Change scene", ref sceneDialogVisible, ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }

        if (dump != null)
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##filter", "Filter scenes...", ref sceneFilter, 160);
            ImGui.BeginChild("scene-list", new Vector2(0, 330), ImGuiChildFlags.Borders);
            foreach (string name in dump.Scenes.Keys)
            {
                if (!name.Contains(sceneFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ImGui.Selectable(name, selectedScene == name))
                {
                    selectedScene = name;
                }
            }

            ImGui.EndChild();
        }

        ImGui.BeginDisabled(selectedScene == "");
        if (ImGui.Button("Open scene"))
        {
            LoadScene(selectedScene);
            sceneDialogVisible = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            sceneDialogVisible = false;
            ImGui.CloseCurrentPopup();
        }

        if (scene != null && ImGui.CollapsingHeader("Current scene details"))
        {
            ImGui.Text($"{scene.Collision.Triangles:N0} collision triangles; {scene.DeathAreas} death areas");
        }

        ImGui.EndPopup();
    }

    private void DrawErrorDialog()
    {
        if (openErrorDialog)
        {
            ReleaseMouse();
            ImGui.OpenPopup("Unable to load");
            openErrorDialog = false;
        }

        ImGui.SetNextWindowSize(new Vector2(500, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal("Unable to load", ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }

        ImGui.TextWrapped(errorMessage);
        if (ImGui.Button("OK"))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    protected override void OnRenderFrame(FrameEventArgs e)
    {
        base.OnRenderFrame(e);
        if (ClientSize.X <= 0 || ClientSize.Y <= 0)
        {
            return;
        }

        ui.Begin(this, (float)e.Time, lookingAround);
        DrawMenuBar();
        DrawSceneDialog();
        DrawErrorDialog();
        PickArea();
        DrawAreaDetails();
        GL.Viewport(0, 0, FramebufferSize.X, FramebufferSize.Y);
        GL.ClearColor(0, 0, 0, 1);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        Vector3 forward = GetCameraDirection();
        Matrix4x4 view = Matrix4x4.CreateLookAt(cameraPosition, cameraPosition + forward, Vector3.UnitY);
        Matrix4x4 projection = CreateProjection();
        renderer.Draw(view * projection, cameraPosition, forward, collisionMode, collisionShaded);
        ui.End();
        RunGraphicsSmokeTest();
        SwapBuffers();
    }

    private Matrix4x4 CreateProjection()
    {
        float near = 0.05f;
        float far = 100000;
        float focalLength = 1 / MathF.Tan(MathF.PI / 6);
        float aspect = (float)ClientSize.X / ClientSize.Y;
        return new Matrix4x4(focalLength / aspect, 0, 0, 0, 0, focalLength, 0, 0, 0, 0, (far + near) / (near - far), -1, 0, 0, 2 * far * near / (near - far), 0);
    }

    private void RunGraphicsSmokeTest()
    {
        if (Array.IndexOf(arguments, "--smoke") < 0)
        {
            return;
        }

        if (scene == null && errorMessage == "")
        {
            return;
        }

        smokeFrames++;
        if (smokeFrames % 8 != 0)
        {
            return;
        }

        int step = smokeFrames / 8 - 1;
        Directory.CreateDirectory("artifacts");
        string filename = $"artifacts/modes-{step}.png";
        if (step == 0)
        {
            filename = "artifacts/viewer.png";
        }

        if (step >= 10 && step <= 13)
        {
            filename = $"artifacts/shading-{step - 10}.png";
        }

        if (step == 14)
        {
            filename = "artifacts/scene-dialog.png";
        }

        SaveFrame(filename);
        var glError = GL.GetError();
        Console.WriteLine($"Smoke: {scene?.Name}; collision={collisionMode}; death={smokeAreaMode}; shading={collisionShaded}/{smokeAreaShaded}; GL error={glError}; {errorMessage}");
        if (glError != OpenTK.Graphics.OpenGL4.ErrorCode.NoError || errorMessage != "")
        {
            ExitCode = 1;
        }

        if (step == 14 || ExitCode != 0)
        {
            Close();
        }
        else if (step == 13)
        {
            openSceneDialog = true;
        }
        else if (step >= 9)
        {
            collisionMode = DisplayMode.Opaque;
            smokeAreaMode = DisplayMode.SemiTransparent;
            int combination = step - 9;
            collisionShaded = (combination & 1) != 0;
            smokeAreaShaded = (combination & 2) != 0;
        }
        else
        {
            collisionMode = (DisplayMode)(step / 3);
            smokeAreaMode = (DisplayMode)(step % 3);
        }
        if (scene != null)
        {
            foreach (SceneLayer layer in scene.AreaLayers)
            {
                layer.Mode = smokeAreaMode;
                layer.Shaded = smokeAreaShaded;
            }
        }
    }

    private void SaveFrame(string path)
    {
        using Bitmap bitmap = new Bitmap(FramebufferSize.X, FramebufferSize.Y);
        Rectangle rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData bits = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        GL.ReadPixels(0, 0, bitmap.Width, bitmap.Height, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, bits.Scan0);
        bitmap.UnlockBits(bits);
        bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);
        bitmap.Save(path, ImageFormat.Png);
    }

    protected override void OnUnload()
    {
        ReleaseMouse();
        renderer.Dispose();
        ui.Dispose();
        base.OnUnload();
    }
}
