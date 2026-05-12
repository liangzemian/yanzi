using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenQuickHost;

public static class AppSettingsStore
{
    public static string SettingsPath =>
        HostAssets.ResolveDataFilePath("appsettings.local.json");

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return Normalize(new AppSettings());
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return Normalize(JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings());
        }
        catch
        {
            return Normalize(new AppSettings());
        }
    }

    public static void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.QuickPanelGlobalGroups ??= [];
        if (settings.QuickPanelGlobalGroups.Count == 0)
        {
            settings.QuickPanelGlobalGroups.Add(new QuickPanelGroupSettings
            {
                Id = "global-default",
                Name = "默认",
                Slots = settings.QuickPanelSlots.Take(12).ToList(),
                SlotItems = settings.QuickPanelSlots
                    .Take(12)
                    .Select(static slot => string.IsNullOrWhiteSpace(slot)
                        ? null
                        : new QuickPanelSlotItem { ExtensionId = slot })
                    .ToList()
            });
        }

        settings.QuickPanelContextGroups ??= [];
        if (settings.QuickPanelContextGroups.Count == 0)
        {
            settings.QuickPanelContextGroups.Add(new QuickPanelGroupSettings
            {
                Id = "context-default",
                Name = "默认"
            });
        }

        foreach (var group in settings.QuickPanelGlobalGroups.Concat(settings.QuickPanelContextGroups))
        {
            group.Id = string.IsNullOrWhiteSpace(group.Id) ? Guid.NewGuid().ToString("N") : group.Id;
            group.Name = string.IsNullOrWhiteSpace(group.Name) ? "未命名" : group.Name.Trim();
            group.ContextProcessName = group.ContextProcessName?.Trim();
            group.ContextDisplayName = group.ContextDisplayName?.Trim();
            group.Slots ??= [];
            group.SlotItems ??= [];
            while (group.Slots.Count < 12)
            {
                group.Slots.Add(null);
            }
            if (group.Slots.Count > 12)
            {
                group.Slots = group.Slots.Take(12).ToList();
            }

            if (group.SlotItems.Count == 0)
            {
                group.SlotItems = group.Slots
                    .Take(12)
                    .Select(static slot => string.IsNullOrWhiteSpace(slot)
                        ? null
                        : new QuickPanelSlotItem { ExtensionId = slot })
                    .ToList();
            }

            while (group.SlotItems.Count < 12)
            {
                group.SlotItems.Add(null);
            }

            if (group.SlotItems.Count > 12)
            {
                group.SlotItems = group.SlotItems.Take(12).ToList();
            }

            for (var index = 0; index < group.SlotItems.Count; index++)
            {
                group.SlotItems[index] = NormalizeSlotItem(group.SlotItems[index]);
            }

            group.Slots = ProjectLegacySlots(group.SlotItems);
        }

        settings.GlobalFavoriteExtensionIds ??= settings.FavoriteExtensionIds?.ToList() ?? [];
        settings.ContextFavoriteExtensionIds ??= [];
        settings.DisabledExtensionIds ??= [];
        settings.RecentlyAddedExtensionIds ??= [];
        settings.UnreadNewExtensionIds ??= [];
        settings.YarnSelect ??= new YarnSelectSettings();
        settings.YarnSelect.WhitelistedProcesses ??= [];
        settings.YarnSelect.BlacklistedProcesses ??= [];
        settings.YarnSelect.Rules ??= [];
        if (settings.YarnSelect.Rules.Count == 0)
        {
            settings.YarnSelect.Rules = YarnSelectSettings.CreateDefaultRulesFromLegacy(settings.YarnSelect);
        }

        settings.YarnSelect.Rules = settings.YarnSelect.Rules
            .Select(YarnSelectSettings.NormalizeRule)
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.TriggerKey))
            .DistinctBy(static rule => rule.TriggerKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.YarnSelect.WhitelistedProcesses = settings.YarnSelect.WhitelistedProcesses
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.YarnSelect.BlacklistedProcesses = settings.YarnSelect.BlacklistedProcesses
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];
        if (settings.RadialMenu.Pages.Count == 0)
        {
            settings.RadialMenu.Pages.Add(new RadialMenuPageSettings
            {
                Id = "default",
                Name = "默认",
                Slots = settings.RadialMenu.Slots?.ToList() ?? Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList()
            });
        }

        foreach (var page in settings.RadialMenu.Pages)
        {
            page.Id = string.IsNullOrWhiteSpace(page.Id) ? Guid.NewGuid().ToString("N") : page.Id.Trim();
            page.Name = string.IsNullOrWhiteSpace(page.Name) ? "未命名" : page.Name.Trim();
            page.Slots ??= [];
            while (page.Slots.Count < RadialMenuSettings.TotalSlotCount)
            {
                page.Slots.Add(null);
            }

            if (page.Slots.Count > RadialMenuSettings.TotalSlotCount)
            {
                page.Slots = page.Slots.Take(RadialMenuSettings.TotalSlotCount).ToList();
            }

            page.Slots = page.Slots
                .Select(static id => string.IsNullOrWhiteSpace(id) ? null : id.Trim())
                .ToList();
            page.SlotTitles ??= [];
            while (page.SlotTitles.Count < RadialMenuSettings.TotalSlotCount)
            {
                page.SlotTitles.Add(null);
            }

            if (page.SlotTitles.Count > RadialMenuSettings.TotalSlotCount)
            {
                page.SlotTitles = page.SlotTitles.Take(RadialMenuSettings.TotalSlotCount).ToList();
            }

            page.SlotTitles = page.SlotTitles
                .Select(static title => string.IsNullOrWhiteSpace(title) ? null : title.Trim())
                .ToList();
            page.ChildPageIds ??= [];
            while (page.ChildPageIds.Count < RadialMenuSettings.TotalSlotCount)
            {
                page.ChildPageIds.Add(null);
            }

            if (page.ChildPageIds.Count > RadialMenuSettings.TotalSlotCount)
            {
                page.ChildPageIds = page.ChildPageIds.Take(RadialMenuSettings.TotalSlotCount).ToList();
            }

            page.ChildPageIds = page.ChildPageIds
                .Select(static id => string.IsNullOrWhiteSpace(id) ? null : id.Trim())
                .ToList();
        }

        settings.RadialMenu.SelectedPageId = settings.RadialMenu.Pages.Any(page => page.Id.Equals(settings.RadialMenu.SelectedPageId, StringComparison.OrdinalIgnoreCase))
            ? settings.RadialMenu.SelectedPageId
            : settings.RadialMenu.Pages[0].Id;
        settings.RadialMenu.Slots = settings.RadialMenu.Pages[0].Slots.ToList();
        settings.RadialMenu.DeadZonePixels = Math.Clamp(settings.RadialMenu.DeadZonePixels, 12, 120);
        settings.RadialMenu.RadiusPixels = Math.Clamp(settings.RadialMenu.RadiusPixels, 80, 240);
        settings.RadialMenu.DragThresholdPixels = Math.Clamp(settings.RadialMenu.DragThresholdPixels, 8, 120);
        settings.YanyuRules ??= [];
        settings.YanyuRules = settings.YanyuRules
            .Select(NormalizeYanyuRule)
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.TriggerText))
            .ToList();
        settings.RecentlyAddedExtensionIds = settings.RecentlyAddedExtensionIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();
        settings.UnreadNewExtensionIds = settings.UnreadNewExtensionIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();

        if (string.IsNullOrWhiteSpace(settings.SelectedQuickPanelGlobalGroupId) ||
            settings.QuickPanelGlobalGroups.All(group => !string.Equals(group.Id, settings.SelectedQuickPanelGlobalGroupId, StringComparison.OrdinalIgnoreCase)))
        {
            settings.SelectedQuickPanelGlobalGroupId = settings.QuickPanelGlobalGroups[0].Id;
        }

        if (string.IsNullOrWhiteSpace(settings.SelectedQuickPanelContextGroupId) ||
            settings.QuickPanelContextGroups.All(group => !string.Equals(group.Id, settings.SelectedQuickPanelContextGroupId, StringComparison.OrdinalIgnoreCase)))
        {
            settings.SelectedQuickPanelContextGroupId = settings.QuickPanelContextGroups[0].Id;
        }

        if (!settings.WebDavSyncManuallyDisabled &&
            HasWebDavConfigValues(settings.WebDavServerUrl, settings.WebDavRootPath, settings.WebDavUsername))
        {
            settings.EnableWebDavSync = true;
        }

        settings.AiBaseUrl = settings.AiBaseUrl?.Trim() ?? string.Empty;
        settings.AiApiKey = settings.AiApiKey?.Trim() ?? string.Empty;
        settings.AiModel = settings.AiModel?.Trim() ?? string.Empty;
        settings.Yanm ??= new YanmSettings();
        settings.Yanm.Components ??= [];
        settings.Yanm.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        settings.Yanm.ActivationKey = YanmActivationKeys.Normalize(settings.Yanm.ActivationKey);
        settings.Yanm.HoldDelayMilliseconds = Math.Clamp(settings.Yanm.HoldDelayMilliseconds, 0, 1000);
        settings.Yanm.GridSizePixels = Math.Clamp(settings.Yanm.GridSizePixels, 5, 80);
        settings.Yanm.OverlayOpacity = Math.Clamp(settings.Yanm.OverlayOpacity, 0.05, 0.85);
        if (!settings.Yanm.HasInitializedDefaultComponents &&
            settings.Yanm.Components.Count == 0)
        {
            settings.Yanm.Components = YanmComponentSettings.CreateDefaultComponents();
            settings.Yanm.HasInitializedDefaultComponents = true;
            settings.Yanm.DefaultComponentVersion = YanmSettings.CurrentDefaultComponentVersion;
        }
        else if (settings.Yanm.DefaultComponentVersion < YanmSettings.CurrentDefaultComponentVersion)
        {
            YanmComponentSettings.UpgradeDefaultComponents(settings.Yanm.Components);
            settings.Yanm.DefaultComponentVersion = YanmSettings.CurrentDefaultComponentVersion;
        }

        foreach (var component in settings.Yanm.Components)
        {
            component.Id = string.IsNullOrWhiteSpace(component.Id) ? Guid.NewGuid().ToString("N") : component.Id;
            component.Title = string.IsNullOrWhiteSpace(component.Title) ? "燕幕组件" : component.Title.Trim();
            component.X = Math.Max(0, component.X);
            component.Y = Math.Max(0, component.Y);
            component.Width = Math.Max(settings.Yanm.GridSizePixels * 8, component.Width);
            component.Height = Math.Max(settings.Yanm.GridSizePixels * 6, component.Height);
            component.Html = string.IsNullOrWhiteSpace(component.Html) ? YanmComponentSettings.DefaultHtml(component.Title) : component.Html;
            component.Locked = component.Locked;
        }

        return settings;
    }

    private static QuickPanelSlotItem? NormalizeSlotItem(QuickPanelSlotItem? item)
    {
        if (item == null)
        {
            return null;
        }

        item.ItemType = string.IsNullOrWhiteSpace(item.ItemType) ? "extension" : item.ItemType.Trim().ToLowerInvariant();
        if (item.IsFolder)
        {
            item.FolderName = string.IsNullOrWhiteSpace(item.FolderName) ? "新分组" : item.FolderName.Trim();
            item.FolderExtensionIds ??= [];
            item.FolderExtensionIds = item.FolderExtensionIds
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return item.FolderExtensionIds.Count == 0 ? null : item;
        }

        item.ExtensionId = string.IsNullOrWhiteSpace(item.ExtensionId) ? null : item.ExtensionId.Trim();
        return string.IsNullOrWhiteSpace(item.ExtensionId) ? null : item;
    }

    private static List<string?> ProjectLegacySlots(IReadOnlyList<QuickPanelSlotItem?> slotItems)
    {
        var result = slotItems
            .Take(12)
            .Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null)
            .ToList();
        while (result.Count < 12)
        {
            result.Add(null);
        }

        return result;
    }

    private static bool HasWebDavConfigValues(string? serverUrl, string? rootPath, string? username)
    {
        return !string.IsNullOrWhiteSpace(serverUrl) ||
               !string.IsNullOrWhiteSpace(rootPath) ||
               !string.IsNullOrWhiteSpace(username);
    }

    private static YanyuRuleSettings NormalizeYanyuRule(YanyuRuleSettings? rule)
    {
        rule ??= new YanyuRuleSettings();
        rule.Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id.Trim();
        rule.TriggerText = (rule.TriggerText ?? string.Empty).Trim();
        rule.Description = (rule.Description ?? string.Empty).Trim();
        rule.BoundProcessName = (rule.BoundProcessName ?? string.Empty).Trim();
        rule.ActionType = YanyuActionTypes.Normalize(rule.ActionType);
        rule.TextContent ??= string.Empty;
        rule.ExtensionId = string.IsNullOrWhiteSpace(rule.ExtensionId) ? string.Empty : rule.ExtensionId.Trim();
        rule.TriggerSuffix = YanyuTriggerSuffix.Normalize(rule.TriggerSuffix);
        return rule;
    }
}

