using System.Text.Json;

namespace Banandium;

public sealed class ViewerSettings
{
    public string RomfsPath { get; set; } = "";
    public bool TexturedModels { get; set; } = true;
    public DisplayMode CollisionMode { get; set; } = DisplayMode.Disabled;
    public bool CollisionShaded { get; set; } = true;
    public bool ConditionalOutlines { get; set; }
    public bool TriggersVisible { get; set; }
    public DisplayMode TriggerMode { get; set; } = DisplayMode.SemiTransparent;
    public bool TriggersShaded { get; set; } = true;
    public Dictionary<string, bool> TriggerTypes { get; set; } = new Dictionary<string, bool>();
    public bool UnconditionalOnly { get; set; }
    public float CameraSpeed { get; set; } = 30;
    public Dictionary<string, LayerSettings> Layers { get; set; } = new Dictionary<string, LayerSettings>();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Banandium", "settings.json");

    public static ViewerSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(FilePath)) ?? new ViewerSettings();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        return new ViewerSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath + ".tmp", json);
            File.Move(FilePath + ".tmp", FilePath, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class LayerSettings
{
    public DisplayMode Mode { get; set; } = DisplayMode.Disabled;
    public bool Shaded { get; set; }
}

public sealed partial class Viewer
{
    private readonly ViewerSettings settings = ViewerSettings.Load();

    private void RestoreSettings()
    {
        modelsVisible = settings.TexturedModels;
        collisionMode = settings.CollisionMode;
        collisionShaded = settings.CollisionShaded;
        conditionalOutlinesVisible = settings.ConditionalOutlines;
        triggersVisible = settings.TriggersVisible;
        triggerMode = settings.TriggerMode;
        triggersShaded = settings.TriggersShaded;
        triggerTypes = settings.TriggerTypes;
        cameraSpeed = float.IsFinite(settings.CameraSpeed) ? Math.Clamp(settings.CameraSpeed, .1f, 10000) : 30;
    }

    private void RememberSceneSettings()
    {
        if (scene == null) return;
        settings.UnconditionalOnly = scene.Conditions.UnconditionalOnly;
        RememberLayerSettings(scene.ActorCollision);
        foreach (SceneLayer layer in scene.AreaLayers) RememberLayerSettings(layer);
    }

    private void RememberLayerSettings(SceneLayer layer)
    {
        settings.Layers[layer.Name] = new LayerSettings { Mode = layer.Mode, Shaded = layer.Shaded };
    }

    private void RestoreLayerSettings(SceneLayer layer)
    {
        if (!settings.Layers.TryGetValue(layer.Name, out LayerSettings? saved)) return;
        layer.Mode = saved.Mode;
        layer.Shaded = saved.Shaded;
    }

    private void SaveSettings()
    {
        RememberSceneSettings();
        settings.TexturedModels = modelsVisible;
        settings.CollisionMode = collisionMode;
        settings.CollisionShaded = collisionShaded;
        settings.ConditionalOutlines = conditionalOutlinesVisible;
        settings.TriggersVisible = triggersVisible;
        settings.TriggerMode = triggerMode;
        settings.TriggersShaded = triggersShaded;
        settings.TriggerTypes = triggerTypes;
        settings.CameraSpeed = cameraSpeed;
        settings.Save();
    }
}
