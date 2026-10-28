using System.Numerics;
using ImGuiNET;

namespace Banandium;

public sealed partial class Viewer
{
    private bool triggersVisible;
    private DisplayMode triggerMode = DisplayMode.SemiTransparent;
    private bool triggersShaded = true;
    private Dictionary<string, bool> triggerTypes = new Dictionary<string, bool>(StringComparer.Ordinal);

    private void DrawTriggerMenu()
    {
        if (scene == null || !ImGui.BeginMenu("Triggers")) return;
        if (ImGui.MenuItem("Show triggers", "", triggersVisible)) triggersVisible = !triggersVisible;
        if (ImGui.MenuItem("Gameplay only"))
        {
            foreach (string name in scene.Triggers.Keys) triggerTypes.Remove(name);
            triggersVisible = true;
        }
        if (ImGui.MenuItem("Enable all types"))
        {
            foreach (string name in scene.Triggers.Keys) triggerTypes[name] = true;
            triggersVisible = true;
        }
        if (ImGui.MenuItem("Disable all types"))
            foreach (string name in scene.Triggers.Keys) triggerTypes[name] = false;
        DrawDisplayModeMenu("Display", ref triggerMode, ref triggersShaded);
        ImGui.Separator();
        foreach (string name in scene.Triggers.Keys)
        {
            bool visible = IsTriggerTypeVisible(name);
            if (ImGui.MenuItem(name, "", visible)) triggerTypes[name] = !visible;
        }
        ImGui.EndMenu();
    }

    private bool IsTriggerTypeVisible(string name)
    {
        if (triggerTypes.TryGetValue(name, out bool visible)) return visible;
        if (scene == null || !scene.Triggers.TryGetValue(name, out List<SceneTrigger>? group)) return false;
        foreach (SceneTrigger trigger in group)
            if (trigger.DefaultVisible) return true;
        return false;
    }

    private bool ShowTrigger(SceneTrigger trigger)
    {
        return triggersVisible && triggerMode != DisplayMode.Disabled && IsTriggerTypeVisible(trigger.Object.Name) &&
            !trigger.Object.Hidden && !trigger.Object.Filtered;
    }

    private void UpdateTriggerLayers()
    {
        if (scene == null) return;
        foreach (var group in scene.Triggers.Values)
        {
            foreach (SceneTrigger trigger in group)
            {
                if (trigger.Layer == null) continue;
                trigger.Layer.Mode = ShowTrigger(trigger) && trigger.Object.Visible ? triggerMode : DisplayMode.Disabled;
                trigger.Layer.Shaded = triggersShaded;
            }
        }
    }

    private Matrix4x4 CameraMatrix()
    {
        return Matrix4x4.CreateLookAt(cameraPosition, cameraPosition + GetCameraDirection(), Vector3.UnitY) * CreateProjection();
    }

    private bool ProjectTrigger(Vector3 point, Matrix4x4 matrix, out Vector2 screen)
    {
        screen = Vector2.Zero;
        Vector4 clip = Vector4.Transform(new Vector4(point, 1), matrix);
        if (clip.W <= 0 || MathF.Abs(clip.X) > clip.W || MathF.Abs(clip.Y) > clip.W || MathF.Abs(clip.Z) > clip.W) return false;
        return Project(point, matrix, out screen);
    }

    private SceneObject? PickTrigger(Vector2 mouse)
    {
        if (scene == null || !triggersVisible) return null;
        SceneObject? found = null;
        float nearest = float.PositiveInfinity;
        Matrix4x4 matrix = CameraMatrix();
        foreach (var group in scene.Triggers.Values)
        {
            foreach (SceneTrigger trigger in group)
            {
                if (trigger.Layer != null) continue;
                if (!ShowTrigger(trigger) || !ProjectTrigger(trigger.Center, matrix, out Vector2 screen)) continue;
                if (Vector2.DistanceSquared(mouse, screen) > 100) continue;
                float distance = Vector3.DistanceSquared(cameraPosition, trigger.Center);
                if (distance < nearest) { nearest = distance; found = trigger.Object; }
            }
        }
        return found;
    }

    private void DrawTriggers(Matrix4x4 matrix)
    {
        if (scene == null || !triggersVisible) return;
        var draw = ImGui.GetBackgroundDrawList();
        Vector2 mouse = ImGui.GetMousePos();
        SceneObject? hovered = ImGui.GetIO().WantCaptureMouse ? null : PickTrigger(mouse);
        foreach (var group in scene.Triggers.Values)
        {
            foreach (SceneTrigger trigger in group)
            {
                if (!ShowTrigger(trigger)) continue;
                if (trigger.Layer != null && (trigger.Object.Inactive || (trigger.Object != selectedObject && trigger.Object != highlightedObject))) continue;
                SceneObject item = trigger.Object;
                Vector3 color = new Vector3(.2f, 1, .35f);
                if (item == selectedObject || item == highlightedObject) color = new Vector3(1, 1, .25f);
                uint packed = ImGui.ColorConvertFloat4ToU32(new Vector4(color, item.Inactive ? .35f : .85f));
                if (!ProjectTrigger(trigger.Center, matrix, out Vector2 screen)) continue;
                if (trigger.Surface != null)
                    draw.AddRect(screen - new Vector2(4), screen + new Vector2(4), packed, 0, ImDrawFlags.None, 2);
                else
                {
                    draw.AddLine(screen + new Vector2(0, -6), screen + new Vector2(6, 0), packed, 2);
                    draw.AddLine(screen + new Vector2(6, 0), screen + new Vector2(0, 6), packed, 2);
                    draw.AddLine(screen + new Vector2(0, 6), screen + new Vector2(-6, 0), packed, 2);
                    draw.AddLine(screen + new Vector2(-6, 0), screen + new Vector2(0, -6), packed, 2);
                }
                if (item == hovered || item == selectedObject)
                    draw.AddText(screen + new Vector2(10, -7), packed, item.Name + (trigger.Surface == null ? " [placement marker]" : ""));
            }
        }
    }
}