public sealed record AppSettings
{
    public string LauncherHotkey { get; set; } = "Alt+Space";

    public bool LaunchAtStartup { get; set; } = true;

    public bool RefreshCloudOnStartup { get; set; } = true;

    public bool CloseToTray { get; set; } = true;

    public List<string?> QuickPanelSlots { get; set; } = Enumerable.Repeat<string?>(null, 28).ToList();

    public List<QuickPanelGroupSettings> QuickPanelGlobalGroups { get; set; } = [];

    public List<QuickPanelGroupSettings> QuickPanelContextGroups { get; set; } = [];

    public string SelectedQuickPanelGlobalGroupId { get; set; } = "global-default";

    public string SelectedQuickPanelContextGroupId { get; set; } = "context-default";

    public string QuickPanelTrigger { get; set; } = "MiddleButtonLongPress";

    public QuickPanelMouseTriggerSettings QuickPanelMouseTriggers { get; set; } = new();

    public YarnSelectSettings YarnSelect { get; set; } = new();

    public RadialMenuSettings RadialMenu { get; set; } = new();

    public List<string> FavoriteExtensionIds { get; set; } = new();

    public List<string> GlobalFavoriteExtensionIds { get; set; } = new();

    public List<string> ContextFavoriteExtensionIds { get; set; } = new();

    public List<string> DisabledExtensionIds { get; set; } = new();

    public List<string> PinnedSearchScopeCommandIds { get; set; } = new();

    public List<string> RecentlyAddedExtensionIds { get; set; } = new();

    public List<string> UnreadNewExtensionIds { get; set; } = new();

    public bool EnableAgentApi { get; set; } = true;

    public int AgentApiPort { get; set; } = 53919;

    public string AgentApiToken { get; set; } = "yanzi-local-dev-token";

    public bool EnableWebDavSync { get; set; } = false;

    public bool WebDavSyncManuallyDisabled { get; set; } = false;

    public string WebDavServerUrl { get; set; } = "https://dav.jianguoyun.com/dav/";

