using System.Numerics;
using ImGuiNET;

namespace Banandium;

public sealed partial class Viewer
{
    private SceneObject? inspectedObject;
    private SceneObject? highlightedObject;
    private bool showConditions;
    private bool conditionalOutlinesVisible = true;

    private void AttachModelConditions()
    {
        if (scene == null || models == null) return;
        for (int i = 0; i < models.Instances.Count; i++)
        {
            ModelInstance instance = models.Instances[i];
            SceneObject item = scene.Conditions.Register(instance.Object.Fields);
            item.HasModel = true;
            for (int v = 0; v < instance.Mesh.Vertices.Length; v += 12)
            {
                var point = new Vector3(instance.Mesh.Vertices[v], instance.Mesh.Vertices[v + 1], instance.Mesh.Vertices[v + 2]);
                item.Include(Vector3.Transform(point, instance.Transform));
            }
            models.Instances[i] = instance with { Object = item };
        }
        scene.Conditions.Apply();
    }

    private void RefreshConditions()
    {
        if (scene == null) return;
        scene.Conditions.Apply();
        List<SceneLayer> layers = SceneAreas.Build(scene.Areas, scene.Conditions);
        foreach (SceneLayer layer in layers)
        {
            foreach (SceneLayer previous in scene.AreaLayers)
            {
                if (layer.Name != previous.Name) continue;
                layer.Mode = previous.Mode;
                layer.Shaded = previous.Shaded;
                break;
            }
        }
        scene.AreaLayers = layers;
        renderer.Upload(scene);
    }

    private void DrawConditionMenu()
    {
        if (scene == null || !ImGui.BeginMenu("Conditional objects")) return;
        if (ImGui.MenuItem("Show all", "", !scene.Conditions.UnconditionalOnly))
        {
            scene.Conditions.UnconditionalOnly = false;
            RefreshConditions();
        }
        if (ImGui.MenuItem("Unconditional only", "", scene.Conditions.UnconditionalOnly))
        {
            scene.Conditions.UnconditionalOnly = true;
            if (highlightedObject?.IsConditional == true) highlightedObject = null;
            RefreshConditions();
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Conditions inspector")) showConditions = true;
        if (ImGui.MenuItem("Clear highlight", "", false, highlightedObject != null)) highlightedObject = null;
        ImGui.EndMenu();
    }

    private void InspectObject(SceneObject item)
    {
        selectedObject = item;
        inspectedObject = item;
        highlightedObject = item;
        showConditions = true;
        ReleaseMouse();
    }

    private void DrawConditionWindows()
    {
        if (scene == null) return;
        if (showConditions)
        {
            ImGui.SetNextWindowPos(new Vector2(20, 55), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(520, 570), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Conditions", ref showConditions)) DrawConditions();
            ImGui.End();
        }
    }

    private void DrawConditions()
    {
        if (scene == null) return;
        if (inspectedObject == null)
        {
            ImGui.TextWrapped("Click an object, or choose a placement below");
            foreach (SceneObject item in scene.Conditions.Objects.Values)
            {
                if (!item.IsConditional) continue;
                ImGui.PushID(item.Key);
                if (ImGui.Selectable(item.Name + " / " + item.Fields.GetValueOrDefault("Id"))) InspectObject(item);
                ImGui.PopID();
            }
            return;
        }
        SceneObject selected = inspectedObject;
        ImGui.TextUnformatted(selected.Name);
        ImGui.TextWrapped("Id: " + selected.Fields.GetValueOrDefault("Id", "Not available"));
        ImGui.TextWrapped("Preview: " + (selected.Condition == ConditionState.Unconditional ? "No placement condition" : selected.Condition.ToString()));
        if (selected.Hidden) ImGui.TextDisabled("Hidden manually");
        if (selected.Filtered) ImGui.TextDisabled("Excluded by Unconditional only");
        ImGui.BeginDisabled(!selected.HasBounds && !selected.HasPosition);
        if (ImGui.Button("Focus")) FocusObject(selected);
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("All conditional placements")) inspectedObject = null;
        ImGui.Separator();
        if (selected.Name.StartsWith("ChangeLevelArea", StringComparison.Ordinal))
        {
            string destination = selected.Fields.GetValueOrDefault("StageName") as string ?? "";
            ImGui.TextWrapped("Destination scene: " + (destination == "" ? "Not specified in this placement" : destination));
            ImGui.TextWrapped("IntervalIndex: " + FormatAreaField(selected.Fields.GetValueOrDefault("IntervalIndex")));
        }
        if (ImGui.CollapsingHeader("Placement fields"))
        {
            foreach (var field in selected.Fields)
                if (field.Key != "Links") ImGui.TextWrapped(field.Key + ": " + FormatAreaField(field.Value));
        }
    }

    private SceneObject? SelectedSwitch()
    {
        return selectedObject?.ControllingSwitch;
    }

    private void DrawSelectedSwitch()
    {
        SceneObject? target = SelectedSwitch();
        bool enabled = scene != null && target != null && scene.Conditions.Switches.ContainsKey(target.Key);
        bool on = enabled && scene!.Conditions.Switches[target!.Key] == SwitchValue.On;
        ImGui.BeginDisabled(!enabled);
        if (ImGui.Checkbox("Switch on", ref on)) SetSelectedSwitch(on);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (target != null) ImGui.SetTooltip(target.Name + " / " + target.Fields.GetValueOrDefault("Id"));
            else ImGui.SetTooltip("Select an object with one controlling switch");
        }
    }

