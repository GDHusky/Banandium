using System.Numerics;
using ImGuiNET;

namespace Banandium;

public sealed partial class Viewer
{
    private sealed class HierarchyGroup
    {
        public string Name;
        public List<SceneObject> Objects = new List<SceneObject>();
        public List<SceneObject> Matches = new List<SceneObject>();

        public HierarchyGroup(string name) { Name = name; }
    }

    private readonly HashSet<SceneObject> visibilityOverrides = new HashSet<SceneObject>();
    private bool sidebarVisible = true;
    private string hierarchyFilter = "";
    private bool revealSelection;
    private readonly List<SceneObject> hierarchyObjects = new List<SceneObject>();
    private readonly SortedDictionary<string, HierarchyGroup> hierarchyGroups = new SortedDictionary<string, HierarchyGroup>(StringComparer.Ordinal);
    private Vector2 sceneViewPosition;
    private Vector2 sceneViewSize = new Vector2(1440, 900);

    private void UpdateSceneView()
    {
        float top = ImGui.GetFrameHeight();
        float width = sidebarVisible ? MathF.Min(360, ClientSize.X * .45f) : 0;
        sceneViewPosition = new Vector2(width, top);
        sceneViewSize = new Vector2(MathF.Max(1, ClientSize.X - width), MathF.Max(1, ClientSize.Y - top));
    }

    private bool IsOverScene(Vector2 mouse)
    {
        return mouse.X >= sceneViewPosition.X && mouse.Y >= sceneViewPosition.Y &&
            mouse.X < sceneViewPosition.X + sceneViewSize.X && mouse.Y < sceneViewPosition.Y + sceneViewSize.Y;
    }

    private Vector2 SceneScreenPoint(Vector4 clip)
    {
        return sceneViewPosition + new Vector2((clip.X / clip.W + 1) * sceneViewSize.X * .5f,
            (1 - clip.Y / clip.W) * sceneViewSize.Y * .5f);
    }

    private void RebuildHierarchy()
    {
        hierarchyObjects.Clear();
        hierarchyGroups.Clear();
        if (scene == null) return;
        var included = new HashSet<SceneObject>();
        foreach (SceneObject item in scene.Conditions.Objects.Values) AddHierarchyObject(item, included);
        foreach (MeshPart part in scene.Collision.Parts) AddHierarchyObject(part.Object, included, "Static collision");
        foreach (MeshPart part in scene.ActorCollision.Mesh.Parts) AddHierarchyObject(part.Object, included);
        if (models != null)
            foreach (ModelInstance instance in models.Instances) AddHierarchyObject(instance.Object, included);
        foreach (HierarchyGroup group in hierarchyGroups.Values) group.Objects.Sort(CompareHierarchyObjects);
        FilterHierarchy();
    }

    private void AddHierarchyObject(SceneObject item, HashSet<SceneObject> included, string? groupName = null)
    {
        if (!included.Add(item)) return;
        hierarchyObjects.Add(item);
        groupName ??= item.Fields.GetValueOrDefault("ParameterClassName") as string ?? item.Name;
        if (!hierarchyGroups.TryGetValue(groupName, out HierarchyGroup? group))
        {
            group = new HierarchyGroup(groupName);
            hierarchyGroups.Add(groupName, group);
        }
        group.Objects.Add(item);
    }

    private static int CompareHierarchyObjects(SceneObject first, SceneObject second)
    {
        int result = StringComparer.Ordinal.Compare(first.Name, second.Name);
        return result != 0 ? result : StringComparer.Ordinal.Compare(first.Key, second.Key);
    }

    private void FilterHierarchy()
    {
        foreach (HierarchyGroup group in hierarchyGroups.Values)
        {
            group.Matches.Clear();
            foreach (SceneObject item in group.Objects)
            {
                string file = item.Fields.GetValueOrDefault("PlacementFileName") as string ?? "";
                if (group.Name.Contains(hierarchyFilter, StringComparison.OrdinalIgnoreCase) ||
                    item.Name.Contains(hierarchyFilter, StringComparison.OrdinalIgnoreCase) ||
                    item.Key.Contains(hierarchyFilter, StringComparison.OrdinalIgnoreCase) ||
                    file.Contains(hierarchyFilter, StringComparison.OrdinalIgnoreCase)) group.Matches.Add(item);
            }
        }
    }

    private void SelectSceneObject(SceneObject? item, bool reveal = true)
    {
        selectedObject = item;
        highlightedObject = item;
        revealSelection = reveal && item != null;
        if (revealSelection && hierarchyFilter != "")
        {
            hierarchyFilter = "";
            FilterHierarchy();
        }
    }

    private void SetObjectHidden(SceneObject item, bool hidden)
    {
        if (hidden) HideObject(item);
        else
        {
            item.Hidden = false;
            hiddenObjects.Remove(item);
            if (item == selectedObject) highlightedObject = item;
            if (scene != null) renderer.Upload(scene);
        }
    }

    private void SetVisibilityOverride(SceneObject item, bool visible)
    {
        item.ForceVisible = visible;
        if (visible)
        {
            visibilityOverrides.Add(item);
            item.Hidden = false;
            hiddenObjects.Remove(item);
            if (item.Area == null && item.Trigger?.Surface == null &&
                item.Fields.GetValueOrDefault("ModelName") is string modelName && modelName != "") LoadModels();
        }
        else visibilityOverrides.Remove(item);
        if (scene != null) renderer.Upload(scene);
    }