    public string WebDavRootPath { get; set; } = "/yanzi";

    public string WebDavUsername { get; set; } = string.Empty;

    public bool PreferManualExtensionEditor { get; set; } = false;

    public string AiBaseUrl { get; set; } = string.Empty;

    public string AiApiKey { get; set; } = string.Empty;

    public string AiModel { get; set; } = string.Empty;

    public List<YanyuRuleSettings> YanyuRules { get; set; } = [];

    public YanmSettings Yanm { get; set; } = new();

    public double? SettingsWindowLeft { get; set; }

    public double? SettingsWindowTop { get; set; }

    public double? SettingsWindowWidth { get; set; }

    public double? SettingsWindowHeight { get; set; }
}

public sealed class QuickPanelGroupSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "未命名";

    public string? ContextProcessName { get; set; }

    public string? ContextDisplayName { get; set; }

    public List<string?> Slots { get; set; } = Enumerable.Repeat<string?>(null, 12).ToList();

    public List<QuickPanelSlotItem?> SlotItems { get; set; } = Enumerable.Repeat<QuickPanelSlotItem?>(null, 12).ToList();
}

public sealed class QuickPanelSlotItem
{
    public string ItemType { get; set; } = "extension";

    public string? ExtensionId { get; set; }

    public string? FolderName { get; set; }

    public List<string> FolderExtensionIds { get; set; } = [];

    public bool IsFolder => string.Equals(ItemType, "folder", StringComparison.OrdinalIgnoreCase);
}

public sealed record QuickPanelMouseTriggerSettings
{
    public bool MiddleButtonDown { get; set; } = false;

    public bool X1ButtonDown { get; set; } = false;

    public bool X2ButtonDown { get; set; } = false;

    public bool CtrlLeftClick { get; set; } = false;

    public bool CtrlRightClick { get; set; } = false;
    
    public bool CtrlMiddleClick { get; set; } = false;

    public bool MiddleButtonLongPress { get; set; } = true;

    public bool RightButtonLongPress { get; set; } = false;

    public bool RightButtonDrag { get; set; } = false;

    public bool HorizontalWheel { get; set; } = false;



    public bool ExecuteOnButtonRelease { get; set; } = true;

    public int LongPressMilliseconds { get; set; } = 500;

    public int DragThresholdPixels { get; set; } = 26;
}

public sealed class YarnSelectSettings
{
    public bool Enabled { get; set; } = true;

    public bool LeftCToCopy { get; set; } = true;

    public bool LeftXToCut { get; set; } = true;

    public bool LeftVToPaste { get; set; } = true;

    public bool LeftSToSearch { get; set; } = true;

    public bool LeftRToRun { get; set; } = true;

    public bool LeftRightSmartCopyPaste { get; set; } = true;

    public bool LeftSideButtonPaste { get; set; } = true;

    public int TriggerDelayMilliseconds { get; set; } = 80;

    public List<YarnSelectRuleSettings> Rules { get; set; } = [];

    public List<string> WhitelistedProcesses { get; set; } = [];

    public List<string> BlacklistedProcesses { get; set; } =
    [
        "Photoshop",
        "Maya",
        "Blender"
    ];

    public static List<YarnSelectRuleSettings> CreateDefaultRulesFromLegacy(YarnSelectSettings settings)
    {
        var rules = new List<YarnSelectRuleSettings>();
        if (settings.LeftCToCopy) rules.Add(new YarnSelectRuleSettings { TriggerKey = "C", ActionType = YarnSelectActionTypes.Copy, Description = "复制选中内容" });
        if (settings.LeftXToCut) rules.Add(new YarnSelectRuleSettings { TriggerKey = "X", ActionType = YarnSelectActionTypes.Cut, Description = "剪切选中内容" });
        if (settings.LeftVToPaste) rules.Add(new YarnSelectRuleSettings { TriggerKey = "V", ActionType = YarnSelectActionTypes.Paste, Description = "粘贴到当前位置" });
        if (settings.LeftSToSearch) rules.Add(new YarnSelectRuleSettings { TriggerKey = "S", ActionType = YarnSelectActionTypes.Search, Description = "复制选中内容并搜索" });
        if (settings.LeftRToRun) rules.Add(new YarnSelectRuleSettings { TriggerKey = "R", ActionType = YarnSelectActionTypes.Run, Description = "运行选中内容" });
        if (settings.LeftRightSmartCopyPaste) rules.Add(new YarnSelectRuleSettings { TriggerKey = "Right", ActionType = YarnSelectActionTypes.SmartCopyPaste, Description = "智能复制/粘贴" });
        if (settings.LeftSideButtonPaste) rules.Add(new YarnSelectRuleSettings { TriggerKey = "X1", ActionType = YarnSelectActionTypes.Paste, Description = "侧键粘贴" });
        return rules;
    }

    public static YarnSelectRuleSettings NormalizeRule(YarnSelectRuleSettings? rule)
    {
        rule ??= new YarnSelectRuleSettings();
        rule.TriggerKey = NormalizeTriggerKey(rule.TriggerKey);
        rule.ActionType = YarnSelectActionTypes.Normalize(rule.ActionType);
        rule.ExtensionId = (rule.ExtensionId ?? string.Empty).Trim();
        rule.Description = (rule.Description ?? string.Empty).Trim();
        return rule;
    }

    public static string NormalizeTriggerKey(string? value)
    {
        var key = (value ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            return string.Empty;
        }

        return key.ToLowerInvariant() switch
        {
            "right" or "右键" => "Right",
            "x1" or "侧键1" => "X1",
            "x2" or "侧键2" => "X2",
            _ when key.Length == 1 => key.ToUpperInvariant(),
            _ => key.ToUpperInvariant()
        };
    }
}

public sealed class RadialMenuSettings
{
    public const int InnerSlotCount = 8;

    public const int OuterSlotCount = 16;

    public const int TotalSlotCount = InnerSlotCount + OuterSlotCount;

    public bool Enabled { get; set; } = false;

    public bool TriggerRightButtonDrag { get; set; } = true;
    
    public bool TriggerRightButtonLongPress { get; set; } = false;
    
    public bool TriggerMiddleButtonLongPress { get; set; } = false;
    
    public bool TriggerMiddleButtonDown { get; set; } = false;
    
    public bool TriggerX1ButtonDown { get; set; } = false;
    
    public bool TriggerX2ButtonDown { get; set; } = false;
    
    public bool TriggerHorizontalWheel { get; set; } = false;
    
    public bool TriggerCtrlLeftClick { get; set; } = false;
    
    public bool TriggerCtrlRightClick { get; set; } = false;
    
    public bool TriggerCtrlMiddleClick { get; set; } = false;

    public bool TriggerCapsLockHold { get; set; } = true;

    public string MouseTriggerMode { get; set; } = MouseTriggerModes.RightDrag;

    public int DeadZonePixels { get; set; } = 32;

    public int RadiusPixels { get; set; } = 134;

    public int DragThresholdPixels { get; set; } = 24;

    public List<string?> Slots { get; set; } = Enumerable.Repeat<string?>(null, TotalSlotCount).ToList();

    public string SelectedPageId { get; set; } = "default";

    public List<RadialMenuPageSettings> Pages { get; set; } = [];
}

public sealed class RadialMenuPageSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "未命名";

    public List<string?> Slots { get; set; } = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList();

    public List<string?> SlotTitles { get; set; } = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList();

    public List<string?> ChildPageIds { get; set; } = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList();
}

public sealed class YanmSettings
{
    public const int CurrentDefaultComponentVersion = 9;

    public bool Enabled { get; set; } = false;