    private void SetSelectedSwitch(bool on)
    {
        SceneObject? target = SelectedSwitch();
        if (scene == null || target == null) return;
        scene.Conditions.Switches[target.Key] = on ? SwitchValue.On : SwitchValue.Off;
        RefreshConditions();
    }

    private void FocusObject(SceneObject item)
    {
        if (!item.HasBounds && !item.HasPosition) return;
        Vector3 center = item.HasBounds ? (item.Minimum + item.Maximum) * .5f : item.Position;
        float distance = item.HasBounds ? MathF.Max(15, (item.Maximum - item.Minimum).Length()) : 30;
        cameraPosition = center - GetCameraDirection() * distance;
        highlightedObject = item;
    }

    private bool ShowConditionOutline(SceneObject item)
    {
        if (scene == null) return false;
        if (item.IsConditional && !conditionalOutlinesVisible) return false;
        if (item == highlightedObject) return item.HasBounds || item.HasPosition;
        if (!item.IsConditional || item.Hidden || item.Filtered) return false;
        if (item.Area != null)
        {
            foreach (SceneLayer layer in scene.AreaLayers)
                if (layer.Name == item.Area.Name) return layer.Mode != DisplayMode.Disabled;
            return false;
        }
        return (item.HasModel && modelsVisible) || (item.HasCollision && scene.ActorCollision.Mode != DisplayMode.Disabled);
    }

