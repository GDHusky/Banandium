namespace Banandium;

public enum SwitchValue { Unknown, Off, On }
public enum ConditionState { Unconditional, Unknown, Active, Inactive }

public sealed record SceneLink(string Name, string Type, SceneObject Target);

public sealed class SceneConditions
{
    public Dictionary<string, SceneObject> Objects { get; } = new Dictionary<string, SceneObject>();
    public Dictionary<string, SwitchValue> Switches { get; } = new Dictionary<string, SwitchValue>();
    private readonly HashSet<string> expanded = new HashSet<string>();
    public bool UnconditionalOnly;

    public SceneConditions() { }

    public SceneConditions(Dictionary<string, object?> scenario)
    {
        var roots = new List<Dictionary<string, object?>>();
        foreach (var section in scenario)
        {
            if (section.Value is not List<object?> list) continue;
            foreach (object? entry in list)
            {
                if (entry is not Dictionary<string, object?> fields || !fields.ContainsKey("ObjectName")) continue;
                roots.Add(fields);
                string key = Key(fields);
                if (key != "")
                {
                    Objects.TryAdd(key, new SceneObject((string)fields["ObjectName"]!, fields));
                    if (section.Key == "AreaList") Objects[key].IsAreaPlacement = true;
                }
            }
        }
        foreach (var fields in roots) Register(fields);
    }

    public static string Key(Dictionary<string, object?> fields)
    {
        string id = fields.GetValueOrDefault("Id") as string ?? "";
        if (id == "") return "";
        return (fields.GetValueOrDefault("PlacementFileName") as string ?? "") + "/" + id;
    }

    public SceneObject Register(Dictionary<string, object?> fields)
    {
        string key = Key(fields);
        SceneObject item;
        if (key != "" && Objects.TryGetValue(key, out SceneObject? existing))
        {
            item = existing;
            fields = item.Fields;
        }
        else
        {
            item = new SceneObject(fields.GetValueOrDefault("ObjectName") as string ?? "Object", fields);
            if (key != "") Objects.Add(key, item);
        }
        if (key != "" && !expanded.Add(key)) return item;
        if (item.IsSwitch && key != "") Switches.TryAdd(key, SwitchValue.On);
        if (fields.GetValueOrDefault("Links") is Dictionary<string, object?> links)
        {
            foreach (var link in links)
            {
                if (link.Value is not List<object?> targets) continue;
                foreach (object? value in targets)
                {
                    if (value is not Dictionary<string, object?> target) continue;
                    SceneObject linked = Register(target);
                    string type = target.GetValueOrDefault("LinkType") as string ?? "Unknown";
                    var relation = new SceneLink(link.Key, type, linked);
                    item.Links.Add(relation);
                    if (IsConditionLink(relation)) item.HasConditions = true;
                    linked.Incoming.Add(new SceneLink(link.Key, type, item));
                }
            }
        }
        return item;
    }

    public ConditionState Evaluate(SceneObject item)
    {
        if (!item.HasConditions) return ConditionState.Unconditional;
        if (item.HasConditionFields) return ConditionState.Unknown;
        SceneLink? gate = null;
        foreach (SceneLink link in item.Links)
        {
            if (!IsConditionLink(link)) continue;
            if (gate != null || link.Type != "Read") return ConditionState.Unknown;
            gate = link;
        }
        if (gate == null) return ConditionState.Unknown;
        SwitchValue value = Switches.GetValueOrDefault(gate.Target.Key);
        if (value == SwitchValue.Unknown) return ConditionState.Unknown;
        //switch listener reference: https://github.com/MonsterDruide1/OdysseyDecomp
        //kill-off does not reconstruct event history, and combined listeners can depend on order
        if (gate.Name == "SwitchAppear" || gate.Name == "SwitchSaveAppear") return value == SwitchValue.On ? ConditionState.Active : ConditionState.Inactive;
        if ((gate.Name == "SwitchKill" || gate.Name == "SwitchSaveKill") && value == SwitchValue.On) return ConditionState.Inactive;
        return ConditionState.Unknown;
    }

    public static bool IsConditionLink(SceneLink link)
    {
        return link.Target.IsSwitch && link.Type != "Write";
    }

    public void Apply()
    {
        foreach (SceneObject item in Objects.Values)
        {
            item.Condition = Evaluate(item);
            item.Filtered = UnconditionalOnly && item.IsConditional;
            item.Inactive = item.Condition == ConditionState.Inactive;
        }
    }

}