    public string ActivationKey { get; set; } = YanmActivationKeys.Win;

    public string CustomShortcut { get; set; } = string.Empty;

    public bool TriggerWinHold { get; set; } = true;

    public bool TriggerWinDoubleTap { get; set; } = true;

    public bool TriggerRightButtonDrag { get; set; } = false;
    
    public bool TriggerRightButtonLongPress { get; set; } = false;
    
    public bool TriggerMiddleButtonLongPress { get; set; } = false;
    
    public bool TriggerMiddleButtonDown { get; set; } = false;
    
    public bool TriggerX1ButtonDown { get; set; } = false;
    
    public bool TriggerX2ButtonDown { get; set; } = false;
    
    public bool TriggerHorizontalWheel { get; set; } = false;
    
    public bool TriggerCtrlLeftClick { get; set; } = false;
    
    public bool TriggerCtrlRightClick { get; set; } = false;
    
    public bool TriggerCtrlMiddleClick { get; set; } = false;

    public string MouseTriggerMode { get; set; } = MouseTriggerModes.None;

    public int DragThresholdPixels { get; set; } = 26;

    public int HoldDelayMilliseconds { get; set; } = 0;

    public int GridSizePixels { get; set; } = 10;

    public double OverlayOpacity { get; set; } = 0.58;

    public bool HasInitializedDefaultComponents { get; set; }

    public int DefaultComponentVersion { get; set; }

    public List<YanmComponentSettings> Components { get; set; } = [];

    public Dictionary<string, string> ComponentState { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class YanmActivationKeys
{
    public const string Win = "Win";

    public const string CapsLock = "CapsLock";

    public const string Custom = "Custom";

    public static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "capslock" => CapsLock,
            "custom" => Custom,
            _ => Win
        };
    }
}

public static class MouseTriggerModes
{
    public const string None = "None";
    public const string MiddleDown = "MiddleDown";
    public const string X1Down = "X1Down";
    public const string X2Down = "X2Down";
    public const string CtrlLeftClick = "CtrlLeftClick";
    public const string CtrlRightClick = "CtrlRightClick";
    public const string MiddleLongPress = "MiddleLongPress";
    public const string RightLongPress = "RightLongPress";
    public const string RightDrag = "RightDrag";
    public const string HorizontalWheel = "HorizontalWheel";

    public static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim() switch
        {
            MiddleDown => MiddleDown,
            X1Down => X1Down,
            X2Down => X2Down,
            CtrlLeftClick => CtrlLeftClick,
            CtrlRightClick => CtrlRightClick,
            MiddleLongPress => MiddleLongPress,
            RightLongPress => RightLongPress,
            RightDrag => RightDrag,
            HorizontalWheel => HorizontalWheel,
            _ => None
        };
    }
}