    private void DrawConditionOutlines(Matrix4x4 matrix)
    {
        if (scene == null) return;
        var draw = ImGui.GetBackgroundDrawList();
        foreach (SceneObject item in scene.Conditions.Objects.Values)
        {
            if (!ShowConditionOutline(item)) continue;
            Vector3 color = scene.ActorCollision.Color;
            if (item.Area != null)
                foreach (SceneLayer layer in scene.AreaLayers) if (layer.Name == item.Area.Name) color = layer.Color;
            if (item == highlightedObject) color = new Vector3(1, 1, .25f);
            float alpha = item.Inactive ? .5f : 1;
            uint packed = ImGui.ColorConvertFloat4ToU32(new Vector4(color, alpha));
            Vector3 low = item.HasBounds ? item.Minimum : item.Position - new Vector3(2);
            Vector3 high = item.HasBounds ? item.Maximum : item.Position + new Vector3(2);
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++) corners[i] = new Vector3((i & 1) == 0 ? low.X : high.X, (i & 2) == 0 ? low.Y : high.Y, (i & 4) == 0 ? low.Z : high.Z);
            for (int i = 0; i < 8; i++)
                for (int axis = 1; axis <= 4; axis *= 2)
                    if ((i & axis) == 0) DrawOutlineEdge(draw, corners[i], corners[i | axis], matrix, packed, item.IsConditional);
            if (item == highlightedObject && Project((low + high) * .5f, matrix, out Vector2 screen))
                draw.AddText(screen, packed, item.Name + (item.HasBounds ? " [bounds]" : " [placement]"));
        }
    }

    private bool Project(Vector3 point, Matrix4x4 matrix, out Vector2 screen)
    {
        Vector4 clip = Vector4.Transform(new Vector4(point, 1), matrix);
        screen = Vector2.Zero;
        if (clip.W <= .05f) return false;
        screen = new Vector2((clip.X / clip.W + 1) * ClientSize.X * .5f, (1 - clip.Y / clip.W) * ClientSize.Y * .5f);
        return true;
    }

    private void DrawOutlineEdge(ImDrawListPtr draw, Vector3 a, Vector3 b, Matrix4x4 matrix, uint color, bool dashed)
    {
        Vector4 first = Vector4.Transform(new Vector4(a, 1), matrix);
        Vector4 last = Vector4.Transform(new Vector4(b, 1), matrix);
        float low = 0, high = 1;
        for (int plane = 0; plane < 6; plane++)
        {
            float x = ClipDistance(first, plane), y = ClipDistance(last, plane);
            if (x < 0 && y < 0) return;
            if (x < 0) low = MathF.Max(low, x / (x - y));
            if (y < 0) high = MathF.Min(high, x / (x - y));
        }
        if (low > high) return;
        first = Vector4.Lerp(Vector4.Transform(new Vector4(a, 1), matrix), last, low);
        last = Vector4.Lerp(Vector4.Transform(new Vector4(a, 1), matrix), last, high);
        if (first.W <= 0 || last.W <= 0) return;
        Vector2 start = new Vector2((first.X / first.W + 1) * ClientSize.X * .5f, (1 - first.Y / first.W) * ClientSize.Y * .5f);
        Vector2 end = new Vector2((last.X / last.W + 1) * ClientSize.X * .5f, (1 - last.Y / last.W) * ClientSize.Y * .5f);
        if (!dashed) { draw.AddLine(start, end, color, 2); return; }
        float length = Vector2.Distance(start, end);
        if (length < .01f) return;
        Vector2 direction = (end - start) / length;
        for (float step = 0; step < length; step += 12)
            draw.AddLine(start + direction * step, start + direction * MathF.Min(step + 7, length), color, 1.5f);
    }

    private static float ClipDistance(Vector4 point, int plane)
    {
        switch (plane)
        {
            case 0: return point.W + point.X;
            case 1: return point.W - point.X;
            case 2: return point.W + point.Y;
            case 3: return point.W - point.Y;
            case 4: return point.W + point.Z;
            default: return point.W - point.Z;
        }
    }

    private static float HitBounds(Vector3 low, Vector3 high, Vector3 origin, Vector3 direction)
    {
        float near = 0, far = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            if (direction[axis] == 0)
            {
                if (origin[axis] < low[axis] || origin[axis] > high[axis]) return float.PositiveInfinity;
                continue;
            }
            float a = (low[axis] - origin[axis]) / direction[axis];
            float b = (high[axis] - origin[axis]) / direction[axis];
            near = MathF.Max(near, MathF.Min(a, b));
            far = MathF.Min(far, MathF.Max(a, b));
            if (near > far) return float.PositiveInfinity;
        }
        return near;
    }
}