    private void DrawSidebar()
    {
        if (!sidebarVisible) return;
        float top = sceneViewPosition.Y;
        float height = sceneViewSize.Y;
        float hierarchyHeight = height * .55f;
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings;
        ImGui.SetNextWindowPos(new Vector2(0, top));
        ImGui.SetNextWindowSize(new Vector2(sceneViewPosition.X, hierarchyHeight));
        if (ImGui.Begin("Hierarchy", flags)) DrawHierarchy();
        ImGui.End();
        ImGui.SetNextWindowPos(new Vector2(0, top + hierarchyHeight));
        ImGui.SetNextWindowSize(new Vector2(sceneViewPosition.X, height - hierarchyHeight));
        if (ImGui.Begin("Inspector", flags)) DrawInspector();
        ImGui.End();
    }

    private unsafe void DrawHierarchy()
    {
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##hierarchy-filter", "Search objects...", ref hierarchyFilter, 160)) FilterHierarchy();
        if (scene == null)
        {
            ImGui.TextDisabled("No scene loaded");
            return;
        }
        ImGui.BeginChild("hierarchy-list", Vector2.Zero);
        foreach (HierarchyGroup group in hierarchyGroups.Values)
        {
            if (group.Matches.Count == 0) continue;
            ImGui.PushID(group.Name);
            if (hierarchyFilter != "" || (revealSelection && selectedObject != null && group.Matches.Contains(selectedObject)))
                ImGui.SetNextItemOpen(true);
            if (ImGui.TreeNode(group.Name + " (" + group.Matches.Count + ")"))
            {
                var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                clipper.Begin(group.Matches.Count, ImGui.GetFrameHeightWithSpacing());
                if (revealSelection && selectedObject != null)
                {
                    int selectedIndex = group.Matches.IndexOf(selectedObject);
                    if (selectedIndex >= 0) clipper.IncludeItemByIndex(selectedIndex);
                }
                while (clipper.Step())
                {
                    for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                    {
                        SceneObject item = group.Matches[i];
                        ImGui.PushID(i);
                        bool shown = !item.Hidden;
                        if (ImGui.Checkbox("##visible", ref shown)) SetObjectHidden(item, !shown);
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip(shown ? "Hide object" : "Unhide object");
                        ImGui.SameLine();
                        string label = item.Name;
                        if (!item.Visible) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
                        bool clicked = ImGui.Selectable(label, selectedObject == item, ImGuiSelectableFlags.AllowDoubleClick);
                        if (!item.Visible) ImGui.PopStyleColor();
                        if (clicked)
                        {
                            SelectSceneObject(item, false);
                            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) FocusObject(item);
                        }
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip(label);
                        if (revealSelection && selectedObject == item) ImGui.SetScrollHereY();
                        ImGui.PopID();
                    }
                }
                clipper.Destroy();
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        ImGui.EndChild();
        revealSelection = false;
    }

    private void DrawInspector()
    {
        SceneObject? item = selectedObject;
        if (item == null)
        {
            ImGui.TextDisabled("Select an object");
            return;
        }
        ImGui.TextWrapped(item.Name);
        string type = item.Fields.GetValueOrDefault("ParameterClassName") as string ?? item.Name;
        ImGui.TextWrapped("Type: " + type);
        if (item.Fields.TryGetValue("Id", out object? id)) ImGui.TextWrapped("Id: " + id);
        ImGui.BeginDisabled(!item.HasPosition && !item.HasBounds);
        if (ImGui.Button("Focus")) FocusObject(item);
        ImGui.EndDisabled();
        ImGui.SameLine();
        bool hidden = item.Hidden;
        if (ImGui.Checkbox("Hidden", ref hidden)) SetObjectHidden(item, hidden);
        bool forceVisible = item.ForceVisible;
        if (ImGui.Checkbox("Show in scene", ref forceVisible)) SetVisibilityOverride(item, forceVisible);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Show this object's model or filled volume even when its layer is hidden");
        if (item.Trigger != null && ImGui.Button("Hide all of type"))
        {
            triggerTypes[item.Name] = false;
            if (highlightedObject?.Name == item.Name) highlightedObject = null;
        }
        if (item.ControllingSwitch != null) DrawSelectedSwitch();
        if (item.HasConditions) ImGui.TextUnformatted("Preview: " + item.Condition);
        if (item.Filtered) ImGui.TextDisabled("Excluded by Unconditional only");
        if (item.Trigger != null && item.Trigger.Surface == null) ImGui.TextWrapped("Placement marker; activation bounds unknown");
        ImGui.Separator();
        foreach (string field in new[] { "Translate", "Rotate", "Scale" })
        {
            if (!item.Fields.TryGetValue(field, out object? value)) continue;
            string label = field == "Translate" ? "Position" : field == "Rotate" ? "Rotation" : "Scale";
            ImGui.TextWrapped(label + ": " + FormatAreaField(value));
        }
        if (ImGui.CollapsingHeader("Placement fields"))
        {
            foreach (var field in item.Fields)
            {
                if (field.Key == "Links" || field.Key == "Translate" || field.Key == "Rotate" || field.Key == "Scale") continue;
                ImGui.TextWrapped(field.Key + ": " + FormatAreaField(field.Value));
            }
        }
        DrawInspectorLinks("Links", item.Links);
        DrawInspectorLinks("Incoming links", item.Incoming);
    }

    private void DrawInspectorLinks(string title, List<SceneLink> links)
    {
        if (links.Count == 0 || !ImGui.CollapsingHeader(title)) return;
        ImGui.PushID(title);
        for (int i = 0; i < links.Count; i++)
        {
            SceneLink link = links[i];
            ImGui.PushID(i);
            ImGui.TextWrapped(link.Name + " (" + link.Type + ")");
            string label = link.Target.Name + " / " + link.Target.Fields.GetValueOrDefault("Id");
            if (ImGui.Selectable(label)) SelectSceneObject(link.Target);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(label);
            ImGui.PopID();
        }
        ImGui.PopID();
    }
}