public sealed class YanmComponentSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "燕幕组件";

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 320;

    public double Height { get; set; } = 180;

    public bool Locked { get; set; }

    public string Html { get; set; } = DefaultHtml("燕幕组件");

    public string ScriptSource { get; set; } = string.Empty;

    public int RefreshIntervalSeconds { get; set; } = 300;

    public static List<YanmComponentSettings> CreateDefaultComponents()
    {
        return
        [
            new YanmComponentSettings
            {
                Title = "今日概览",
                X = 80,
                Y = 90,
                Width = 360,
                Height = 245,
                Html = CreateOverviewHtml()
            },
            new YanmComponentSettings
            {
                Title = "待办清单",
                X = 470,
                Y = 90,
                Width = 340,
                Height = 260,
                Html = CreateTodoHtml()
            },
            new YanmComponentSettings
            {
                Title = "网页监控",
                X = 840,
                Y = 90,
                Width = 390,
                Height = 240,
                Html = CreateWebMonitorHtml()
            },
            new YanmComponentSettings
            {
                Title = "系统状态",
                X = 80,
                Y = 350,
                Width = 360,
                Height = 190,
                Html = CreateSystemHtml()
            },
            new YanmComponentSettings
            {
                Title = "燕幕提示",
                X = 470,
                Y = 380,
                Width = 500,
                Height = 180,
                Html = CreateTipsHtml()
            },
            new YanmComponentSettings
            {
                Title = "便签",
                X = 1000,
                Y = 360,
                Width = 320,
                Height = 220,
                Html = CreateStickyNoteHtml()
            },
            new YanmComponentSettings
            {
                Title = "番茄时钟",
                X = 80,
                Y = 580,
                Width = 320,
                Height = 220,
                Html = CreatePomodoroHtml()
            },
            new YanmComponentSettings
            {
                Title = "倒计时",
                X = 430,
                Y = 590,
                Width = 340,
                Height = 210,
                Html = CreateCountdownHtml()
            },
            new YanmComponentSettings
            {
                Title = "下载目录",
                X = 800,
                Y = 580,
                Width = 340,
                Height = 220,
                Html = CreateDownloadFolderHtml()
            }
        ];
    }

    public static void UpgradeDefaultComponents(List<YanmComponentSettings> components)
    {
        var latest = CreateDefaultComponents();
        foreach (var template in latest)
        {
            var existing = components.FirstOrDefault(item =>
                item.Title.Equals(template.Title, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                components.Add(template);
                continue;
            }

            // Only refresh the built-in examples; user-created cards normally have different titles.
            existing.Html = template.Html;
            existing.Width = Math.Max(existing.Width, template.Width);
            existing.Height = Math.Max(existing.Height, template.Height);
        }
    }

    public static string DefaultHtml(string? title)
    {
        var safeTitle = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "燕幕组件" : title.Trim());
        return $$"""
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <style>
    html, body { margin: 0; width: 100%; height: 100%; overflow: hidden; background: transparent; font-family: "Microsoft YaHei", sans-serif; }
    .card { box-sizing: border-box; width: 100vw; height: 100vh; padding: 18px; border-radius: 22px; color: #fff; background: linear-gradient(135deg, rgba(35,38,48,.92), rgba(15,18,24,.82)); border: 1px solid rgba(255,255,255,.14); box-shadow: 0 18px 60px rgba(0,0,0,.34); }
    .eyebrow { font-size: 12px; color: #7cc7ff; letter-spacing: .18em; text-transform: uppercase; }
    h1 { margin: 10px 0 8px; font-size: 22px; }
    p { margin: 0; color: rgba(255,255,255,.68); line-height: 1.7; font-size: 13px; }
  </style>
</head>
<body>
  <section class="card">
    <div class="eyebrow">YANM COMPONENT</div>
    <h1>{{safeTitle}}</h1>
    <p>这是一个 HTML 信息组件。后续可接入脚本数据、网页抓取、待办、日历或 AI 生成的自定义界面。</p>
  </section>
</body>
</html>
""";
    }

    public static string BuildAiPrompt()
    {
        return """
请为“燕子启动器”的“燕幕”功能生成一个可直接粘贴使用的 HTML 信息组件。

输出要求：
1. 只输出 Markdown，不要解释，不要额外正文。
2. 最终内容必须放在一个 `html` 代码块里，格式为 ```html ... ```，方便预览和复制。
3. 代码块内必须是完整单文件 HTML：包含 <!doctype html>、html、head、style、body、script。
4. 不要依赖外部网络资源、CDN、图片、字体或 npm 包。
5. 组件运行在 Microsoft WebView2 中，组件尺寸由宿主控制，CSS 必须适配任意宽高。
6. html, body 必须：margin:0; width:100%; height:100%; overflow:hidden; background:transparent。
7. 主体用一个 .card 填满 100% 宽高，box-sizing:border-box，圆角 24-28px，深色/半透明/渐变风格，适合悬浮在桌面上。
8. 字体使用 "Microsoft YaHei", sans-serif，文字优先中文，视觉风格要像高级效率工具，不要白底表单风。
9. 可交互组件优先使用燕幕宿主状态保存数据，再用 localStorage 做本机兜底。
10. 输入框、按钮要有清晰 hover/active 状态，颜色要适配深色玻璃拟态背景。
11. 避免页面滚动条；内部列表需要滚动时只让内部容器滚动。
12. 如果需要滚动条，请不要使用默认系统样式。必须自定义成窄条、暗色、低对比度、圆角滑块的样式，并尽量不影响视觉。

宿主能力协议：
1. 统一入口是 `window.yanm.invoke(method, args)`，返回 `Promise`。
2. 兼容封装仍可存在，但组件优先使用 `window.yanm.invoke("clipboard.read")` 这类写法。
3. 宿主返回的数据统一通过：
   `window.addEventListener('yanm:message', function(e) { ... })`
4. 当 `e.detail.type === 'yanm.reply'` 时，说明一次 `invoke` 已完成，可根据 `id` 取回结果。
5. 当 `e.detail.type === 'host.systemInfo'` 时，可读取：
   - `e.detail.cpuCores`
   - `e.detail.isNetworkAvailable`
   - `e.detail.machineName`
   - `e.detail.osVersion`
   - `e.detail.time`
   - `e.detail.date`
   - `e.detail.totalMemoryMb`
   - `e.detail.availableMemoryMb`
   - `e.detail.usedMemoryPercent`
6. 当 `e.detail.type === 'host.state'` 时，可读取：
   - `e.detail.key`
   - `e.detail.value`
7. 组件初始化时不能假设 `window.yanm` 或 `window.yanmHost` 已经存在。必须实现重试初始化，例如 `setTimeout(initHost, 200)`。
8. 严禁写同步错误代码，例如：
   - `const value = window.yanmHost.getState("k")`
   - `const info = window.yanmHost.requestSystemInfo()`
   - `const text = window.yanm.invoke("clipboard.read")`
9. 正确模式是：
   - 先渲染本地兜底内容
   - 再异步请求宿主数据
   - 再在 `yanm:message` 里更新界面或处理 `Promise`
10. 如果组件包含备注、待办、输入框、开关等交互状态，必须：
   - 先更新内存中的当前状态
   - 同步刷新界面
   - 调用 `localStorage` 做本机兜底
   - 再调用 `window.yanm.invoke("state.set", { key: "...", value: "..." })` 保存到宿主
11. 如果组件需要剪贴板、桌面文件、命令执行、系统信息，优先使用以下能力名：
   - `clipboard.read`
   - `clipboard.write`
   - `desktop.list`
   - `command.execute`
   - `system.info`
   - `state.get`
   - `state.set`
   - `path.open`
   - `file.read`
   - `file.write`
   - `file.delete`
   - `file.exists`
   - `file.list`
   - `file.copy`
   - `file.move`
   - `path.downloads`

12. 文件能力说明：
   - `file.read` 默认按文本读取；传 `binary: true` 时返回 `contentBase64`
   - `file.write` 默认按文本写入；传 `binary: true` 且 `contentBase64` 时写入二进制
   - `file.delete` 支持文件与目录，目录可传 `recursive: true`
   - `file.list` 读取目录列表，可传 `recursive` 和 `limit`
   - `file.copy` / `file.move` 传 `source`、`destination`、`overwrite`
   - `path.downloads` 返回用户下载目录，不存在时回退到桌面目录

13. 如果你要生成“下载目录”类组件，推荐直接使用宿主能力：
   - `const folder = await window.yanm.invoke("path.downloads")`
   - `const list = await window.yanm.invoke("file.list", { path: folder.path, limit: 20 })`
   - `await window.yanm.invoke("path.open", { path: item.path })`

下载目录组件示例模板：
```html
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <style>
    html,body{margin:0;width:100%;height:100%;overflow:hidden;background:transparent;font-family:"Microsoft YaHei",sans-serif;color:#fff}
    .card{box-sizing:border-box;width:100%;height:100%;padding:18px;border-radius:24px;background:linear-gradient(135deg,rgba(16,31,54,.96),rgba(8,12,20,.92));border:1px solid rgba(255,255,255,.12);box-shadow:0 18px 60px rgba(0,0,0,.32)}
    .title{font-size:22px;font-weight:800;margin:0 0 8px}
    .path{font-size:12px;color:rgba(255,255,255,.65);word-break:break-all;margin-bottom:12px}
    .list{height:calc(100% - 110px);overflow:auto}
    .item{display:flex;justify-content:space-between;gap:10px;padding:10px 0;border-top:1px solid rgba(255,255,255,.08);cursor:pointer}
    .name{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
    .meta{font-size:12px;color:#93c5fd}
  </style>
</head>
<body>
  <section class="card">
    <div class="title">下载目录</div>
    <div class="path" id="folderPath">正在加载...</div>
    <div class="list" id="fileList"></div>
  </section>
  <script>
    var folder = '';
    var items = [];
    function render(){
      document.getElementById('folderPath').innerText = folder || '未找到下载目录，已回退到桌面。';
      var list = document.getElementById('fileList');
      list.innerHTML = '';
      items.forEach(function(item){
        var row = document.createElement('div');
        row.className = 'item';
        row.innerHTML = '<div class="name">' + (item.name || item.path || '') + '</div><div class="meta">' + (item.isDirectory ? '文件夹' : '文件') + '</div>';
        row.onclick = function(){ window.yanm.invoke('path.open', { path: item.path }); };
        list.appendChild(row);
      });
    }
    function init(){
      if(!window.yanm || !window.yanm.invoke){
        setTimeout(init, 200);
        return;
      }
      window.yanm.invoke('path.downloads').then(function(res){
        folder = res && res.path ? res.path : '';
        return window.yanm.invoke('file.list', { path: folder, limit: 20 });
      }).then(function(res){
        items = (res && res.items) || [];
        render();
      }).catch(function(){
        render();
      });
    }
    init();
  </script>
</body>
</html>
```

设计参考：
- 卡片使用 radial-gradient + linear-gradient + rgba 边框 + 柔和阴影。
- 标题 20-28px，标签 11-12px 且 letter-spacing。
- 数据块可使用圆角 pill/chip/grid。
- 交互控件使用圆角 12-16px，背景 rgba(255,255,255,.08-.14)。

基础模板：
```html
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
    <style>
    html,body{margin:0;width:100%;height:100%;overflow:hidden;background:transparent;font-family:"Microsoft YaHei",sans-serif;color:#fff}
    .card{box-sizing:border-box;width:100%;height:100%;padding:18px;border-radius:26px;background:radial-gradient(circle at 20% 0%,rgba(56,189,248,.22),transparent 34%),linear-gradient(135deg,rgba(20,24,35,.96),rgba(8,10,16,.92));border:1px solid rgba(255,255,255,.12);box-shadow:0 18px 60px rgba(0,0,0,.32)}
    .title{font-size:24px;font-weight:800;margin:0 0 10px}
    .muted{font-size:12px;color:rgba(255,255,255,.6)}
    .value{font-size:18px;font-weight:700}
    .scrollbar{scrollbar-width:thin;scrollbar-color:rgba(255,255,255,.24) transparent}
    .scrollbar::-webkit-scrollbar{width:6px;height:6px}
    .scrollbar::-webkit-scrollbar-track{background:transparent}
    .scrollbar::-webkit-scrollbar-thumb{background:rgba(255,255,255,.18);border-radius:999px;border:1px solid rgba(255,255,255,.06)}
  </style>
</head>
<body>
  <section class="card">
    <div class="title">组件标题</div>
    <div class="muted" id="status">等待宿主数据</div>
    <div class="value" id="content">--</div>
  </section>
  <script>
    var currentNote = '';
    function render(){
      document.getElementById('content').innerText = currentNote || '--';
    }
    function requestHost(){
      if(window.yanm && window.yanm.invoke){
        window.yanm.invoke('system.info');
        window.yanm.invoke('state.get', { key: 'demo.key' });
        return true;
      }
      return false;
    }
    function initHost(){
      if(!requestHost()){
        setTimeout(initHost, 200);
      }
    }
    window.addEventListener('yanm:message', function(e){
      var d = e.detail || {};
      if(d.type === 'yanm.reply' && d.id){
        if(d.ok === false){ return; }
      }
      if(d.type === 'host.systemInfo'){
        document.getElementById('status').innerText = '在线 ' + (d.machineName || '--');
      }
      if(d.type === 'host.state' && d.key === 'demo.key'){
        currentNote = d.value || '';
        render();
      }
    });
    try{
      currentNote = localStorage.getItem('demo.key') || '';
      render();
    }catch(e){}
    initHost();
  </script>
</body>
</html>
```

请按以上规范生成一个实用组件，主题由我下一句需求决定；如果我没有指定主题，请生成一个“今日效率概览”组件。
""";
    }

    private static string CreateOverviewHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:#fff;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:20px;border-radius:28px;background:radial-gradient(circle at 20% 0%,rgba(56,189,248,.38),transparent 32%),linear-gradient(135deg,rgba(23,32,51,.96),rgba(7,10,18,.9));border:1px solid rgba(255,255,255,.16);box-shadow:0 22px 70px rgba(0,0,0,.35)}
