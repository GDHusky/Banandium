using System.Numerics;

namespace Banandium;

public sealed class SceneTrigger
{
    public SceneObject Object { get; }
    public string Category { get; }
    public bool DefaultVisible { get; init; }
    public MeshData? Surface { get; }
    public SceneLayer? Layer { get; }
    public Vector3 Center { get; }
    public Vector3 Minimum { get; }
    public Vector3 Maximum { get; }

    public SceneTrigger(SceneObject item, string category, bool area)
    {
        Object = item;
        Category = category;
        Center = item.Position;
        Minimum = Maximum = Center;
        string model = item.Fields.GetValueOrDefault("ModelName") as string ?? "";
        if (!area || (model != "AreaCubeCenter" && model != "AreaCylinder" && model != "AreaSphere")) return;
        Matrix4x4 placement = Dump.ReadTransform(item.Fields);
        if (placement.GetDeterminant() == 0) return;
        Surface = new AreaCutout(model, placement).CreateSurface();
        Minimum = new Vector3(float.PositiveInfinity);
        Maximum = new Vector3(float.NegativeInfinity);
        foreach (Vector3 vertex in Surface.Vertices)
        {
            Minimum = Vector3.Min(Minimum, vertex);
            Maximum = Vector3.Max(Maximum, vertex);
        }
        Center = (Minimum + Maximum) * .5f;
        if (item.Area == null)
        {
            Layer = new SceneLayer(item.Name, new Vector3(.2f, 1, .35f));
            Layer.Mesh.Vertices.AddRange(Surface.Vertices);
            Layer.Mesh.Parts.Add(new MeshPart(Layer.Mesh, 0, Layer.Mesh.Vertices.Count, item));
        }
    }

}

public static class SceneTriggers
{
    private static readonly HashSet<string> ActorTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "DemoStartObj", "DemoStartObjPaulineRock", "BattleStageAppearDemoDirector",
        "BioWaterSurfaceDemoDirector", "BurgerTowerDemoDirector", "HarmoneelDemoDirector",
        "HelpPaulineEventDirector", "SoundLevelLiveMissionDirector", "NpcDKFamilyEventWatcher",
        "DoorBattleRush", "DoorBonusStage", "DoorBonusStageIsland", "DoorBossRush",
        "DoorChallengeCourse", "DoorOneWay", "DoorOneWaySide", "DoorSun",
        "SoundLevelAltarDoor", "SoundLevelDoorHidden", "SoundLevelDoorSmallHall",
        "SoundLevelDoorVip", "SoundLevelKeyMoveDoor", "LakeLevelDungeonGate",
        "Sinkhole", "SinkholeGate", "SinkholeGenerator", "BossSinkholeGenerator",
        "MissionClearReader", "MissionClearWriter", "MissionOpenWriter", "MissionPoint",
        "StoneTabletMissionOpener", "GoalValve", "GameProgressSwitch",
        "StageSwitch", "StageSwitchSave", "StageSwitchSaveSetter", "SwitchAnd", "SwitchOr",
        "SwitchMaterial", "SyncSwitch", "SyncSwitchDirector", "BioThornSwitch", "BurgerSwitch",
        "CannonBarrelSwitch", "IceMakerSwitch", "MinecartRailSwitch", "PunchingBallSwitch",
        "SunSwitch", "TankSwitch"
    };

    private static readonly HashSet<string> GameplayAreaTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "ChangeLevelArea", "SwitchOnArea", "SwitchOnPlayerGroundInArea",
        "SwitchKeepOnArea", "SwitchKeepOnPlayerGroundInArea", "SwitchKeepOnIgnoreOffAreaTarget",
        "MissionClearWriterArea", "MinecartSwitchArea", "MainRouteSwitchingArea",
        "DKFamilyEventArea_AreaCubeBase_Area", "NpcExTalkArea", "TalkPaulineArea",
        "StyleTutorialTalkArea", "SkydivingGoalArea", "SkydivingBossArea", "YocchiGoalArea",
        "TimerBreakerArea", "VoidAttackTriggerArea", "VoidBattleEndEraseArea",
        "VoidBattleStayArea", "VoidChaseProgressArea", "VoxelPartsBrokenRateCheckerArea",
        "VoxelNpcHideGameArea_AreaCubeBase"
    };

    public static SortedDictionary<string, List<SceneTrigger>> Read(SceneConditions conditions)
    {
        var result = new SortedDictionary<string, List<SceneTrigger>>(StringComparer.Ordinal);
        foreach (SceneObject item in conditions.Objects.Values)
        {
            if (!item.HasPosition) continue;
            string type = item.Fields.GetValueOrDefault("ParameterClassName") as string ?? item.Name;
            string model = item.Fields.GetValueOrDefault("ModelName") as string ?? "";
            bool area = item.IsAreaPlacement || model == "AreaCubeCenter" || model == "AreaCylinder" || model == "AreaSphere";
            bool writesSwitch = false;
            foreach (SceneLink link in item.Links)
                if (link.Type == "Write" && link.Target.IsSwitch) writesSwitch = true;
            if (!area && !writesSwitch && !ActorTypes.Contains(type)) continue;
            string category = area ? "Area" : "Actor";
            if (type.Contains("Switch", StringComparison.Ordinal) || writesSwitch) category = "Switch";
            if (type.Contains("Mission", StringComparison.Ordinal)) category = "Mission";
            if (type.Contains("Demo", StringComparison.Ordinal) || type.Contains("Event", StringComparison.Ordinal)) category = "Cutscene / event";
            if (type == "ChangeLevelArea") category = "Level transition";
            if (type.Contains("Door", StringComparison.Ordinal) || type == "LakeLevelDungeonGate") category = "Door";
            if (type.Contains("Sinkhole", StringComparison.Ordinal)) category = "Sinkhole";
            var trigger = new SceneTrigger(item, category, area)
            {
                DefaultVisible = ActorTypes.Contains(type) || GameplayAreaTypes.Contains(type)
            };
            item.Trigger = trigger;
            if (!result.TryGetValue(item.Name, out List<SceneTrigger>? list))
            {
                list = new List<SceneTrigger>();
                result.Add(item.Name, list);
            }
            list.Add(trigger);
        }
        return result;
    }
}
