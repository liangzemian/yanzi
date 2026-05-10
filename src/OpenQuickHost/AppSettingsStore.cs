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
                Slots = settings.RadialMenu.Slots?.ToList() ?? Enumerable.Repeat<string?>(null, 8).ToList()
            });
        }

        foreach (var page in settings.RadialMenu.Pages)
        {
            page.Id = string.IsNullOrWhiteSpace(page.Id) ? Guid.NewGuid().ToString("N") : page.Id.Trim();
            page.Name = string.IsNullOrWhiteSpace(page.Name) ? "未命名" : page.Name.Trim();
            page.Slots ??= [];
            while (page.Slots.Count < 8)
            {
                page.Slots.Add(null);
            }

            if (page.Slots.Count > 8)
            {
                page.Slots = page.Slots.Take(8).ToList();
            }

            page.Slots = page.Slots
                .Select(static id => string.IsNullOrWhiteSpace(id) ? null : id.Trim())
                .ToList();
            page.ChildPageIds ??= [];
            while (page.ChildPageIds.Count < 8)
            {
                page.ChildPageIds.Add(null);
            }

            if (page.ChildPageIds.Count > 8)
            {
                page.ChildPageIds = page.ChildPageIds.Take(8).ToList();
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
    public bool Enabled { get; set; } = false;

    public bool TriggerRightButtonDrag { get; set; } = true;

    public bool TriggerCapsLockHold { get; set; } = true;

    public int DeadZonePixels { get; set; } = 32;

    public int RadiusPixels { get; set; } = 134;

    public int DragThresholdPixels { get; set; } = 24;

    public List<string?> Slots { get; set; } = Enumerable.Repeat<string?>(null, 8).ToList();

    public string SelectedPageId { get; set; } = "default";

    public List<RadialMenuPageSettings> Pages { get; set; } = [];
}

public sealed class RadialMenuPageSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "未命名";

    public List<string?> Slots { get; set; } = Enumerable.Repeat<string?>(null, 8).ToList();

    public List<string?> ChildPageIds { get; set; } = Enumerable.Repeat<string?>(null, 8).ToList();
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