.top{display:flex;justify-content:space-between;align-items:center}.tag{font-size:12px;color:#7dd3fc;letter-spacing:.18em}.clock{font-size:13px;color:rgba(255,255,255,.72)}
.date{font-size:30px;font-weight:800;margin:14px 0 4px}.line{color:rgba(255,255,255,.7);font-size:13px;line-height:1.7}
.grid{display:grid;grid-template-columns:1fr 1fr 1fr;gap:10px;margin-top:16px}.pill{padding:12px;border-radius:18px;background:rgba(255,255,255,.09);border:1px solid rgba(255,255,255,.08)}.n{font-size:20px;font-weight:800}.l{font-size:11px;color:rgba(255,255,255,.58);margin-top:4px}
</style></head><body><section class="card"><div class="top"><div class="tag">TODAY</div><div id="clock" class="clock"></div></div><div id="date" class="date">今日概览</div><div class="line">快速扫一眼今天：时间、待办、刷新节奏和你关心的数据都可以放在这里。</div><div class="grid"><div class="pill"><div class="n" id="day">--</div><div class="l">星期</div></div><div class="pill"><div class="n">3</div><div class="l">待处理</div></div><div class="pill"><div class="n">30m</div><div class="l">刷新</div></div></div></section><script>
function tick(){var d=new Date();document.getElementById('clock').innerText=d.toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit'});document.getElementById('date').innerText=(d.getMonth()+1)+'月'+d.getDate()+'日';document.getElementById('day').innerText='周'+'日一二三四五六'.charAt(d.getDay());}tick();setInterval(tick,1000);
</script></body></html>
""";

    private static string CreateTodoHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:#fff;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:18px;border-radius:28px;background:radial-gradient(circle at 100% 0%,rgba(34,197,94,.35),transparent 34%),linear-gradient(160deg,rgba(18,48,38,.96),rgba(8,13,16,.92));border:1px solid rgba(255,255,255,.14);box-shadow:0 22px 70px rgba(0,0,0,.35)}
.head{display:flex;justify-content:space-between;align-items:center}h1{font-size:22px;margin:0}.count{font-size:12px;color:#86efac;background:rgba(34,197,94,.12);padding:5px 9px;border-radius:999px}
.add{display:flex;gap:8px;margin:14px 0}input{flex:1;border:0;outline:0;border-radius:14px;padding:10px 12px;background:rgba(255,255,255,.1);color:white}button{border:0;border-radius:14px;padding:0 13px;background:#22c55e;color:#06200f;font-weight:800;cursor:pointer}.list{height:145px;overflow:auto;scrollbar-width:thin;scrollbar-color:rgba(255,255,255,.24) transparent}.list::-webkit-scrollbar{width:6px;height:6px}.list::-webkit-scrollbar-track{background:transparent}.list::-webkit-scrollbar-thumb{background:rgba(255,255,255,.18);border-radius:999px;border:1px solid rgba(255,255,255,.06)}.item{display:flex;gap:10px;align-items:center;padding:9px 0;border-top:1px solid rgba(255,255,255,.08)}.check{width:18px;height:18px;border-radius:9px;background:#22c55e;cursor:pointer}.done .text{text-decoration:line-through;color:rgba(255,255,255,.42)}.text{flex:1;font-size:13px}.del{background:rgba(255,255,255,.1);color:#fecaca;height:26px}
</style></head><body><section class="card"><div class="head"><h1>待办清单</h1><span id="count" class="count">0 项</span></div><div class="add"><input id="todoInput" placeholder="添加一条待办..." /><button id="addButton" type="button">添加</button></div><div id="todoList" class="list"></div></section><script>
(function(){var todos=[];function fallback(){return[{text:"把常用信息组件化",done:false},{text:"接入脚本数据源",done:false},{text:"固定到顺手的位置",done:false}];}
function load(){try{var raw=localStorage.getItem("yanm.todos.v2");todos=raw?JSON.parse(raw):fallback();}catch(e){todos=fallback();}}
function save(){try{localStorage.setItem("yanm.todos.v2",JSON.stringify(todos));}catch(e){}}
function el(tag,cls,text){var n=document.createElement(tag);if(cls)n.className=cls;if(text)n.appendChild(document.createTextNode(text));return n;}
function render(){var list=document.getElementById("todoList");var count=document.getElementById("count");list.innerHTML="";var open=0;for(var i=0;i<todos.length;i++){(function(index){var item=todos[index];if(!item.done)open++;var row=el("div","item "+(item.done?"done":""));var check=el("span","check");var text=el("span","text",item.text);var del=el("button","del","×");check.onclick=function(){item.done=!item.done;save();render();};del.onclick=function(){todos.splice(index,1);save();render();};row.appendChild(check);row.appendChild(text);row.appendChild(del);list.appendChild(row);})(i);}count.innerText=open+" 项";}
function add(){var input=document.getElementById("todoInput");var v=input.value.replace(/^\s+|\s+$/g,"");if(!v){input.focus();return;}todos.unshift({text:v,done:false});input.value="";save();render();input.focus();}
function init(){if(window.__yanmTodoReady)return;window.__yanmTodoReady=true;load();render();document.getElementById("addButton").onclick=add;document.getElementById("todoInput").onkeydown=function(e){e=e||window.event;if(e.keyCode===13)add();};}
window.addTodo=add;if(document.readyState==="loading"){document.addEventListener("DOMContentLoaded",init);}else{init();}})();
</script></body></html>
""";

    private static string CreateWebMonitorHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:#fff;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:20px;border-radius:24px;background:linear-gradient(135deg,rgba(57,38,20,.95),rgba(16,14,12,.9));border:1px solid rgba(255,255,255,.13)}
.tag{color:#fbbf24;font-size:12px;letter-spacing:.18em}h1{margin:10px 0;font-size:24px}.url{padding:10px 12px;border-radius:14px;background:rgba(255,255,255,.08);font-size:12px;color:#fde68a;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.p{font-size:13px;line-height:1.7;color:rgba(255,255,255,.66);margin-top:12px}
</style></head><body><section class="card"><div class="tag">WEB WATCH</div><h1>网页监控</h1><div class="url">https://example.com/profile</div><div class="p">后续可用脚本定期抓取网页、RSS、接口或账号数据，例如小红书、B 站、GitHub、公众号等。</div></section></body></html>
""";

    private static string CreateSystemHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:#fff;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:18px;border-radius:28px;background:radial-gradient(circle at 20% 0%,rgba(129,140,248,.38),transparent 35%),linear-gradient(135deg,rgba(36,43,64,.96),rgba(9,12,20,.92));border:1px solid rgba(255,255,255,.14);box-shadow:0 22px 70px rgba(0,0,0,.35)}
h1{font-size:22px;margin:0 0 12px}.row{display:flex;justify-content:space-between;align-items:center;margin:9px 0;color:rgba(255,255,255,.72);font-size:13px}.bar{height:9px;border-radius:9px;background:rgba(255,255,255,.12);overflow:hidden}.fill{height:100%;width:0;background:linear-gradient(90deg,#38bdf8,#22c55e);transition:.3s}.chips{display:flex;gap:8px;margin-top:14px}.chip{flex:1;padding:9px;border-radius:15px;background:rgba(255,255,255,.08);font-size:11px;color:rgba(255,255,255,.65)}.v{display:block;color:white;font-size:16px;font-weight:800;margin-top:3px}
</style></head><body><section class="card"><h1>系统状态</h1><div class="row"><span>内存估算</span><strong id="memText">读取中</strong></div><div class="bar"><div id="memFill" class="fill"></div></div><div class="chips"><div class="chip">CPU 核心<span id="cores" class="v">--</span></div><div class="chip">在线状态<span id="net" class="v">--</span></div><div class="chip">时间<span id="time" class="v">--</span></div></div></section><script>
function paint(data){var percent=data&&data.usedMemoryPercent?data.usedMemoryPercent:0;var total=data&&data.totalMemoryMb?Math.round(data.totalMemoryMb/1024):0;var free=data&&data.availableMemoryMb?Math.round(data.availableMemoryMb/1024):0;document.getElementById('memText').innerText=total?('已用 '+Math.round(percent)+'% · 可用 '+free+' GB / '+total+' GB'):'等待宿主数据';document.getElementById('memFill').style.width=(percent||38)+'%';document.getElementById('cores').innerText=data&&data.cpuCores?data.cpuCores:'--';document.getElementById('net').innerText=data&&data.isNetworkAvailable?'在线':'离线';document.getElementById('time').innerText=data&&data.time?data.time:new Date().toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit'});}
window.addEventListener('yanm:message',function(e){if(e.detail&&e.detail.type==='host.systemInfo')paint(e.detail);});
function request(){if(window.yanmHost&&yanmHost.requestSystemInfo){yanmHost.requestSystemInfo();}else{paint(null);}}request();setInterval(request,3000);
</script></body></html>
""";

    private static string CreateTipsHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:#fff;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:20px;border-radius:24px;background:linear-gradient(135deg,rgba(38,31,75,.94),rgba(13,13,20,.9));border:1px solid rgba(255,255,255,.13)}
h1{font-size:23px;margin:0 0 10px}.p{font-size:13px;color:rgba(255,255,255,.68);line-height:1.8}.kbd{display:inline-block;padding:2px 8px;border-radius:8px;background:rgba(255,255,255,.12);color:#fff}
</style></head><body><section class="card"><h1>燕幕提示</h1><div class="p"><span class="kbd">按住 Win</span> 临时查看；<span class="kbd">双击 Win</span> 固定编辑；拖动空白区域新建组件；右键组件可编辑、锁定、删除。</div></section></body></html>
""";

    private static string CreateStickyNoteHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:#1b1608;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:18px;border-radius:26px;background:linear-gradient(145deg,#fde68a,#facc15);border:1px solid rgba(255,255,255,.5);box-shadow:0 22px 70px rgba(0,0,0,.28)}
.head{display:flex;justify-content:space-between;align-items:center;color:#713f12}.tag{font-size:12px;letter-spacing:.16em;font-weight:800}.hint{font-size:11px;opacity:.65}
textarea{box-sizing:border-box;width:100%;height:150px;margin-top:12px;border:0;outline:0;resize:none;background:rgba(255,255,255,.28);border-radius:18px;padding:14px;color:#241a05;font-size:15px;line-height:1.6}
</style></head><body><section class="card"><div class="head"><div class="tag">NOTE</div><div class="hint">自动保存</div></div><textarea id="note" placeholder="写下临时想法、链接、会议重点..."></textarea></section><script>
(function(){var key='yanm.sticky.note.v1';var el=document.getElementById('note');function local(){try{return localStorage.getItem(key)||'';}catch(e){return '';}}function saveLocal(v){try{localStorage.setItem(key,v);}catch(e){}}
function applyValue(v){el.value=typeof v==='string'?v:'';saveLocal(el.value);}
function requestHost(){if(window.yanmHost&&yanmHost.getState){yanmHost.getState(key);return true;}return false;}
el.value=local();
window.addEventListener('yanm:message',function(e){var d=e.detail||{};if(d.type==='host.state'&&d.key===key&&Object.prototype.hasOwnProperty.call(d,'value')){applyValue(String(d.value||''));}});
if(!requestHost()){document.addEventListener('DOMContentLoaded',function(){requestHost();},{once:true});setTimeout(requestHost,300);}
el.addEventListener('input',function(){var v=el.value;saveLocal(v);if(window.yanmHost&&yanmHost.setState){yanmHost.setState(key,v);}});
})();
</script></body></html>
""";

    private static string CreatePomodoroHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:white;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:20px;border-radius:28px;background:radial-gradient(circle at 80% 10%,rgba(248,113,113,.42),transparent 35%),linear-gradient(135deg,rgba(70,18,31,.96),rgba(16,8,12,.92));border:1px solid rgba(255,255,255,.14);box-shadow:0 22px 70px rgba(0,0,0,.35);text-align:center}
.tag{font-size:12px;color:#fca5a5;letter-spacing:.18em}.time{font-size:52px;font-weight:900;margin:18px 0 12px;font-variant-numeric:tabular-nums}.mode{font-size:13px;color:rgba(255,255,255,.68)}button{border:0;border-radius:14px;padding:9px 13px;margin:14px 4px 0;background:rgba(255,255,255,.13);color:white;font-weight:800;cursor:pointer}.primary{background:#fb7185;color:#2b0710}
</style></head><body><section class="card"><div class="tag">POMODORO</div><div id="time" class="time">25:00</div><div id="mode" class="mode">专注 25 分钟</div><button id="start" class="primary">开始</button><button id="reset">重置</button></section><script>
(function(){var total=25*60,left=total,timer=null,running=false;function fmt(s){return String(Math.floor(s/60)).padStart(2,'0')+':'+String(s%60).padStart(2,'0')}function render(){document.getElementById('time').innerText=fmt(left);document.getElementById('start').innerText=running?'暂停':'开始';}
function tick(){if(left>0){left--;render();return;}clearInterval(timer);timer=null;running=false;document.getElementById('mode').innerText='完成，休息一下';render();}
document.getElementById('start').onclick=function(){running=!running;if(running){timer=setInterval(tick,1000);}else{clearInterval(timer);timer=null;}render();};
document.getElementById('reset').onclick=function(){clearInterval(timer);timer=null;running=false;left=total;document.getElementById('mode').innerText='专注 25 分钟';render();};render();})();
</script></body></html>
""";

    private static string CreateCountdownHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:white;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:18px;border-radius:26px;background:radial-gradient(circle at 0% 0%,rgba(96,165,250,.42),transparent 36%),linear-gradient(135deg,rgba(20,41,74,.96),rgba(8,12,22,.92));border:1px solid rgba(255,255,255,.14);box-shadow:0 22px 70px rgba(0,0,0,.34)}
.top{display:flex;justify-content:space-between;align-items:center}.tag{font-size:12px;color:#93c5fd;letter-spacing:.18em}.time{font-size:42px;font-weight:900;margin:15px 0 10px;font-variant-numeric:tabular-nums}.row{display:flex;gap:8px}input{flex:1;min-width:0;border:0;outline:0;border-radius:14px;padding:10px;background:rgba(255,255,255,.1);color:white}button{border:0;border-radius:14px;padding:0 12px;background:#60a5fa;color:#061525;font-weight:900;cursor:pointer}.hint{font-size:12px;color:rgba(255,255,255,.62);line-height:1.6}
</style></head><body><section class="card"><div class="top"><div class="tag">COUNTDOWN</div><div class="hint">分钟</div></div><div id="time" class="time">10:00</div><div class="row"><input id="minutes" value="10" /><button id="start">开始</button><button id="reset">重置</button></div><div id="hint" class="hint">输入分钟后开始倒计时。</div></section><script>
(function(){var left=600,total=600,timer=null;function fmt(s){return String(Math.floor(s/60)).padStart(2,'0')+':'+String(s%60).padStart(2,'0')}function render(){document.getElementById('time').innerText=fmt(Math.max(0,left));}
function setFromInput(){var m=parseFloat(document.getElementById('minutes').value)||10;total=Math.max(1,Math.round(m*60));left=total;render();}
document.getElementById('start').onclick=function(){if(timer){clearInterval(timer);timer=null;this.innerText='开始';return;}if(left<=0)setFromInput();this.innerText='暂停';timer=setInterval(function(){left--;render();if(left<=0){clearInterval(timer);timer=null;document.getElementById('start').innerText='开始';document.getElementById('hint').innerText='倒计时结束';}},1000);};
document.getElementById('reset').onclick=function(){clearInterval(timer);timer=null;document.getElementById('start').innerText='开始';setFromInput();document.getElementById('hint').innerText='输入分钟后开始倒计时。';};setFromInput();})();
</script></body></html>
""";

    private static string CreateDownloadFolderHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:#fff;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:18px;border-radius:24px;background:radial-gradient(circle at 100% 0%,rgba(14,165,233,.35),transparent 36%),linear-gradient(135deg,rgba(11,18,31,.96),rgba(9,12,20,.92));border:1px solid rgba(255,255,255,.13);box-shadow:0 22px 70px rgba(0,0,0,.34)}
.top{display:flex;justify-content:space-between;align-items:center}.tag{font-size:12px;letter-spacing:.18em;color:#7dd3fc}.btn{border:0;border-radius:14px;padding:8px 12px;background:rgba(255,255,255,.1);color:#fff;cursor:pointer}
.path{margin-top:12px;padding:12px;border-radius:16px;background:rgba(255,255,255,.08);font-size:12px;color:rgba(255,255,255,.72);word-break:break-all;min-height:44px}
.list{margin-top:12px;height:94px;overflow:auto;scrollbar-width:thin;scrollbar-color:rgba(255,255,255,.24) transparent}.list::-webkit-scrollbar{width:6px;height:6px}.list::-webkit-scrollbar-track{background:transparent}.list::-webkit-scrollbar-thumb{background:rgba(255,255,255,.18);border-radius:999px;border:1px solid rgba(255,255,255,.06)}
.item{display:flex;justify-content:space-between;gap:10px;padding:8px 0;border-top:1px solid rgba(255,255,255,.08);font-size:12px;cursor:pointer}.name{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.open{color:#93c5fd}
</style></head><body><section class="card"><div class="top"><div class="tag">DOWNLOADS</div><button class="btn" id="refreshBtn" type="button">刷新</button></div><div class="path" id="folderPath">正在定位下载目录...</div><div id="list" class="list"></div></section><script>
(function(){var folder='';var items=[];function render(){document.getElementById('folderPath').innerText=folder||'未找到下载目录，已回退到桌面。';var list=document.getElementById('list');list.innerHTML='';for(var i=0;i<items.length&&i<5;i++){(function(item){var row=document.createElement('div');row.className='item';var left=document.createElement('div');left.className='name';left.innerText=item.name||(item.path||'');var right=document.createElement('div');right.className='open';right.innerText=item.isDirectory?'打开文件夹':'打开文件';row.onclick=function(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('path.open',{path:item.path});}};row.appendChild(left);row.appendChild(right);list.appendChild(row);})(items[i]);}}
function loadFolder(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('path.downloads').then(function(res){folder=res&&res.path?res.path:'';render();refresh();}).catch(function(){folder='';render();});}else{folder='';render();}}
function refresh(){if(!folder){items=[];render();return;}if(window.yanm&&window.yanm.invoke){window.yanm.invoke('file.list',{path:folder,limit:20}).then(function(res){items=(res&&res.items)||[];render();}).catch(function(){items=[];render();});}}
document.getElementById('refreshBtn').onclick=function(){loadFolder();};loadFolder();})();
</script></body></html>
""";
}

public sealed class YarnSelectRuleSettings
{
    public bool Enabled { get; set; } = true;

    public string TriggerKey { get; set; } = string.Empty;

    public string ActionType { get; set; } = YarnSelectActionTypes.Copy;

    public string ExtensionId { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

public static class YarnSelectActionTypes
{
    public const string Copy = "copy";
    public const string Cut = "cut";
    public const string Paste = "paste";
    public const string Search = "search";
    public const string Run = "run";
    public const string SmartCopyPaste = "smart_copy_paste";
    public const string RunExtension = "run_extension";

    public static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            Cut => Cut,
            Paste => Paste,
            Search => Search,
            Run => Run,
            SmartCopyPaste => SmartCopyPaste,
            RunExtension => RunExtension,
            _ => Copy
        };
    }
}

public static class YanyuActionTypes
{
    public const string PasteText = "paste_text";
    public const string RunExtension = "run_extension";

    public static string Normalize(string? value)
    {
        return string.Equals(value, RunExtension, StringComparison.OrdinalIgnoreCase)
            ? RunExtension
            : PasteText;
    }
}

public static class YanyuTriggerSuffix
{
    public const string Space = "space";
    public const string Tab = "tab";
    public const string Enter = "enter";

    public static string Normalize(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return Space;
        }

        var lowered = trimmed.ToLowerInvariant();
        return lowered switch
        {
            Space => Space,
            Tab => Tab,
            Enter => Enter,
            _ when trimmed.Length == 1 => trimmed,
            _ => Space
        };
    }

    public static string ToDisplayText(string? value)
    {
        return Normalize(value) switch
        {
            Space => "空格",
            Tab => "Tab",
            Enter => "Enter",
            var custom => custom
        };
    }
}

public sealed class YanyuRuleSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public bool Enabled { get; set; } = true;

    public string TriggerText { get; set; } = string.Empty;

    public string TriggerSuffix { get; set; } = YanyuTriggerSuffix.Space;

    public bool UseRegex { get; set; }

    public string BoundProcessName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ActionType { get; set; } = YanyuActionTypes.PasteText;

    public string TextContent { get; set; } = string.Empty;

    public string ExtensionId { get; set; } = string.Empty;
}
