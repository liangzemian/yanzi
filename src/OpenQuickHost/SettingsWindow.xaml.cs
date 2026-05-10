using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public partial class SettingsWindow : Window, INotifyPropertyChanged
{
    private const string RadialSimulatedKeyPrefix = "keysim::";
    private readonly MainWindow _mainWindow;
    private AppSettings _settings;
    private SettingsNavigationItem? _selectedNavigation;
    private string _accountTitle = "未登录";
    private string _accountSubtitle = "点击左上角账户卡片登录或切换账号。";
    private string _accountInitial = "燕";
    private bool _isAccountLoggedIn;
    private string _localExtensionSummary = "正在统计...";
    private string _settingsSearchText = string.Empty;
    private string _extensionSearchText = string.Empty;
    private string _radialMenuSearchText = string.Empty;
    private string _launcherHotkey = "Alt+Space";
    private string _syncStatusText = "同步服务状态未知。";
    private string _webDavServerUrl = "https://dav.jianguoyun.com/dav/";
    private string _webDavRootPath = "/yanzi";
    private string _webDavUsername = string.Empty;
    private string _webDavStatusText = "未启用个人扩展同步。";
    private string _syncActivityLogText = "暂无同步记录。";
    private string _aiBaseUrl = string.Empty;
    private string _aiApiKey = string.Empty;
    private string _aiModel = string.Empty;
    private string _aiSettingsStatusText = "尚未配置 AI。";
    private string _recycleBinSummary = "回收站为空。";
    private string _recycleBinSearchText = string.Empty;
    private bool _isExtensionsLoading;
    private int _extensionsRefreshVersion;
    private IReadOnlyList<SettingsExtensionItem> _cachedExtensionItems = [];
    private IReadOnlyList<SettingsRecycleBinItem> _cachedRecycleBinItems = [];
    private bool _suppressWindowBoundsPersistence;
    private bool _isRefreshingRadialMenu;
    private bool _isRenamingRadialMenuPage;
    private RadialMenuSlotEditorItem? _selectedRadialMenuSlot;

    public SettingsWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _settings = AppSettingsStore.Load();
        _settings.QuickPanelMouseTriggers ??= new QuickPanelMouseTriggerSettings();
        _settings.YarnSelect ??= new YarnSelectSettings();
        _settings.RadialMenu ??= new RadialMenuSettings();
        NavigationItems =
        [
            new SettingsNavigationItem("general", "mdi:settings", "常规", "#FF3B82F6"),
            new SettingsNavigationItem("ai", "mdi:ai", "AI", "#FF8B5CF6"),
            new SettingsNavigationItem("sync", "mdi:sync", "同步", "#FF22C55E"),
            new SettingsNavigationItem("extensions", "mdi:dashboard", "扩展", "#FFF97316"),
            new SettingsNavigationItem("recycle", "mdi:recycle", "回收站", "#FFEF4444"),
            new SettingsNavigationItem("shortcuts", "mdi:shortcut", "快捷键", "#FFEAB308"),
            new SettingsNavigationItem("quickpanel", "mdi:mouse-panel", "鼠标面板", "#FFEC4899"),
            new SettingsNavigationItem("yarnselect", "mdi:shortcut", "燕选", "#FF14B8A6"),
            new SettingsNavigationItem("about", "mdi:about", "关于", "#FF8B5CF6")
        ];
        _selectedNavigation = NavigationItems.First();
        LaunchAtStartup = _settings.LaunchAtStartup;
        RefreshCloudOnStartup = _settings.RefreshCloudOnStartup;
        CloseToTray = _settings.CloseToTray;
        LauncherHotkey = _settings.LauncherHotkey;
        EnableWebDavSync = _settings.EnableWebDavSync;
        WebDavServerUrl = string.IsNullOrWhiteSpace(_settings.WebDavServerUrl) ? "https://dav.jianguoyun.com/dav/" : _settings.WebDavServerUrl;
        WebDavRootPath = _settings.WebDavRootPath;
        WebDavUsername = _settings.WebDavUsername;
        AiBaseUrl = _settings.AiBaseUrl;
        AiApiKey = _settings.AiApiKey;
        AiModel = _settings.AiModel;
        AiSettingsStatusText = BuildAiSettingsSummary(_settings);
        BaseUrl = _mainWindow.SyncBaseUrl;
        ExtensionsRootPath = LocalExtensionCatalog.CatalogRootPath;
        AppVersionText = AppVersionInfo.DisplayText;
        ShortcutItems = new ObservableCollection<SettingsShortcutItem>();
        ExtensionItems = new ObservableCollection<SettingsExtensionItem>();
        RecycleBinItems = new ObservableCollection<SettingsRecycleBinItem>();
        YarnSelectRules = new ObservableCollection<YarnSelectRuleItem>();
        YarnSelectExtensionOptions = new ObservableCollection<YarnSelectExtensionOption>();
        RadialMenuExtensionOptions = new ObservableCollection<YarnSelectExtensionOption>();
        FilteredRadialMenuCommandOptions = new ObservableCollection<YarnSelectExtensionOption>();
        RadialMenuSlots = new ObservableCollection<RadialMenuSlotEditorItem>();
        RadialMenuPreviewSeparators = new ObservableCollection<RadialSeparatorViewModel>();
        RadialMenuPages = new ObservableCollection<RadialMenuPageEditorItem>();
        RadialMenuChildPageOptions = new ObservableCollection<RadialMenuPageEditorItem>();
        DataContext = this;
        RefreshRadialMenuSlots();
        ApplySavedWindowBounds();
        Loaded += SettingsWindow_Loaded;
        Activated += SettingsWindow_Activated;
        LocationChanged += SettingsWindow_BoundsChanged;
        SizeChanged += SettingsWindow_BoundsChanged;
        Closing += SettingsWindow_Closing;
        LoadLogoImage();
    }

    public ObservableCollection<SettingsNavigationItem> NavigationItems { get; }

    public ObservableCollection<SettingsShortcutItem> ShortcutItems { get; }

    public ObservableCollection<SettingsExtensionItem> ExtensionItems { get; }

    public ObservableCollection<SettingsRecycleBinItem> RecycleBinItems { get; }

    public ObservableCollection<YarnSelectRuleItem> YarnSelectRules { get; }

    public ObservableCollection<YarnSelectExtensionOption> YarnSelectExtensionOptions { get; }

    public ObservableCollection<YarnSelectExtensionOption> RadialMenuExtensionOptions { get; }

    public ObservableCollection<YarnSelectExtensionOption> FilteredRadialMenuCommandOptions { get; }

    public ObservableCollection<RadialMenuSlotEditorItem> RadialMenuSlots { get; }

    public ObservableCollection<RadialSeparatorViewModel> RadialMenuPreviewSeparators { get; }

    public ObservableCollection<RadialMenuPageEditorItem> RadialMenuPages { get; }

    public ObservableCollection<RadialMenuPageEditorItem> RadialMenuChildPageOptions { get; }

    public IReadOnlyList<YarnSelectActionTypeOption> YarnSelectActionOptions { get; } =
    [
        new(YarnSelectActionTypes.Copy, "复制"),
        new(YarnSelectActionTypes.Cut, "剪切"),
        new(YarnSelectActionTypes.Paste, "粘贴"),
        new(YarnSelectActionTypes.Search, "搜索"),
        new(YarnSelectActionTypes.Run, "运行文本"),
        new(YarnSelectActionTypes.SmartCopyPaste, "智能复制/粘贴"),
        new(YarnSelectActionTypes.RunExtension, "运行扩展")
    ];

    public SettingsNavigationItem? SelectedNavigation
    {
        get => _selectedNavigation;
        set
        {
            if (Equals(value, _selectedNavigation))
            {
                return;
            }

            _selectedNavigation = value;
            HostAssets.AppendLog($"Settings navigation selected: key={_selectedNavigation?.Key ?? "null"}");
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSectionTitle));
            OnPropertyChanged(nameof(SelectedSectionDescription));
            OnPropertyChanged(nameof(IsGeneralSelected));
            OnPropertyChanged(nameof(IsAiSelected));
            OnPropertyChanged(nameof(IsSyncSelected));
            OnPropertyChanged(nameof(IsExtensionsSelected));
            OnPropertyChanged(nameof(IsRecycleBinSelected));
            OnPropertyChanged(nameof(IsShortcutsSelected));
            OnPropertyChanged(nameof(IsQuickPanelSelected));
            OnPropertyChanged(nameof(IsYarnSelectSelected));
            OnPropertyChanged(nameof(IsAboutSelected));
            if (IsExtensionsSelected)
            {
                _ = RefreshExtensionsFromDiskAsync();
            }
            else if (IsRecycleBinSelected)
            {
                _ = RefreshExtensionsFromDiskAsync();
            }
            else if (IsSyncSelected)
            {
                RefreshSyncActivityLog();
            }
            else if (IsShortcutsSelected)
            {
                try
                {
                    RefreshExtensionCacheFromMainWindow();
                    RefreshShortcutItems();
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"Settings shortcuts refresh failed on navigation: {ex}");
                }
            }
        }
    }

    public bool RefreshCloudOnStartup
    {
        get => _settings.RefreshCloudOnStartup;
        set
        {
            if (value == _settings.RefreshCloudOnStartup)
            {
                return;
            }

            _settings = _settings with { RefreshCloudOnStartup = value };
            OnPropertyChanged();
        }
    }

    public bool LaunchAtStartup
    {
        get => _settings.LaunchAtStartup;
        set
        {
            if (value == _settings.LaunchAtStartup)
            {
                return;
            }

            _settings = _settings with { LaunchAtStartup = value };
            OnPropertyChanged();
        }
    }

    private void LoadLogoImage()
    {
        try
        {
            AboutLogoImage.Source = new BitmapImage(new Uri("pack://application:,,,/logo-white.png", UriKind.Absolute));
        }
        catch
        {
            // Ignore logo load failures so settings can still open in published builds.
        }
    }

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set
        {
            if (value == _settings.CloseToTray)
            {
                return;
            }

            _settings = _settings with { CloseToTray = value };
            OnPropertyChanged();
        }
    }

    public string AccountTitle
    {
        get => _accountTitle;
        private set
        {
            if (value == _accountTitle)
            {
                return;
            }

            _accountTitle = value;
            OnPropertyChanged();
        }
    }

    public string AccountSubtitle
    {
        get => _accountSubtitle;
        private set
        {
            if (value == _accountSubtitle)
            {
                return;
            }

            _accountSubtitle = value;
            OnPropertyChanged();
        }
    }

    public string AccountInitial
    {
        get => _accountInitial;
        private set
        {
            if (value == _accountInitial)
            {
                return;
            }

            _accountInitial = value;
            OnPropertyChanged();
        }
    }

    public bool IsAccountLoggedIn
    {
        get => _isAccountLoggedIn;
        private set
        {
            if (value == _isAccountLoggedIn)
            {
                return;
            }

            _isAccountLoggedIn = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SignInButtonText));
            OnPropertyChanged(nameof(SignInMenuText));
        }
    }

    public string SignInButtonText => IsAccountLoggedIn ? "切换账号" : "登录账号";

    public string SignInMenuText => IsAccountLoggedIn ? "切换账号" : "登录账号";

    public string BaseUrl { get; }

    public string ExtensionsRootPath { get; }

    public string AppVersionText { get; }

    public string LauncherHotkey
    {
        get => _launcherHotkey;
        private set
        {
            if (value == _launcherHotkey)
            {
                return;
            }

            _launcherHotkey = value;
            OnPropertyChanged();
        }
    }

    public string SyncStatusText
    {
        get => _syncStatusText;
        private set
        {
            if (value == _syncStatusText)
            {
                return;
            }

            _syncStatusText = value;
            OnPropertyChanged();
        }
    }

    public bool EnableWebDavSync
    {
        get => _settings.EnableWebDavSync;
        set
        {
            if (value == _settings.EnableWebDavSync)
            {
                return;
            }

            _settings = _settings with { EnableWebDavSync = value };
            OnPropertyChanged();
        }
    }

    public string WebDavServerUrl
    {
        get => _webDavServerUrl;
        set
        {
            if (value == _webDavServerUrl)
            {
                return;
            }

            _webDavServerUrl = value;
            OnPropertyChanged();
        }
    }

    public string WebDavRootPath
    {
        get => _webDavRootPath;
        set
        {
            if (value == _webDavRootPath)
            {
                return;
            }

            _webDavRootPath = value;
            OnPropertyChanged();
        }
    }

    public string WebDavUsername
    {
        get => _webDavUsername;
        set
        {
            if (value == _webDavUsername)
            {
                return;
            }

            _webDavUsername = value;
            OnPropertyChanged();
        }
    }

    public string WebDavStatusText
    {
        get => _webDavStatusText;
        private set
        {
            if (value == _webDavStatusText)
            {
                return;
            }

            _webDavStatusText = value;
            OnPropertyChanged();
        }
    }

    public string SyncActivityLogText
    {
        get => _syncActivityLogText;
        private set
        {
            if (value == _syncActivityLogText)
            {
                return;
            }

            _syncActivityLogText = value;
            OnPropertyChanged();
        }
    }

    public string AiBaseUrl
    {
        get => _aiBaseUrl;
        set
        {
            if (value == _aiBaseUrl)
            {
                return;
            }

            _aiBaseUrl = value;
            OnPropertyChanged();
        }
    }

    public string AiApiKey
    {
        get => _aiApiKey;
        set
        {
            if (value == _aiApiKey)
            {
                return;
            }

            _aiApiKey = value;
            OnPropertyChanged();
        }
    }

    public string AiModel
    {
        get => _aiModel;
        set
        {
            if (value == _aiModel)
            {
                return;
            }

            _aiModel = value;
            OnPropertyChanged();
        }
    }

    public string AiSettingsStatusText
    {
        get => _aiSettingsStatusText;
        private set
        {
            if (value == _aiSettingsStatusText)
            {
                return;
            }

            _aiSettingsStatusText = value;
            OnPropertyChanged();
        }
    }

    public string RecycleBinSummary
    {
        get => _recycleBinSummary;
        private set
        {
            if (value == _recycleBinSummary)
            {
                return;
            }

            _recycleBinSummary = value;
            OnPropertyChanged();
        }
    }

    public string LocalExtensionSummary
    {
        get => _localExtensionSummary;
        private set
        {
            if (value == _localExtensionSummary)
            {
                return;
            }

            _localExtensionSummary = value;
            OnPropertyChanged();
        }
    }

    public string SettingsSearchText
    {
        get => _settingsSearchText;
        set
        {
            if (value == _settingsSearchText)
            {
                return;
            }

            _settingsSearchText = value;
            OnPropertyChanged();
            ApplySettingsSearch(value);
        }
    }

    public string ExtensionSearchText
    {
        get => _extensionSearchText;
        set
        {
            if (value == _extensionSearchText)
            {
                return;
            }

            _extensionSearchText = value;
            OnPropertyChanged();
            RefreshExtensionItems();
        }
    }

    public string RadialMenuSearchText
    {
        get => _radialMenuSearchText;
        set
        {
            value ??= string.Empty;
            if (value == _radialMenuSearchText)
            {
                return;
            }

            _radialMenuSearchText = value;
            OnPropertyChanged();
            RefreshRadialMenuCommandCandidates(value);
        }
    }

    public string RadialMenuSelectedSlotSummary => _selectedRadialMenuSlot == null
        ? "先点击左侧轮盘槽位，再搜索并添加；也可以右键槽位打开菜单。"
        : $"当前槽位：{_selectedRadialMenuSlot.Label} · 可添加扩展、程序、系统设置项，或右键添加子环。";

    public string RecycleBinSearchText
    {
        get => _recycleBinSearchText;
        set
        {
            if (value == _recycleBinSearchText)
            {
                return;
            }

            _recycleBinSearchText = value;
            OnPropertyChanged();
            RefreshRecycleBinItems();
        }
    }

    public string ExtensionSearchSummary =>
        IsExtensionsLoading
            ? "正在刷新..."
            : ExtensionItems.Count == 0
            ? "无匹配项"
            : $"显示 {ExtensionItems.Count} 项";

    public string RecycleBinSearchSummary =>
        IsExtensionsLoading
            ? "正在刷新..."
            : RecycleBinItems.Count == 0
            ? "无匹配项"
            : $"显示 {RecycleBinItems.Count} 项";

    public bool IsExtensionsLoading
    {
        get => _isExtensionsLoading;
        private set
        {
            if (value == _isExtensionsLoading)
            {
                return;
            }

            _isExtensionsLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExtensionsLoadingVisibility));
            OnPropertyChanged(nameof(ExtensionsListVisibility));
            OnPropertyChanged(nameof(CanRefreshExtensions));
            OnPropertyChanged(nameof(ExtensionSearchSummary));
            OnPropertyChanged(nameof(RecycleBinSearchSummary));
        }
    }

    public Visibility ExtensionsLoadingVisibility => IsExtensionsLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ExtensionsListVisibility => IsExtensionsLoading ? Visibility.Collapsed : Visibility.Visible;

    public bool CanRefreshExtensions => !IsExtensionsLoading;

    public bool TriggerMiddleButtonDown
    {
        get => _settings.QuickPanelMouseTriggers.MiddleButtonDown;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.MiddleButtonDown = value);
    }

    public bool TriggerX1ButtonDown
    {
        get => _settings.QuickPanelMouseTriggers.X1ButtonDown;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.X1ButtonDown = value);
    }

    public bool TriggerX2ButtonDown
    {
        get => _settings.QuickPanelMouseTriggers.X2ButtonDown;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.X2ButtonDown = value);
    }

    public bool TriggerCtrlLeftClick
    {
        get => _settings.QuickPanelMouseTriggers.CtrlLeftClick;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.CtrlLeftClick = value);
    }

    public bool TriggerCtrlRightClick
    {
        get => _settings.QuickPanelMouseTriggers.CtrlRightClick;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.CtrlRightClick = value);
    }

    public bool TriggerMiddleButtonLongPress
    {
        get => _settings.QuickPanelMouseTriggers.MiddleButtonLongPress;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.MiddleButtonLongPress = value);
    }

    public bool TriggerRightButtonLongPress
    {
        get => _settings.QuickPanelMouseTriggers.RightButtonLongPress;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.RightButtonLongPress = value);
    }

    public bool TriggerRightButtonDrag
    {
        get => _settings.QuickPanelMouseTriggers.RightButtonDrag;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.RightButtonDrag = value);
    }

    public bool TriggerHorizontalWheel
    {
        get => _settings.QuickPanelMouseTriggers.HorizontalWheel;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.HorizontalWheel = value);
    }



    public bool ExecuteOnButtonRelease
    {
        get => _settings.QuickPanelMouseTriggers.ExecuteOnButtonRelease;
        set => UpdateQuickPanelMouseTrigger(value, trigger => trigger.ExecuteOnButtonRelease = value);
    }

    public bool EnableRadialMenu
    {
        get => _settings.RadialMenu.Enabled;
        set => UpdateRadialMenu(value, settings => settings.Enabled = value);
    }

    public bool EnableRadialCapsLockHold
    {
        get => _settings.RadialMenu.TriggerCapsLockHold;
        set => UpdateRadialMenu(value, settings => settings.TriggerCapsLockHold = value);
    }

    public string RadialMenuSummary => _settings.RadialMenu.Enabled
        ? "燕环已启用：右键按住移动或按住 CapsLock 触发，支持滚轮切页、子环和搜索配置。"
        : "燕环未启用：当前仍使用传统鼠标面板。";

    public string SelectedRadialMenuPageId
    {
        get
        {
            _settings.RadialMenu ??= new RadialMenuSettings();
            _settings.RadialMenu.Pages ??= [];
            if (_settings.RadialMenu.Pages.Count == 0)
            {
                return string.Empty;
            }

            if (_settings.RadialMenu.Pages.Any(page => page.Id.Equals(_settings.RadialMenu.SelectedPageId, StringComparison.OrdinalIgnoreCase)))
            {
                return _settings.RadialMenu.SelectedPageId;
            }

            _settings.RadialMenu.SelectedPageId = _settings.RadialMenu.Pages[0].Id;
            return _settings.RadialMenu.SelectedPageId;
        }
        set
        {
            value ??= string.Empty;
            if (_isRefreshingRadialMenu ||
                string.IsNullOrWhiteSpace(value) ||
                value == _settings.RadialMenu.SelectedPageId)
            {
                return;
            }

            SaveRadialMenuSlots();
            _settings.RadialMenu.SelectedPageId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedRadialMenuPageName));
            RefreshRadialMenuSlots();
        }
    }

    public string SelectedRadialMenuPageName =>
        _settings.RadialMenu?.Pages?.FirstOrDefault(page => page.Id.Equals(SelectedRadialMenuPageId, StringComparison.OrdinalIgnoreCase))?.Name
        ?? "默认";

    public bool EnableYarnSelect
    {
        get => _settings.YarnSelect.Enabled;
        set => UpdateYarnSelect(value, settings => settings.Enabled = value);
    }

    public bool YarnSelectCopy
    {
        get => _settings.YarnSelect.LeftCToCopy;
        set => UpdateYarnSelect(value, settings => settings.LeftCToCopy = value);
    }

    public bool YarnSelectCut
    {
        get => _settings.YarnSelect.LeftXToCut;
        set => UpdateYarnSelect(value, settings => settings.LeftXToCut = value);
    }

    public bool YarnSelectPaste
    {
        get => _settings.YarnSelect.LeftVToPaste;
        set => UpdateYarnSelect(value, settings => settings.LeftVToPaste = value);
    }

    public bool YarnSelectSearch
    {
        get => _settings.YarnSelect.LeftSToSearch;
        set => UpdateYarnSelect(value, settings => settings.LeftSToSearch = value);
    }

    public bool YarnSelectRun
    {
        get => _settings.YarnSelect.LeftRToRun;
        set => UpdateYarnSelect(value, settings => settings.LeftRToRun = value);
    }

    public bool YarnSelectSmartCopyPaste
    {
        get => _settings.YarnSelect.LeftRightSmartCopyPaste;
        set => UpdateYarnSelect(value, settings => settings.LeftRightSmartCopyPaste = value);
    }

    public bool YarnSelectSidePaste
    {
        get => _settings.YarnSelect.LeftSideButtonPaste;
        set => UpdateYarnSelect(value, settings => settings.LeftSideButtonPaste = value);
    }

    public string YarnSelectBlacklistedProcessesText
    {
        get => string.Join(", ", _settings.YarnSelect.BlacklistedProcesses ?? []);
        set
        {
            _settings.YarnSelect.BlacklistedProcesses = (value ?? string.Empty)
                .Split([',', ';', '，', '；', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            OnPropertyChanged();
        }
    }

    public string YarnSelectSummary
    {
        get
        {
            if (!_settings.YarnSelect.Enabled)
            {
                return "燕选已关闭。";
            }

            var labels = (_settings.YarnSelect.Rules ?? [])
                .Where(static rule => rule.Enabled)
                .Select(rule => $"左键+{rule.TriggerKey} {GetYarnSelectActionLabel(rule.ActionType)}")
                .ToList();
            return labels.Count == 0 ? "燕选已启用，但没有开启任何动作。" : string.Join("、", labels);
        }
    }

    public string QuickPanelTriggerSummary
    {
        get
        {
            var labels = new List<string>();
            var trigger = _settings.QuickPanelMouseTriggers;
            if (trigger.MiddleButtonDown) labels.Add("按下中键");
            if (trigger.X1ButtonDown) labels.Add("按下 X1 键");
            if (trigger.X2ButtonDown) labels.Add("按下 X2 键");
            if (trigger.CtrlLeftClick) labels.Add("Ctrl+左键单击");
            if (trigger.CtrlRightClick) labels.Add("Ctrl+右键单击");
            if (trigger.MiddleButtonLongPress) labels.Add("长按中键");
            if (trigger.RightButtonLongPress) labels.Add("长按右键");
            if (trigger.RightButtonDrag) labels.Add("按右键移动");
            if (trigger.HorizontalWheel) labels.Add("滚轮左右");

            return labels.Count == 0 ? "未启用鼠标触发，默认回退为长按中键。" : string.Join("、", labels);
        }
    }

    public string SelectedSectionTitle => SelectedNavigation?.Title ?? "Settings";

    public string SelectedSectionDescription => SelectedNavigation?.Key switch
    {
        "general" => "控制燕子(Swallow)的基础行为，包括启动同步和托盘停驻策略。",
        "ai" => "配置 AI 对话使用的本地或远程兼容接口，包括地址、Key 和模型名。",
        "sync" => "管理云账号状态、同步入口和当前服务端连接信息。",
        "extensions" => "查看本地扩展目录和当前机器已发现的扩展数量。",
        "recycle" => "查看已删除扩展，支持恢复和彻底删除。",
        "shortcuts" => "查看和管理主程序与扩展的全局快捷键。",
        "quickpanel" => "控制悬浮网格的操作面板，包括触发逻辑和槽位预设。",
        "yarnselect" => "按住左键选中文本时，用字母或鼠标键快速复制、搜索、运行或粘贴。",
        "about" => "查看当前版本与这套设置窗口的结构定位。",
        _ => "燕子设置"
    };

    public bool IsGeneralSelected => SelectedNavigation?.Key == "general";

    public bool IsAiSelected => SelectedNavigation?.Key == "ai";

    public bool IsSyncSelected => SelectedNavigation?.Key == "sync";

    public bool IsExtensionsSelected => SelectedNavigation?.Key == "extensions";

    public bool IsRecycleBinSelected => SelectedNavigation?.Key == "recycle";

    public bool IsShortcutsSelected => SelectedNavigation?.Key == "shortcuts";

    public bool IsQuickPanelSelected => SelectedNavigation?.Key == "quickpanel";

    public bool IsYarnSelectSelected => SelectedNavigation?.Key == "yarnselect";

    public bool IsAboutSelected => SelectedNavigation?.Key == "about";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NavigateTo(string? sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            return;
        }

        var target = NavigationItems.FirstOrDefault(item =>
            item.Key.Equals(sectionKey, StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            SelectedNavigation = target;
        }
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshAccountSummary();
        RefreshQuickPanelTriggerBindings();
        RefreshYarnSelectBindings();
        RefreshRadialMenuSlots();
        SyncStatusText = _mainWindow.SyncStatus;
        RefreshVisibleSectionData();
    }

    private void SettingsWindow_Activated(object? sender, EventArgs e)
    {
        if (_isRenamingRadialMenuPage)
        {
            HostAssets.AppendLog("Settings activated skipped during radial page rename.");
            return;
        }

        _settings = AppSettingsStore.Load();
        _settings.YarnSelect ??= new YarnSelectSettings();
        OnPropertyChanged(nameof(LaunchAtStartup));
        OnPropertyChanged(nameof(RefreshCloudOnStartup));
        OnPropertyChanged(nameof(CloseToTray));
        LauncherHotkey = _settings.LauncherHotkey;
        RefreshQuickPanelTriggerBindings();
        RefreshYarnSelectBindings();
        RefreshRadialMenuSlots();
        EnableWebDavSync = _settings.EnableWebDavSync;
        WebDavServerUrl = string.IsNullOrWhiteSpace(_settings.WebDavServerUrl) ? "https://dav.jianguoyun.com/dav/" : _settings.WebDavServerUrl;
        WebDavRootPath = _settings.WebDavRootPath;
        WebDavUsername = _settings.WebDavUsername;
        AiBaseUrl = _settings.AiBaseUrl;
        AiApiKey = _settings.AiApiKey;
        AiModel = _settings.AiModel;
        AiSettingsStatusText = BuildAiSettingsSummary(_settings);
        
        // 加载已保存的密码
        var credential = WebDavCredentialStore.Load();
        if (credential != null && !string.IsNullOrWhiteSpace(credential.Password))
        {
            WebDavPasswordBox.Password = credential.Password;
        }
        else
        {
            WebDavPasswordBox.Password = string.Empty;
        }
        
        RefreshAccountSummary();
        RefreshWebDavSummary();
        SyncStatusText = _mainWindow.SyncStatus;
        RefreshVisibleSectionData();
    }

    private void RefreshVisibleSectionData()
    {
        if (IsExtensionsSelected)
        {
            _ = RefreshExtensionsFromDiskAsync();
            return;
        }

        if (IsRecycleBinSelected)
        {
            _ = RefreshExtensionsFromDiskAsync();
            return;
        }

        if (IsShortcutsSelected)
        {
            RefreshExtensionCacheFromMainWindow();
            RefreshShortcutItems();
            return;
        }

        if (IsSyncSelected)
        {
            RefreshSyncActivityLog();
        }
    }

    private static string BuildAiSettingsSummary(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.AiApiKey) ||
            string.IsNullOrWhiteSpace(settings.AiModel))
        {
            return "尚未配置 AI。首次使用前请填写服务地址、API Key 和模型名。";
        }

        return $"当前使用 {settings.AiModel} · {settings.AiBaseUrl}";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractiveSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        BeginWindowDrag();
    }

    private void WindowFrame_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractiveSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.GetPosition(this).Y > 64)
        {
            return;
        }

        BeginWindowDrag();
    }

    private void BeginWindowDrag()
    {
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw if the mouse button is released before WPF starts the drag loop.
        }
    }

    private void ResizeBottomRightThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(rightDelta: e.HorizontalChange, bottomDelta: e.VerticalChange);
    }

    private void ResizeTopThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(topDelta: e.VerticalChange);
    }

    private void ResizeBottomThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(bottomDelta: e.VerticalChange);
    }

    private void ResizeLeftThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(leftDelta: e.HorizontalChange);
    }

    private void ResizeRightThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(rightDelta: e.HorizontalChange);
    }

    private void ResizeTopLeftThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(leftDelta: e.HorizontalChange, topDelta: e.VerticalChange);
    }

    private void ResizeTopRightThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(rightDelta: e.HorizontalChange, topDelta: e.VerticalChange);
    }

    private void ResizeBottomLeftThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWindow(leftDelta: e.HorizontalChange, bottomDelta: e.VerticalChange);
    }

    private void ResizeWindow(double leftDelta = 0, double topDelta = 0, double rightDelta = 0, double bottomDelta = 0)
    {
        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        var newLeft = Left;
        var newTop = Top;
        var newWidth = Width;
        var newHeight = Height;

        if (leftDelta != 0)
        {
            var targetWidth = Math.Max(MinWidth, Width - leftDelta);
            newLeft = Left + (Width - targetWidth);
            newWidth = targetWidth;
        }

        if (topDelta != 0)
        {
            var targetHeight = Math.Max(MinHeight, Height - topDelta);
            newTop = Top + (Height - targetHeight);
            newHeight = targetHeight;
        }

        if (rightDelta != 0)
        {
            newWidth = Math.Max(MinWidth, newWidth + rightDelta);
        }

        if (bottomDelta != 0)
        {
            newHeight = Math.Max(MinHeight, newHeight + bottomDelta);
        }

        Left = newLeft;
        Top = newTop;
        Width = newWidth;
        Height = newHeight;
        PersistWindowBounds();
    }

    private void ApplySavedWindowBounds()
    {
        var settings = _settings;
        if (settings.SettingsWindowWidth is not > 0 || settings.SettingsWindowHeight is not > 0)
        {
            return;
        }

        _suppressWindowBoundsPersistence = true;
        try
        {
            Width = Math.Max(MinWidth, settings.SettingsWindowWidth.Value);
            Height = Math.Max(MinHeight, settings.SettingsWindowHeight.Value);

            if (settings.SettingsWindowLeft.HasValue)
            {
                Left = settings.SettingsWindowLeft.Value;
            }

            if (settings.SettingsWindowTop.HasValue)
            {
                Top = settings.SettingsWindowTop.Value;
            }
        }
        finally
        {
            _suppressWindowBoundsPersistence = false;
        }
    }

    private void SettingsWindow_BoundsChanged(object? sender, EventArgs e)
    {
        PersistWindowBounds();
    }

    private void SettingsWindow_Closing(object? sender, CancelEventArgs e)
    {
        PersistWindowBounds();
    }

    private void PersistWindowBounds()
    {
        if (_suppressWindowBoundsPersistence || WindowState != WindowState.Normal)
        {
            return;
        }

        _settings = _settings with
        {
            SettingsWindowLeft = Left,
            SettingsWindowTop = Top,
            SettingsWindowWidth = Width,
            SettingsWindowHeight = Height
        };
        AppSettingsStore.Save(_settings);
    }

    private void SettingsSearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox && string.IsNullOrEmpty(textBox.Text))
        {
            textBox.CaretIndex = 0;
        }
    }

    private void SaveSettingsToggle_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsStore.Save(_settings);
        _mainWindow.RefreshAppSettings();
        StartupRegistrationService.Apply(_settings.LaunchAtStartup);
    }

    private void SaveQuickPanelTrigger_Click(object sender, RoutedEventArgs e)
    {
        SaveQuickPanelTriggerSettings();
    }

    private void AccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu != null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.IsOpen = true;
        }
    }

    private async void SignInMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await SignInAsync();
    }

    private async void SignOutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await SignOutAsync();
    }

    private async void RefreshAccountMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCloudAsync();
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        await SignInAsync();
    }

    private async void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        await SignOutAsync();
    }

    private async void RefreshSyncStatusButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCloudAsync();
    }

    private void RefreshSyncLogButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshSyncActivityLog();
    }

    private void SaveWebDavSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.SaveWebDavSettings(EnableWebDavSync, WebDavServerUrl, WebDavRootPath, WebDavUsername);
        
        // 保存密码
        var password = WebDavPasswordBox.Password;
        if (!string.IsNullOrWhiteSpace(password))
        {
            _mainWindow.SaveWebDavCredential(WebDavUsername, password);
        }
        
        _settings = AppSettingsStore.Load();
        RefreshWebDavSummary();
        SyncStatusText = "WebDAV 配置已保存。";
        RefreshSyncActivityLog();
    }

    private void SaveAiSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.SaveAiSettings(AiBaseUrl, AiApiKey, AiModel);
        _settings = AppSettingsStore.Load();
        AiBaseUrl = _settings.AiBaseUrl;
        AiApiKey = _settings.AiApiKey;
        AiModel = _settings.AiModel;
        AiSettingsStatusText = BuildAiSettingsSummary(_settings);
        SyncStatusText = string.IsNullOrWhiteSpace(_settings.AiBaseUrl) ||
                         string.IsNullOrWhiteSpace(_settings.AiApiKey) ||
                         string.IsNullOrWhiteSpace(_settings.AiModel)
            ? "AI 配置已清空。"
            : "AI 配置已保存。";
    }

    private void WebDavPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // 密码改变时更新状态
        RefreshWebDavSummary();
    }

    private void SetWebDavCredentialButton_Click(object sender, RoutedEventArgs e)
    {
        var username = WebDavUsername.Trim();
        var requireUsername = string.IsNullOrWhiteSpace(username);
        if (requireUsername)
        {
            System.Windows.MessageBox.Show(this, "请先在上一层填写 WebDAV 用户名，再设置应用密码。", "缺少用户名", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new WebDavCredentialWindow(username, requireUsername: false)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        WebDavUsername = dialog.Username;
        _mainWindow.SaveWebDavCredential(dialog.Username, dialog.Password);
        RefreshWebDavSummary();
        SyncStatusText = "WebDAV 凭据已保存。";
    }

    private async void TestWebDavButton_Click(object sender, RoutedEventArgs e)
    {
        SaveWebDavSettingsButton_Click(sender, e);
        var result = await _mainWindow.ProbeWebDavAsync();
        WebDavStatusText = result.message;
        RefreshSyncActivityLog();
        if (!result.ok)
        {
            System.Windows.MessageBox.Show(this, result.message, "WebDAV 测试失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void SyncWebDavButton_Click(object sender, RoutedEventArgs e)
    {
        SaveWebDavSettingsButton_Click(sender, e);
        var result = await _mainWindow.SyncWebDavNowAsync();
        WebDavStatusText = result.message;
        await RefreshExtensionsFromDiskAsync();
        RefreshSyncActivityLog();
        if (!result.ok)
        {
            System.Windows.MessageBox.Show(this, result.message, "WebDAV 同步失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenExtensionsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = ExtensionsRootPath,
            UseShellExecute = true
        });
    }

    private async void RefreshExtensionStatsButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshExtensionsFromDiskAsync();
    }

    private async void RefreshRecycleBinButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshExtensionsFromDiskAsync();
    }

    private void OpenExtensionDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item })
        {
            return;
        }

        if (!Directory.Exists(item.DirectoryPath))
        {
            System.Windows.MessageBox.Show(this, "扩展目录不存在。", "打开目录失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            _ = RefreshExtensionsFromDiskAsync();
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = item.DirectoryPath,
            UseShellExecute = true
        });
    }

    private async void EditExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item })
        {
            return;
        }

        var result = await _mainWindow.EditExtensionFromSettingsAsync(item.ExtensionId, this);
        if (!string.IsNullOrWhiteSpace(result.message))
        {
            SyncStatusText = result.message;
        }

        if (!result.ok)
        {
            if (!string.IsNullOrWhiteSpace(result.message))
            {
                System.Windows.MessageBox.Show(this, result.message, "编辑扩展失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        _settings = _mainWindow.GetCurrentAppSettings();
        RefreshExtensionCacheFromMainWindow();
        RefreshExtensionSummary();
        RefreshExtensionItems();
        RefreshShortcutItems();
    }

    private async void DeleteExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item })
        {
            return;
        }

        var result = await _mainWindow.DeleteExtensionFromSettingsAsync(item.ExtensionId, this);
        if (!string.IsNullOrWhiteSpace(result.message))
        {
            SyncStatusText = result.message;
        }

        if (!result.ok)
        {
            if (!string.IsNullOrWhiteSpace(result.message))
            {
                System.Windows.MessageBox.Show(this, result.message, "删除扩展失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return;
        }

        ExtensionItems.Remove(item);
        _settings = _mainWindow.GetCurrentAppSettings();
        RefreshExtensionCacheFromMainWindow();
        RefreshExtensionSummary();
        OnPropertyChanged(nameof(ExtensionSearchSummary));
        RefreshShortcutItems();
        await RefreshExtensionsFromDiskAsync();
    }

    private async void RestoreRecycleBinExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsRecycleBinItem item } || item.IsOperationBusy)
        {
            return;
        }

        item.IsRestoring = true;
        try
        {
            var result = await _mainWindow.RestoreExtensionFromRecycleBinAsync(item.ItemId);
            if (!string.IsNullOrWhiteSpace(result.message))
            {
                SyncStatusText = result.message;
            }

            if (!result.ok)
            {
                System.Windows.MessageBox.Show(this, result.message, "恢复扩展失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RefreshExtensionsFromDiskAsync();
            if (System.Windows.Application.Current is App app)
            {
                app.ShowDesktopNotification("扩展已恢复", $"{item.Title} 已从回收站恢复。");
            }
        }
        finally
        {
            item.IsRestoring = false;
        }
    }

    private async void DeleteRecycleBinExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsRecycleBinItem item } || item.IsOperationBusy)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            this,
            $"确认彻底删除“{item.Title}”吗？这会清空回收站中的本地副本，无法恢复。",
            "彻底删除扩展",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        item.IsDeletingPermanently = true;
        try
        {
            var result = await _mainWindow.PurgeExtensionFromRecycleBinAsync(item.ItemId);
            if (!string.IsNullOrWhiteSpace(result.message))
            {
                SyncStatusText = result.message;
            }

            if (!result.ok)
            {
                System.Windows.MessageBox.Show(this, result.message, "彻底删除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RefreshExtensionsFromDiskAsync();
            if (System.Windows.Application.Current is App app)
            {
                app.ShowDesktopNotification("回收站扩展已清理", $"{item.Title} 已从回收站彻底删除。");
            }
        }
        finally
        {
            item.IsDeletingPermanently = false;
        }
    }

    private async void PublishExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item } || item.IsOperationBusy)
        {
            return;
        }

        item.IsPublishing = true;
        try
        {
            var result = await _mainWindow.PublishExtensionFromSettingsAsync(item.ExtensionId);
            if (!string.IsNullOrWhiteSpace(result.message))
            {
                SyncStatusText = result.message;
            }

            if (!result.ok)
            {
                System.Windows.MessageBox.Show(this, result.message, "发布扩展失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RefreshExtensionsFromDiskAsync();
            if (System.Windows.Application.Current is App app)
            {
                app.ShowDesktopNotification("扩展已发布到商店", $"{item.Title} 已完成发布，可在扩展商店查看。");
            }
        }
        finally
        {
            item.IsPublishing = false;
        }
    }

    private async void UnpublishExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsExtensionItem item } || item.IsOperationBusy)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            this,
            $"确认下线扩展“{item.Title}”吗？下线后扩展商店将不再展示它。",
            "确认下线扩展",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        item.IsUnpublishing = true;
        try
        {
            var result = await _mainWindow.UnpublishExtensionFromSettingsAsync(item.ExtensionId);
            if (!string.IsNullOrWhiteSpace(result.message))
            {
                SyncStatusText = result.message;
            }

            if (!result.ok)
            {
                System.Windows.MessageBox.Show(this, result.message, "下线扩展失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RefreshExtensionsFromDiskAsync();
            if (System.Windows.Application.Current is App app)
            {
                app.ShowDesktopNotification("扩展已从商店下线", $"{item.Title} 已从扩展商店移除。");
            }
        }
        finally
        {
            item.IsUnpublishing = false;
        }
    }

    private async Task RefreshExtensionsFromDiskAsync()
    {
        if (IsExtensionsLoading)
        {
            return;
        }

        var refreshVersion = ++_extensionsRefreshVersion;
        var startedAt = Stopwatch.StartNew();
        IsExtensionsLoading = true;
        LocalExtensionSummary = "正在后台刷新扩展数据...";
        HostAssets.AppendLog($"Settings extensions refresh started: version={refreshVersion}");

        try
        {
            var publishedMap = await _mainWindow.GetOwnedPublishedExtensionsForSettingsAsync();
            HostAssets.AppendLog($"Settings extensions refresh cloud publish map count={publishedMap.Count}");
            var data = await Task.Run(() =>
            {
                var backgroundStartedAt = Stopwatch.StartNew();
                LocalExtensionCatalog.EnsureSampleExtension();
                var entries = LocalExtensionCatalog.LoadEntries()
                    .ToList();
                var recycleBinItems = _mainWindow.GetRecycleBinEntriesForSettings()
                    .Select(item => new SettingsRecycleBinItem(
                        item.ItemId,
                        item.ExtensionId,
                        item.Title,
                        item.Category,
                        item.Version,
                        item.DeletedAtUtc))
                    .ToList();
                var settings = _mainWindow.GetCurrentAppSettings();
                settings.DisabledExtensionIds ??= [];
                var disabledIds = new HashSet<string>(settings.DisabledExtensionIds, StringComparer.OrdinalIgnoreCase);
                var extensionItems = entries
                    .Select(entry => new
                    {
                        entry.Manifest.Id,
                        entry.Manifest.Name,
                        Category = entry.Manifest.Category ?? "扩展",
                        Version = entry.Manifest.Version ?? "0.1.0",
                        DirectoryPath = Path.GetDirectoryName(entry.ManifestPath) ?? string.Empty
                    })
                    .OrderBy(static item => item.Category, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(item =>
                    {
                        publishedMap.TryGetValue(item.Id, out var cloudRecord);
                        return new SettingsExtensionItem(
                            item.Id,
                            item.Name,
                            item.Category,
                            item.Version,
                            item.DirectoryPath,
                            item.Category.Contains("网页搜索", StringComparison.OrdinalIgnoreCase) ? "网页搜索扩展" : "本地扩展",
                            true,
                            !disabledIds.Contains(item.Id),
                            cloudRecord?.IsPublished != 0,
                            cloudRecord?.PublisherUsername ?? string.Empty);
                    })
                    .ToList();
                var shortcutItems = entries
                    .Select(entry => new
                    {
                        entry.Manifest.Id,
                        entry.Manifest.Name,
                        Category = entry.Manifest.Category ?? "扩展",
                        entry.Manifest.GlobalShortcut
                    })
                    .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new SettingsShortcutItem(
                        item.Id,
                        item.Name,
                        item.Category,
                        item.GlobalShortcut))
                    .ToList();
                HostAssets.AppendLog(
                    $"Settings extensions refresh background prepared: version={refreshVersion}, " +
                    $"entries={entries.Count}, extensionItems={extensionItems.Count}, recycleBinItems={recycleBinItems.Count}, shortcutItems={shortcutItems.Count}, " +
                    $"elapsedMs={backgroundStartedAt.ElapsedMilliseconds}");
                return (entries, extensionItems, recycleBinItems, shortcutItems);
            });

            if (refreshVersion != _extensionsRefreshVersion)
            {
                HostAssets.AppendLog($"Settings extensions refresh skipped stale result: version={refreshVersion}");
                return;
            }

            var uiApplyStartedAt = Stopwatch.StartNew();
            await Dispatcher.InvokeAsync(() =>
            {
                _mainWindow.ReloadLocalExtensionsFromEntries(data.entries, "已刷新本地扩展。");
                _cachedExtensionItems = data.extensionItems;
                _cachedRecycleBinItems = data.recycleBinItems;
                if (IsExtensionsSelected)
                {
                    RefreshExtensionSummary();
                    RefreshExtensionItems();
                }

                if (IsRecycleBinSelected)
                {
                    RefreshRecycleBinSummary();
                    RefreshRecycleBinItems();
                }

                if (IsShortcutsSelected)
                {
                    ShortcutItems.Clear();
                    foreach (var item in data.shortcutItems)
                    {
                        ShortcutItems.Add(item);
                    }
                }
            }, DispatcherPriority.Background);
            HostAssets.AppendLog(
                $"Settings extensions refresh UI applied: version={refreshVersion}, elapsedMs={uiApplyStartedAt.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Settings extensions refresh failed: version={refreshVersion}, error={ex.Message}");
            LocalExtensionSummary = $"刷新扩展失败：{ex.Message}";
        }
        finally
        {
            if (refreshVersion == _extensionsRefreshVersion)
            {
                IsExtensionsLoading = false;
            }

            HostAssets.AppendLog(
                $"Settings extensions refresh finished: version={refreshVersion}, totalElapsedMs={startedAt.ElapsedMilliseconds}");
        }
    }

    private void RefreshWebDavSummary()
    {
        WebDavStatusText = !EnableWebDavSync
            ? "未启用个人扩展同步。"
            : _mainWindow.HasWebDavCredential()
                ? $"已配置：{WebDavServerUrl} {WebDavRootPath}"
                : "已启用，但还未设置 WebDAV 密码。";
    }

    public void RefreshWebDavConfigFromExternal()
    {
        _settings = AppSettingsStore.Load();
        EnableWebDavSync = _settings.EnableWebDavSync;
        WebDavServerUrl = string.IsNullOrWhiteSpace(_settings.WebDavServerUrl) 
            ? "https://dav.jianguoyun.com/dav/" 
            : _settings.WebDavServerUrl;
        WebDavRootPath = _settings.WebDavRootPath;
        WebDavUsername = _settings.WebDavUsername;
        
        // Load password from credential store
        var credential = WebDavCredentialStore.Load();
        if (credential != null && !string.IsNullOrWhiteSpace(credential.Password))
        {
            WebDavPasswordBox.Password = credential.Password;
        }
        else
        {
            WebDavPasswordBox.Password = string.Empty;
        }
        
        RefreshWebDavSummary();
        SyncStatusText = "WebDAV 配置已从云端同步。";
        RefreshSyncActivityLog();
    }

    public void RefreshAiConfigFromExternal()
    {
        _settings = AppSettingsStore.Load();
        AiBaseUrl = _settings.AiBaseUrl;
        AiApiKey = _settings.AiApiKey;
        AiModel = _settings.AiModel;
        AiSettingsStatusText = BuildAiSettingsSummary(_settings);
    }

    private void EditLauncherHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HotkeyCaptureWindow(
            "设置主程序快捷键",
            "窗口激活后，直接按一次新的组合键即可完成录制。也支持全局双击 Ctrl 或双击 Alt 呼出主界面。",
            LauncherHotkey,
            allowDoubleTap: true)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (_mainWindow.TryUpdateLauncherHotkey(dialog.ShortcutText, out var message))
        {
            LauncherHotkey = _mainWindow.GetLauncherHotkey();
            SyncStatusText = message;
            RefreshSyncActivityLog();
            return;
        }

        System.Windows.MessageBox.Show(this, message, "快捷键设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ResetLauncherHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow.TryUpdateLauncherHotkey("Alt+Space", out var message))
        {
            LauncherHotkey = _mainWindow.GetLauncherHotkey();
            SyncStatusText = message;
            RefreshSyncActivityLog();
            return;
        }

        System.Windows.MessageBox.Show(this, message, "快捷键设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void EditShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsShortcutItem item })
        {
            return;
        }

        var dialog = new HotkeyCaptureWindow(
            "设置扩展快捷键",
            $"窗口激活后，直接按一次新的组合键即可为 {item.Title} 完成录制。",
            item.ShortcutValue,
            allowEmpty: true)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = await _mainWindow.UpdateExtensionShortcutFromSettingsAsync(item.ExtensionId, dialog.ShortcutText);
        SyncStatusText = result.message;
        if (!result.ok)
        {
            System.Windows.MessageBox.Show(this, result.message, "快捷键设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RefreshShortcutItems();
        RefreshExtensionSummary();
    }

    private async void ClearShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SettingsShortcutItem item })
        {
            return;
        }

        var result = await _mainWindow.UpdateExtensionShortcutFromSettingsAsync(item.ExtensionId, null);
        SyncStatusText = result.message;
        if (!result.ok)
        {
            System.Windows.MessageBox.Show(this, result.message, "快捷键清除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RefreshShortcutItems();
        RefreshExtensionSummary();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async Task SignInAsync()
    {
        var ok = await _mainWindow.PromptLoginFromSettingsAsync();
        RefreshAccountSummary();
        if (ok)
        {
            await _mainWindow.RefreshCloudFromSettingsAsync();
            RefreshWebDavConfigFromExternal();
            SyncStatusText = _mainWindow.SyncStatus;
            RefreshSyncActivityLog();
        }
    }

    private void ExtensionEnabledSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { DataContext: SettingsExtensionItem item } checkbox)
        {
            return;
        }

        _mainWindow.SetExtensionEnabled(item.ExtensionId, checkbox.IsChecked == true);
        _settings = _mainWindow.GetCurrentAppSettings();
        RefreshExtensionCacheFromMainWindow();
        RefreshExtensionSummary();
        RefreshExtensionItems();
    }

    private async Task SignOutAsync()
    {
        _mainWindow.SignOutFromSettings();
        ClearWebDavConfiguration();
        RefreshAccountSummary();
        SyncStatusText = _mainWindow.SyncStatus;
        RefreshSyncActivityLog();
        await Task.CompletedTask;
    }

    private void RefreshSyncActivityLog()
    {
        try
        {
            if (!File.Exists(HostAssets.HostLogPath))
            {
                SyncActivityLogText = "暂无同步记录。";
                return;
            }

            var lines = ReadLogTailLines(HostAssets.HostLogPath, 512 * 1024)
                .Where(static line =>
                    line.Contains("sync", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("webdav", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("cloud", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("登录", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("账号", StringComparison.OrdinalIgnoreCase))
                .TakeLast(40)
                .ToArray();

            SyncActivityLogText = lines.Length == 0
                ? "暂无同步记录。"
                : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            SyncActivityLogText = $"读取同步记录失败：{ex.Message}";
        }
    }

    private static IEnumerable<string> ReadLogTailLines(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        if (length <= 0)
        {
            return [];
        }

        var bytesToRead = (int)Math.Min(length, maxBytes);
        stream.Seek(-bytesToRead, SeekOrigin.End);
        using var reader = new StreamReader(stream);
        if (bytesToRead < length)
        {
            _ = reader.ReadLine();
        }

        var content = reader.ReadToEnd();
        return content.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries);
    }

    private void ClearWebDavConfiguration()
    {
        // Clear UI-bound properties
        EnableWebDavSync = false;
        WebDavServerUrl = string.Empty;
        WebDavRootPath = string.Empty;
        WebDavUsername = string.Empty;
        WebDavPasswordBox.Password = string.Empty;
        
        // Save cleared settings to persistent storage
        _mainWindow.SaveWebDavSettings(false, string.Empty, string.Empty, string.Empty);
        
        // Clear stored credential
        WebDavCredentialStore.Clear();
        
        // Update UI status
        RefreshWebDavSummary();
        SyncStatusText = "已退出登录，WebDAV 配置已清除。";
    }

    private async Task RefreshCloudAsync()
    {
        await _mainWindow.RefreshCloudFromSettingsAsync();
        RefreshAccountSummary();
        RefreshWebDavConfigFromExternal();
        SyncStatusText = _mainWindow.SyncStatus;
    }

    private void RefreshAccountSummary()
    {
        var session = SyncSessionStore.Load();
        HostAssets.AppendLog($"Settings RefreshAccountSummary: sessionExists={session != null}, sessionExpired={session != null && session.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        if (session != null && session.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            IsAccountLoggedIn = true;
            AccountTitle = session.Username;
            AccountSubtitle = $"已登录 Cloud · 用户 ID {session.UserId}";
            AccountInitial = session.Username[..1].ToUpperInvariant();
            return;
        }

        IsAccountLoggedIn = false;
        AccountTitle = "未登录";
        AccountSubtitle = "点击左上角账户卡片登录或切换账号。";
        AccountInitial = "燕";
    }

    public void RefreshAccountFromExternal()
    {
        RefreshAccountSummary();
        SyncStatusText = _mainWindow.SyncStatus;
    }

    private void RefreshExtensionSummary()
    {
        var count = _cachedExtensionItems.Count > 0
            ? _cachedExtensionItems.Count
            : _mainWindow.GetExtensionsForSettings().Count;
        LocalExtensionSummary = $"当前机器已发现 {count} 个扩展。";
        OnPropertyChanged(nameof(ExtensionSearchSummary));
    }

    private void RefreshRecycleBinSummary()
    {
        var count = _cachedRecycleBinItems.Count;
        RecycleBinSummary = count == 0
            ? "回收站为空。"
            : $"当前回收站中有 {count} 个扩展。";
        OnPropertyChanged(nameof(RecycleBinSearchSummary));
    }

    private void RefreshExtensionItems()
    {
        if (_cachedExtensionItems.Count == 0)
        {
            RefreshExtensionCacheFromMainWindow();
        }

        ExtensionItems.Clear();

        var keyword = ExtensionSearchText.Trim();
        var items = _cachedExtensionItems
            .Where(item =>
                string.IsNullOrWhiteSpace(keyword) ||
                item.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.ExtensionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.DirectoryPath.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var item in items)
        {
            ExtensionItems.Add(item);
        }

        OnPropertyChanged(nameof(ExtensionSearchSummary));
    }

    private void RefreshRecycleBinItems()
    {
        RecycleBinItems.Clear();

        var keyword = RecycleBinSearchText.Trim();
        var items = _cachedRecycleBinItems
            .Where(item =>
                string.IsNullOrWhiteSpace(keyword) ||
                item.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.ExtensionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var item in items)
        {
            RecycleBinItems.Add(item);
        }

        OnPropertyChanged(nameof(RecycleBinSearchSummary));
    }

    private void RefreshExtensionCacheFromMainWindow()
    {
        _cachedExtensionItems = _mainWindow.GetExtensionsForSettings()
            .Select(command => new SettingsExtensionItem(
                command.ExtensionId,
                command.Title,
                command.Category,
                command.DeclaredVersion,
                command.ExtensionDirectoryPath ?? string.Empty,
                command.Category.Contains("网页搜索", StringComparison.OrdinalIgnoreCase) ? "网页搜索扩展" : "本地扩展",
                command.Source == CommandSource.LocalExtension,
                _mainWindow.IsExtensionEnabled(command.ExtensionId),
                false,
                string.Empty))
            .ToList();
    }

    private void RefreshShortcutItems()
    {
        ShortcutItems.Clear();
        foreach (var command in _mainWindow.GetLocalExtensionsForSettings())
        {
            ShortcutItems.Add(new SettingsShortcutItem(
                command.ExtensionId,
                command.Title,
                command.Category,
                command.GlobalShortcut));
        }
    }

    private void ApplySettingsSearch(string query)
    {
        query = query.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var target = NavigationItems.FirstOrDefault(item => SettingsSearchMatches(item.Key, query));
        if (target != null)
        {
            SelectedNavigation = target;
        }
    }

    private static bool SettingsSearchMatches(string sectionKey, string query)
    {
        return GetSettingsSearchTerms(sectionKey)
            .Any(term => term.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         query.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] GetSettingsSearchTerms(string sectionKey) => sectionKey switch
    {
        "general" =>
        [
            "常规", "开机", "启动", "托盘", "关闭", "主程序", "快捷键", "general", "startup", "launch", "tray", "hotkey"
        ],
        "sync" =>
        [
            "同步", "云", "云同步", "账号", "登录", "注册", "坚果云", "webdav", "cloud", "cloudflare", "服务器", "密码", "配置"
        ],
        "extensions" =>
        [
            "扩展", "插件", "目录", "本地", "删除", "编辑", "搜索", "打开目录", "extension", "plugin", "folder", "delete", "edit"
        ],
        "recycle" =>
        [
            "回收站", "恢复", "彻底删除", "已删除", "扩展回收站", "recycle", "trash", "restore", "deleted"
        ],
        "shortcuts" =>
        [
            "快捷键", "热键", "组合键", "录制", "全局快捷键", "shortcut", "hotkey", "keyboard"
        ],
        "quickpanel" =>
        [
            "鼠标面板", "快捷面板", "面板", "鼠标", "右键", "中键", "x1", "x2", "长按", "滚轮", "松开", "quick panel", "mouse", "middle", "right click"
        ],
        "yarnselect" =>
        [
            "燕选", "左键辅助", "鼠标选中", "选中操作", "复制", "剪切", "粘贴", "搜索选中", "left button", "selection", "copy", "paste"
        ],
        "about" =>
        [
            "关于", "版本", "协议", "logo", "about", "version", "license"
        ],
        _ => [sectionKey]
    };

    private static bool IsInteractiveSource(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is System.Windows.Controls.TextBox or
                System.Windows.Controls.Primitives.ButtonBase or
                Selector or
                System.Windows.Controls.Primitives.ScrollBar or
                ResizeGrip)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void UpdateQuickPanelMouseTrigger(bool value, Action<QuickPanelMouseTriggerSettings> update)
    {
        _settings.QuickPanelMouseTriggers ??= new QuickPanelMouseTriggerSettings();
        update(_settings.QuickPanelMouseTriggers);
        OnPropertyChanged();
        OnPropertyChanged(nameof(QuickPanelTriggerSummary));
    }

    private void UpdateRadialMenu(bool value, Action<RadialMenuSettings> update)
    {
        _settings.RadialMenu ??= new RadialMenuSettings();
        update(_settings.RadialMenu);
        OnPropertyChanged();
        OnPropertyChanged(nameof(RadialMenuSummary));
    }

    private void SaveQuickPanelTriggerSettings()
    {
        SaveRadialMenuSlots();
        AppSettingsStore.Save(_settings);
        _mainWindow.RefreshAppSettings();
        _mainWindow.NotifyQuickPanelSettingsChanged("quickpanel-trigger-settings-saved");
        SyncStatusText = $"鼠标面板触发已保存：{QuickPanelTriggerSummary}";
    }

    private void SaveYarnSelectSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveYarnSelectSettings();
    }

    private void UpdateYarnSelect(bool value, Action<YarnSelectSettings> update)
    {
        _settings.YarnSelect ??= new YarnSelectSettings();
        update(_settings.YarnSelect);
        OnPropertyChanged();
        OnPropertyChanged(nameof(YarnSelectSummary));
    }

    private void SaveYarnSelectSettings()
    {
        _settings.YarnSelect ??= new YarnSelectSettings();
        _settings.YarnSelect.BlacklistedProcesses ??= [];
        _settings.YarnSelect.Rules = YarnSelectRules
            .Select(item => YarnSelectSettings.NormalizeRule(new YarnSelectRuleSettings
            {
                Enabled = item.Enabled,
                TriggerKey = item.TriggerKey,
                ActionType = item.ActionType,
                ExtensionId = ResolveYarnSelectExtensionId(item),
                Description = item.Description
            }))
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.TriggerKey))
            .DistinctBy(static rule => rule.TriggerKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        AppSettingsStore.Save(_settings);
        _mainWindow.RefreshAppSettings();
        SyncStatusText = $"燕选设置已保存：{YarnSelectSummary}";
        RefreshYarnSelectBindings();
    }

    private string ResolveYarnSelectExtensionId(YarnSelectRuleItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ExtensionId) &&
            YarnSelectExtensionOptions.Any(option => option.ExtensionId.Equals(item.ExtensionId, StringComparison.OrdinalIgnoreCase)))
        {
            return item.ExtensionId;
        }

        var searchText = (item.ExtensionSearchText ?? string.Empty).Trim();
        return YarnSelectExtensionOptions.FirstOrDefault(option =>
            option.Title.Equals(searchText, StringComparison.OrdinalIgnoreCase) ||
            option.ExtensionId.Equals(searchText, StringComparison.OrdinalIgnoreCase))
            ?.ExtensionId ?? string.Empty;
    }

    private void RefreshYarnSelectBindings()
    {
        _settings.YarnSelect ??= new YarnSelectSettings();
        _settings.YarnSelect.Rules ??= [];
        if (_settings.YarnSelect.Rules.Count == 0)
        {
            _settings.YarnSelect.Rules = YarnSelectSettings.CreateDefaultRulesFromLegacy(_settings.YarnSelect);
        }

        RefreshYarnSelectExtensionOptions();
        YarnSelectRules.Clear();
        foreach (var rule in _settings.YarnSelect.Rules.Select(YarnSelectSettings.NormalizeRule))
        {
            var item = new YarnSelectRuleItem(rule);
            ApplyYarnSelectExtensionSelection(item);
            YarnSelectRules.Add(item);
        }

        OnPropertyChanged(nameof(EnableYarnSelect));
        OnPropertyChanged(nameof(YarnSelectCopy));
        OnPropertyChanged(nameof(YarnSelectCut));
        OnPropertyChanged(nameof(YarnSelectPaste));
        OnPropertyChanged(nameof(YarnSelectSearch));
        OnPropertyChanged(nameof(YarnSelectRun));
        OnPropertyChanged(nameof(YarnSelectSmartCopyPaste));
        OnPropertyChanged(nameof(YarnSelectSidePaste));
        OnPropertyChanged(nameof(YarnSelectBlacklistedProcessesText));
        OnPropertyChanged(nameof(YarnSelectSummary));
    }

    private void RefreshYarnSelectExtensionOptions()
    {
        YarnSelectExtensionOptions.Clear();
        RadialMenuExtensionOptions.Clear();
        YarnSelectExtensionOptions.Add(new YarnSelectExtensionOption(string.Empty, "不绑定扩展"));
        foreach (var command in _mainWindow.GetLocalExtensionsForSettings())
        {
            var option = new YarnSelectExtensionOption(command);
            YarnSelectExtensionOptions.Add(option);
        }

        foreach (var command in _mainWindow.GetAllCommands()
                     .Where(IsRadialMenuCommandCandidate)
                     .DistinctBy(static command => command.ExtensionId, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static command => command.ItemKindLabel, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static command => command.Title, StringComparer.OrdinalIgnoreCase))
        {
            RadialMenuExtensionOptions.Add(new YarnSelectExtensionOption(command));
        }

        RefreshRadialMenuCommandCandidates(RadialMenuSearchText);
    }

    private void RefreshRadialMenuSlots()
    {
        if (_isRefreshingRadialMenu)
        {
            return;
        }

        try
        {
            _isRefreshingRadialMenu = true;
            RefreshYarnSelectExtensionOptions();
            _settings.RadialMenu ??= new RadialMenuSettings();
            _settings.RadialMenu.Pages ??= [];
            if (_settings.RadialMenu.Pages.Count == 0)
            {
                _settings.RadialMenu.Pages.Add(new RadialMenuPageSettings { Id = "default", Name = "默认" });
            }

            if (_settings.RadialMenu.Pages.All(page => !page.Id.Equals(_settings.RadialMenu.SelectedPageId, StringComparison.OrdinalIgnoreCase)))
            {
                _settings.RadialMenu.SelectedPageId = _settings.RadialMenu.Pages[0].Id;
            }

            RadialMenuPages.Clear();
            foreach (var page in _settings.RadialMenu.Pages)
            {
                RadialMenuPages.Add(new RadialMenuPageEditorItem(page.Id, page.Name));
            }

            RadialMenuChildPageOptions.Clear();
            RadialMenuChildPageOptions.Add(new RadialMenuPageEditorItem(string.Empty, "不进入子环"));
            foreach (var page in _settings.RadialMenu.Pages)
            {
                RadialMenuChildPageOptions.Add(new RadialMenuPageEditorItem(page.Id, page.Name));
            }

            var selectedPage = _settings.RadialMenu.Pages.First(page => page.Id.Equals(_settings.RadialMenu.SelectedPageId, StringComparison.OrdinalIgnoreCase));
            selectedPage.Slots ??= [];
            selectedPage.ChildPageIds ??= [];
            while (selectedPage.Slots.Count < 8) selectedPage.Slots.Add(null);
            while (selectedPage.ChildPageIds.Count < 8) selectedPage.ChildPageIds.Add(null);

            RadialMenuSlots.Clear();
            BuildRadialPreviewSeparators();
            var center = 180.0;
            var radius = 128.0;
            for (var index = 0; index < 8; index++)
            {
                var angle = (-90 + index * 45) * Math.PI / 180.0;
                RadialMenuSlots.Add(new RadialMenuSlotEditorItem(
                    index,
                    selectedPage.Slots.ElementAtOrDefault(index) ?? string.Empty,
                    selectedPage.ChildPageIds.ElementAtOrDefault(index) ?? string.Empty,
                    ResolveRadialExtensionTitle(selectedPage.Slots.ElementAtOrDefault(index)),
                    ResolveRadialChildPageTitle(selectedPage.ChildPageIds.ElementAtOrDefault(index)),
                    center + Math.Cos(angle) * radius - 52,
                    center + Math.Sin(angle) * radius - 32));
            }

            OnPropertyChanged(nameof(SelectedRadialMenuPageId));
            OnPropertyChanged(nameof(SelectedRadialMenuPageName));
        }
        finally
        {
            _isRefreshingRadialMenu = false;
        }
    }

    private void BuildRadialPreviewSeparators()
    {
        RadialMenuPreviewSeparators.Clear();
        const double center = 180.0;
        for (var index = 0; index < 8; index++)
        {
            var angle = (-112.5 + index * 45) * Math.PI / 180.0;
            RadialMenuPreviewSeparators.Add(new RadialSeparatorViewModel(
                center + Math.Cos(angle) * 46,
                center + Math.Sin(angle) * 46,
                center + Math.Cos(angle) * 180,
                center + Math.Sin(angle) * 180));
        }
    }

    private void SaveRadialMenuSlots()
    {
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];
        var selectedPage = _settings.RadialMenu.Pages.FirstOrDefault(page => page.Id.Equals(_settings.RadialMenu.SelectedPageId, StringComparison.OrdinalIgnoreCase));
        if (selectedPage == null)
        {
            return;
        }

        selectedPage.Slots = RadialMenuSlots
            .OrderBy(static item => item.Index)
            .Select(static item => string.IsNullOrWhiteSpace(item.ExtensionId) ? null : item.ExtensionId.Trim())
            .Cast<string?>()
            .Take(8)
            .ToList();
        selectedPage.ChildPageIds = RadialMenuSlots
            .OrderBy(static item => item.Index)
            .Select(static item => string.IsNullOrWhiteSpace(item.ChildPageId) ? null : item.ChildPageId.Trim())
            .Cast<string?>()
            .Take(8)
            .ToList();
        while (selectedPage.Slots.Count < 8)
        {
            selectedPage.Slots.Add(null);
        }

        while (selectedPage.ChildPageIds.Count < 8)
        {
            selectedPage.ChildPageIds.Add(null);
        }
        var firstPageSlots = _settings.RadialMenu.Pages[0].Slots ?? [];
        _settings.RadialMenu.Slots = firstPageSlots.Concat(Enumerable.Repeat<string?>(null, 8)).Take(8).ToList();
    }

    private static bool IsRadialMenuCommandCandidate(CommandItem command)
    {
        return command.Source is CommandSource.LocalExtension or CommandSource.WebSearch or CommandSource.Application or CommandSource.Local;
    }

    private void RefreshRadialMenuCommandCandidates(string? keyword)
    {
        FilteredRadialMenuCommandOptions.Clear();
        keyword = (keyword ?? string.Empty).Trim();
        var candidates = RadialMenuExtensionOptions.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            candidates = candidates.Where(option =>
                option.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                option.ExtensionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                option.Detail.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var option in candidates.Take(40))
        {
            FilteredRadialMenuCommandOptions.Add(option);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var fileResults = EverythingSearchService.Search(keyword, 20);
            if (fileResults.Success)
            {
                foreach (var result in fileResults.Results)
                {
                    var command = BuildRadialFileCommand(result);
                    if (FilteredRadialMenuCommandOptions.Any(option =>
                            option.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    FilteredRadialMenuCommandOptions.Add(new YarnSelectExtensionOption(command));
                }
            }
        }
    }

    private void SelectRadialMenuSlot(RadialMenuSlotEditorItem slot)
    {
        _selectedRadialMenuSlot = slot;
        OnPropertyChanged(nameof(RadialMenuSelectedSlotSummary));
    }

    private RadialMenuSlotEditorItem? ResolveRadialSlotFromMenuSender(object sender)
    {
        DependencyObject? current = sender as DependencyObject;
        while (current != null)
        {
            if (current is ContextMenu { PlacementTarget: FrameworkElement { DataContext: RadialMenuSlotEditorItem slot } })
            {
                return slot;
            }

            current = LogicalTreeHelper.GetParent(current);
        }

        if (sender is FrameworkElement { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: RadialMenuSlotEditorItem fallbackSlot } } })
        {
            return fallbackSlot;
        }

        return _selectedRadialMenuSlot;
    }

    private void ApplyRadialMenuCommandToSlot(RadialMenuSlotEditorItem slot, YarnSelectExtensionOption option)
    {
        if (string.IsNullOrWhiteSpace(option.ExtensionId))
        {
            return;
        }

        SelectRadialMenuSlot(slot);
        slot.ExtensionId = option.ExtensionId;
        slot.ExtensionTitle = option.Title;
        SaveQuickPanelTriggerSettings();
    }

    private static CommandItem BuildRadialFileCommand(EverythingSearchResult result)
    {
        var subtitle = string.IsNullOrWhiteSpace(result.SizeText)
            ? result.DirectoryPath
            : $"{result.DirectoryPath}   ·   {result.SizeText}";
        return new CommandItem(
            glyph: result.IsFolder ? "夹" : "文",
            title: result.Name,
            subtitle: subtitle,
            category: result.IsFolder ? "文件夹" : "文件",
            accentHex: result.IsFolder ? "#FF3B82F6" : "#FF4B5563",
            openTarget: result.FullPath,
            keywords: [result.FullPath, result.DirectoryPath, result.Name],
            source: CommandSource.File,
            extensionId: $"result::{result.FullPath}",
            resultKind: result.IsFolder ? ResultItemKind.Folder : ResultItemKind.File,
            resultProviderTitle: "Everything 文件",
            iconSourceOverride: NativeFileIconService.GetIcon(result.FullPath, result.IsFolder));
    }

    private void AddRadialMenuPageButton_Click(object sender, RoutedEventArgs e)
    {
        SaveRadialMenuSlots();
        _settings.RadialMenu ??= new RadialMenuSettings();
        var page = new RadialMenuPageSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"页面 {_settings.RadialMenu.Pages.Count + 1}"
        };
        _settings.RadialMenu.Pages.Add(page);
        _settings.RadialMenu.SelectedPageId = page.Id;
        RefreshRadialMenuSlots();
        SaveQuickPanelTriggerSettings();
    }

    private void DeleteRadialMenuPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.RadialMenu.Pages.Count <= 1)
        {
            return;
        }

        var removedId = _settings.RadialMenu.SelectedPageId;
        _settings.RadialMenu.Pages.RemoveAll(page => page.Id.Equals(removedId, StringComparison.OrdinalIgnoreCase));
        foreach (var page in _settings.RadialMenu.Pages)
        {
            page.ChildPageIds = (page.ChildPageIds ?? [])
                .Select(id => string.Equals(id, removedId, StringComparison.OrdinalIgnoreCase) ? null : id)
                .ToList();
        }
        _settings.RadialMenu.SelectedPageId = _settings.RadialMenu.Pages[0].Id;
        RefreshRadialMenuSlots();
        SaveQuickPanelTriggerSettings();
    }

    private void RadialMenuCenter_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        RenameCurrentRadialMenuPage();
        e.Handled = true;
    }

    private void RadialMenuCenter_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu != null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void RenameRadialMenuPageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RenameCurrentRadialMenuPage();
    }

    private void RenameCurrentRadialMenuPage()
    {
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];
        var pageId = SelectedRadialMenuPageId;
        var page = _settings.RadialMenu.Pages.FirstOrDefault(item =>
            item.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
        if (page == null)
        {
            HostAssets.AppendLog($"Radial rename skipped: selected page not found, pageId={pageId}.");
            return;
        }

        var oldName = page.Name;
        var dialog = new SimpleTextInputWindow("重命名轮盘", "输入新的轮盘名称。", page.Name)
        {
            Owner = this
        };
        bool accepted;
        try
        {
            _isRenamingRadialMenuPage = true;
            accepted = dialog.ShowDialog() == true;
        }
        finally
        {
            _isRenamingRadialMenuPage = false;
        }

        if (!accepted)
        {
            HostAssets.AppendLog($"Radial rename cancelled: pageId={pageId}, oldName={oldName}.");
            return;
        }

        var trimmedName = dialog.ValueText.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            HostAssets.AppendLog($"Radial rename ignored empty name: pageId={pageId}, oldName={oldName}.");
            return;
        }

        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];
        page = _settings.RadialMenu.Pages.FirstOrDefault(item =>
            item.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
        if (page == null)
        {
            HostAssets.AppendLog($"Radial rename failed after dialog: page missing, pageId={pageId}, newName={trimmedName}.");
            return;
        }

        page.Name = trimmedName;
        HostAssets.AppendLog($"Radial rename saving: pageId={pageId}, oldName={oldName}, newName={trimmedName}.");
        SaveQuickPanelTriggerSettings();
        RefreshRadialMenuSlots();
        OnPropertyChanged(nameof(SelectedRadialMenuPageName));
        var saved = AppSettingsStore.Load().RadialMenu.Pages.FirstOrDefault(item =>
            item.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase))?.Name ?? string.Empty;
        HostAssets.AppendLog($"Radial rename saved: pageId={pageId}, savedName={saved}, currentName={SelectedRadialMenuPageName}.");
    }

    private void RadialExtensionDragStart_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { DataContext: YarnSelectExtensionOption option } ||
            string.IsNullOrWhiteSpace(option.ExtensionId))
        {
            return;
        }

        System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, option.ExtensionId, System.Windows.DragDropEffects.Copy);
    }

    private void RadialSlot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialMenuSlotEditorItem slot })
        {
            SelectRadialMenuSlot(slot);
        }
    }

    private void RadialSlot_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialMenuSlotEditorItem slot })
        {
            SelectRadialMenuSlot(slot);
        }
    }

    private void RadialSlot_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.StringFormat) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void RadialSlot_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RadialMenuSlotEditorItem slot } ||
            !e.Data.GetDataPresent(System.Windows.DataFormats.StringFormat))
        {
            return;
        }

        slot.ExtensionId = e.Data.GetData(System.Windows.DataFormats.StringFormat) as string ?? string.Empty;
        slot.ExtensionTitle = ResolveRadialExtensionTitle(slot.ExtensionId);
        e.Handled = true;
        SaveQuickPanelTriggerSettings();
    }

    private void RadialSlotAddCommandMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        SelectRadialMenuSlot(slot);
        RadialMenuSearchText = string.Empty;
        RefreshRadialMenuCommandCandidates(string.Empty);
    }

    private void RadialSlotSetSimulatedKeyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        SelectRadialMenuSlot(slot);
        var initialShortcut = slot.ExtensionId.StartsWith(RadialSimulatedKeyPrefix, StringComparison.OrdinalIgnoreCase)
            ? slot.ExtensionId[RadialSimulatedKeyPrefix.Length..]
            : string.Empty;
        var dialog = new HotkeyCaptureWindow(
            "模拟按键",
            "录制要在此槽位执行的组合键。松开燕环时会直接模拟这个按键。",
            initialShortcut,
            allowEmpty: false,
            allowDoubleTap: false,
            allowModifierless: true)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var shortcut = dialog.ShortcutText.Trim();
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return;
        }

        slot.ExtensionId = $"{RadialSimulatedKeyPrefix}{shortcut}";
        slot.ExtensionTitle = ResolveRadialExtensionTitle(slot.ExtensionId);
        SaveQuickPanelTriggerSettings();
    }

    private void RadialSlotClearCommandMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        SelectRadialMenuSlot(slot);
        slot.ExtensionId = string.Empty;
        slot.ExtensionTitle = ResolveRadialExtensionTitle(string.Empty);
        SaveQuickPanelTriggerSettings();
    }

    private void RadialSlotAddChildPageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        SelectRadialMenuSlot(slot);
        SaveRadialMenuSlots();
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];
        var page = new RadialMenuPageSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"子环 {slot.Label}"
        };
        _settings.RadialMenu.Pages.Add(page);
        slot.ChildPageId = page.Id;
        slot.ChildPageTitle = ResolveRadialChildPageTitle(page.Id);
        SaveQuickPanelTriggerSettings();
        RefreshRadialMenuSlots();
    }

    private void RadialSlotClearChildPageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        SelectRadialMenuSlot(slot);
        var removedId = slot.ChildPageId;
        slot.ChildPageId = string.Empty;
        slot.ChildPageTitle = ResolveRadialChildPageTitle(string.Empty);
        if (!string.IsNullOrWhiteSpace(removedId) &&
            _settings.RadialMenu?.Pages?.Count > 1)
        {
            _settings.RadialMenu.Pages.RemoveAll(page => page.Id.Equals(removedId, StringComparison.OrdinalIgnoreCase));
            foreach (var page in _settings.RadialMenu.Pages)
            {
                page.ChildPageIds = (page.ChildPageIds ?? [])
                    .Select(id => string.Equals(id, removedId, StringComparison.OrdinalIgnoreCase) ? null : id)
                    .ToList();
            }
        }

        SaveQuickPanelTriggerSettings();
        RefreshRadialMenuSlots();
    }

    private void RadialMenuSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Down || FilteredRadialMenuCommandOptions.Count == 0)
        {
            return;
        }

        if (FindSiblingListBox(sender as DependencyObject) is { } listBox)
        {
            listBox.SelectedIndex = 0;
            listBox.Focus();
            e.Handled = true;
        }
    }

    private void RadialMenuCommandListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitRadialMenuCommandCandidate(listBox);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RadialMenuSearchText = string.Empty;
            e.Handled = true;
        }
    }

    private void RadialMenuCommandListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox)
        {
            CommitRadialMenuCommandCandidate(listBox);
        }
    }

    private void CommitRadialMenuCommandCandidate(System.Windows.Controls.ListBox listBox)
    {
        if (_selectedRadialMenuSlot == null ||
            listBox.SelectedItem is not YarnSelectExtensionOption option)
        {
            return;
        }

        ApplyRadialMenuCommandToSlot(_selectedRadialMenuSlot, option);
    }

    private string ResolveRadialExtensionTitle(string? extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return "拖入扩展";
        }

        if (extensionId.StartsWith(RadialSimulatedKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"模拟按键：{extensionId[RadialSimulatedKeyPrefix.Length..]}";
        }

        if (extensionId.StartsWith("result::", StringComparison.OrdinalIgnoreCase))
        {
            var path = extensionId["result::".Length..];
            var title = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(title) ? path : title;
        }

        return RadialMenuExtensionOptions.FirstOrDefault(option =>
            option.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase))?.Title ?? "未知扩展";
    }

    private string ResolveRadialChildPageTitle(string? pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            return "无子环";
        }

        var name = _settings.RadialMenu?.Pages?.FirstOrDefault(page =>
            page.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase))?.Name;
        return string.IsNullOrWhiteSpace(name) ? "未知子环" : $"进入 {name}";
    }

    private void AddYarnSelectRuleButton_Click(object sender, RoutedEventArgs e)
    {
        var item = new YarnSelectRuleItem(new YarnSelectRuleSettings
        {
            TriggerKey = "A",
            ActionType = YarnSelectActionTypes.RunExtension,
            Description = "新燕选规则"
        });
        ApplyYarnSelectExtensionSelection(item);
        YarnSelectRules.Add(item);
        OnPropertyChanged(nameof(YarnSelectSummary));
    }

    private void DeleteYarnSelectRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            return;
        }

        YarnSelectRules.Remove(item);
        SaveYarnSelectSettings();
    }

    private void ResetYarnSelectRulesButton_Click(object sender, RoutedEventArgs e)
    {
        YarnSelectRules.Clear();
        foreach (var rule in YarnSelectSettings.CreateDefaultRulesFromLegacy(new YarnSelectSettings()))
        {
            var item = new YarnSelectRuleItem(rule);
            ApplyYarnSelectExtensionSelection(item);
            YarnSelectRules.Add(item);
        }

        SaveYarnSelectSettings();
    }

    private static string GetYarnSelectActionLabel(string actionType)
    {
        return YarnSelectActionTypes.Normalize(actionType) switch
        {
            YarnSelectActionTypes.Cut => "剪切",
            YarnSelectActionTypes.Paste => "粘贴",
            YarnSelectActionTypes.Search => "搜索",
            YarnSelectActionTypes.Run => "运行",
            YarnSelectActionTypes.SmartCopyPaste => "智能复制/粘贴",
            YarnSelectActionTypes.RunExtension => "运行扩展",
            _ => "复制"
        };
    }

    private void ApplyYarnSelectExtensionSelection(YarnSelectRuleItem item)
    {
        var selected = YarnSelectExtensionOptions.FirstOrDefault(option =>
            option.ExtensionId.Equals(item.ExtensionId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        item.ExtensionSearchText = selected?.Title ?? string.Empty;
        item.FilteredExtensionOptions = [];
    }

    private void RefreshYarnSelectExtensionCandidates(YarnSelectRuleItem item, string keyword)
    {
        keyword = (keyword ?? string.Empty).Trim();
        if (keyword.Length == 0)
        {
            item.FilteredExtensionOptions = [];
            return;
        }

        item.FilteredExtensionOptions = new ObservableCollection<YarnSelectExtensionOption>(
            YarnSelectExtensionOptions
                .Where(option =>
                    option.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    option.ExtensionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    option.Detail.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Take(8));
    }

    private void YarnSelectExtensionSearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            RefreshYarnSelectExtensionCandidates(item, item.ExtensionSearchText ?? string.Empty);
        }
    }

    private void YarnSelectExtensionSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            var selected = YarnSelectExtensionOptions.FirstOrDefault(option =>
                option.ExtensionId.Equals(item.ExtensionId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (selected == null ||
                !selected.Title.Equals(item.ExtensionSearchText ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                item.ExtensionId = string.Empty;
            }

            RefreshYarnSelectExtensionCandidates(item, item.ExtensionSearchText ?? string.Empty);
        }
    }

    private void YarnSelectExtensionSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: YarnSelectRuleItem item } ||
            e.Key != Key.Down ||
            item.FilteredExtensionOptions.Count == 0)
        {
            return;
        }

        if (FindDescendantListBox(this, FilteredRadialMenuCommandOptions) is { } listBox)
        {
            listBox.SelectedIndex = 0;
            listBox.Focus();
            e.Handled = true;
        }
    }

    private void YarnSelectExtensionListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitYarnSelectExtensionCandidate(listBox);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && listBox.DataContext is YarnSelectRuleItem item)
        {
            item.FilteredExtensionOptions = [];
            e.Handled = true;
        }
    }

    private void YarnSelectExtensionListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox)
        {
            CommitYarnSelectExtensionCandidate(listBox);
        }
    }

    private void CommitYarnSelectExtensionCandidate(System.Windows.Controls.ListBox listBox)
    {
        if (listBox.DataContext is not YarnSelectRuleItem item ||
            listBox.SelectedItem is not YarnSelectExtensionOption option)
        {
            return;
        }

        item.ExtensionId = option.ExtensionId;
        item.ExtensionSearchText = option.Title;
        item.FilteredExtensionOptions = [];
    }

    private static System.Windows.Controls.ListBox? FindSiblingListBox(DependencyObject? source)
    {
        var parent = source == null ? null : VisualTreeHelper.GetParent(source);
        while (parent != null)
        {
            if (parent is StackPanel panel)
            {
                return panel.Children.OfType<System.Windows.Controls.ListBox>().FirstOrDefault();
            }

            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private static System.Windows.Controls.ListBox? FindDescendantListBox(DependencyObject source, object itemsSource)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++)
        {
            var child = VisualTreeHelper.GetChild(source, i);
            if (child is System.Windows.Controls.ListBox listBox &&
                ReferenceEquals(listBox.ItemsSource, itemsSource))
            {
                return listBox;
            }

            var nested = FindDescendantListBox(child, itemsSource);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private void RefreshQuickPanelTriggerBindings()
    {
        _settings.QuickPanelMouseTriggers ??= new QuickPanelMouseTriggerSettings();
        OnPropertyChanged(nameof(TriggerMiddleButtonDown));
        OnPropertyChanged(nameof(TriggerX1ButtonDown));
        OnPropertyChanged(nameof(TriggerX2ButtonDown));
        OnPropertyChanged(nameof(TriggerCtrlLeftClick));
        OnPropertyChanged(nameof(TriggerCtrlRightClick));
        OnPropertyChanged(nameof(TriggerMiddleButtonLongPress));
        OnPropertyChanged(nameof(TriggerRightButtonLongPress));
        OnPropertyChanged(nameof(TriggerRightButtonDrag));
        OnPropertyChanged(nameof(TriggerHorizontalWheel));

        OnPropertyChanged(nameof(ExecuteOnButtonRelease));
        OnPropertyChanged(nameof(QuickPanelTriggerSummary));
        OnPropertyChanged(nameof(EnableRadialMenu));
        OnPropertyChanged(nameof(EnableRadialCapsLockHold));
        OnPropertyChanged(nameof(RadialMenuSummary));
        RefreshRadialMenuSlots();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    private void ExternalLink_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"无法打开链接: {ex.Message}", "出错啦", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed record SettingsNavigationItem(string Key, string IconReference, string Title, string Accent)
{
    public Geometry? IconGeometry => ExtensionIconLibrary.ResolveVectorIcon(IconReference);
}

public sealed record SettingsShortcutItem(string ExtensionId, string Title, string Category, string? Shortcut)
{
    public string ShortcutValue => Shortcut ?? string.Empty;

    public string ShortcutLabel => string.IsNullOrWhiteSpace(Shortcut) ? "未设置" : Shortcut;

    public bool HasShortcut => !string.IsNullOrWhiteSpace(Shortcut);
}

public sealed record YarnSelectActionTypeOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record YarnSelectExtensionOption(
    string ExtensionId,
    string Title,
    string Detail,
    ImageSource? IconSource,
    Geometry? VectorIcon,
    System.Windows.Media.Brush AccentBrush,
    string DisplayGlyph)
{
    public YarnSelectExtensionOption(CommandItem command)
        : this(
            command.ExtensionId,
            command.Title,
            string.IsNullOrWhiteSpace(command.OpenTarget)
                ? command.ItemKindLabel
                : $"{command.ItemKindLabel} · {command.OpenTarget}",
            command.IconSource,
            command.VectorIcon,
            command.AccentBrush,
            command.DisplayGlyph)
    {
    }

    public YarnSelectExtensionOption(string extensionId, string title)
        : this(extensionId, title, string.Empty, null, null, System.Windows.Media.Brushes.Transparent, string.Empty)
    {
    }

    public bool HasImageIcon => IconSource != null;

    public bool HasVectorIcon => VectorIcon != null;

    public bool UseGlyphIcon => !HasImageIcon && !HasVectorIcon && !string.IsNullOrWhiteSpace(DisplayGlyph);

    public override string ToString() => Title;
}

public sealed class RadialMenuSlotEditorItem : INotifyPropertyChanged
{
    private string _extensionId;
    private string _childPageId;
    private string _extensionTitle;
    private string _childPageTitle;

    public RadialMenuSlotEditorItem(int index, string extensionId, string childPageId, string extensionTitle, string childPageTitle, double x, double y)
    {
        Index = index;
        _extensionId = extensionId;
        _childPageId = childPageId;
        _extensionTitle = extensionTitle;
        _childPageTitle = childPageTitle;
        X = x;
        Y = y;
    }

    public int Index { get; }

    public string Label => (Index + 1).ToString(CultureInfo.InvariantCulture);

    public double X { get; }

    public double Y { get; }

    public string ExtensionId
    {
        get => _extensionId;
        set
        {
            value ??= string.Empty;
            if (value == _extensionId)
            {
                return;
            }

            _extensionId = value;
            OnPropertyChanged();
        }
    }

    public string ExtensionTitle
    {
        get => _extensionTitle;
        set
        {
            value ??= string.Empty;
            if (value == _extensionTitle)
            {
                return;
            }

            _extensionTitle = value;
            OnPropertyChanged();
        }
    }

    public string ChildPageId
    {
        get => _childPageId;
        set
        {
            value ??= string.Empty;
            if (value == _childPageId)
            {
                return;
            }

            _childPageId = value;
            OnPropertyChanged();
        }
    }

    public string ChildPageTitle
    {
        get => _childPageTitle;
        set
        {
            value ??= string.Empty;
            if (value == _childPageTitle)
            {
                return;
            }

            _childPageTitle = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record RadialMenuPageEditorItem(string Id, string Name);

public sealed class YarnSelectRuleItem : INotifyPropertyChanged
{
    private bool _enabled;
    private string _triggerKey;
    private string _actionType;
    private string _extensionId;
    private string _extensionSearchText;
    private string _description;
    private ObservableCollection<YarnSelectExtensionOption> _filteredExtensionOptions = [];

    public YarnSelectRuleItem(YarnSelectRuleSettings rule)
    {
        _enabled = rule.Enabled;
        _triggerKey = rule.TriggerKey;
        _actionType = rule.ActionType;
        _extensionId = rule.ExtensionId;
        _extensionSearchText = string.Empty;
        _description = rule.Description;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value == _enabled)
            {
                return;
            }

            _enabled = value;
            OnPropertyChanged();
        }
    }

    public string TriggerKey
    {
        get => _triggerKey;
        set
        {
            value = YarnSelectSettings.NormalizeTriggerKey(value);
            if (value == _triggerKey)
            {
                return;
            }

            _triggerKey = value;
            OnPropertyChanged();
        }
    }

    public string ActionType
    {
        get => _actionType;
        set
        {
            value = YarnSelectActionTypes.Normalize(value);
            if (value == _actionType)
            {
                return;
            }

            _actionType = value;
            OnPropertyChanged();
        }
    }

    public string ExtensionId
    {
        get => _extensionId;
        set
        {
            value ??= string.Empty;
            if (value == _extensionId)
            {
                return;
            }

            _extensionId = value;
            OnPropertyChanged();
        }
    }

    public string ExtensionSearchText
    {
        get => _extensionSearchText;
        set
        {
            value ??= string.Empty;
            if (value == _extensionSearchText)
            {
                return;
            }

            _extensionSearchText = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<YarnSelectExtensionOption> FilteredExtensionOptions
    {
        get => _filteredExtensionOptions;
        set
        {
            if (ReferenceEquals(value, _filteredExtensionOptions))
            {
                return;
            }

            _filteredExtensionOptions = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredExtensionListVisibility));
        }
    }

    public Visibility FilteredExtensionListVisibility => FilteredExtensionOptions.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string Description
    {
        get => _description;
        set
        {
            value ??= string.Empty;
            if (value == _description)
            {
                return;
            }

            _description = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SettingsExtensionItem : INotifyPropertyChanged
{
    private bool _isPublished;
    private string _publisherName;
    private bool _isPublishing;
    private bool _isUnpublishing;

    public SettingsExtensionItem(
        string extensionId,
        string title,
        string category,
        string version,
        string directoryPath,
        string sourceLabel,
        bool canOpenDirectory,
        bool isEnabled,
        bool isPublished,
        string publisherName)
    {
        ExtensionId = extensionId;
        Title = title;
        Category = category;
        Version = version;
        DirectoryPath = directoryPath;
        SourceLabel = sourceLabel;
        CanOpenDirectory = canOpenDirectory;
        IsEnabled = isEnabled;
        _isPublished = isPublished;
        _publisherName = publisherName;
    }

    public string ExtensionId { get; }

    public string Title { get; }

    public string Category { get; }

    public string Version { get; }

    public string DirectoryPath { get; }

    public string SourceLabel { get; }

    public bool CanOpenDirectory { get; }

    public bool IsEnabled { get; }

    public bool IsPublished
    {
        get => _isPublished;
        set
        {
            if (_isPublished == value)
            {
                return;
            }

            _isPublished = value;
            NotifyPublishStateChanged();
        }
    }

    public string PublisherName
    {
        get => _publisherName;
        set
        {
            if (string.Equals(_publisherName, value, StringComparison.Ordinal))
            {
                return;
            }

            _publisherName = value;
            NotifyPublishStateChanged();
        }
    }

    public bool IsPublishing
    {
        get => _isPublishing;
        set
        {
            if (_isPublishing == value)
            {
                return;
            }

            _isPublishing = value;
            NotifyBusyStateChanged();
        }
    }

    public bool IsUnpublishing
    {
        get => _isUnpublishing;
        set
        {
            if (_isUnpublishing == value)
            {
                return;
            }

            _isUnpublishing = value;
            NotifyBusyStateChanged();
        }
    }

    public bool IsPublishedInStore => IsPublished && !string.IsNullOrWhiteSpace(PublisherName);

    public bool IsOperationBusy => IsPublishing || IsUnpublishing;

    public string PublishActionLabel => IsPublishedInStore ? "更新商店版本" : "发布到商店";

    public string PublishButtonText => IsPublishing
        ? (IsPublishedInStore ? "更新中..." : "发布中...")
        : PublishActionLabel;

    public string UnpublishButtonText => IsUnpublishing ? "下线中..." : "下线";

    public Visibility PublishSpinnerVisibility => IsPublishing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UnpublishSpinnerVisibility => IsUnpublishing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PublishNewButtonVisibility => IsPublishedInStore ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PublishUpdateButtonVisibility => IsPublishedInStore ? Visibility.Visible : Visibility.Collapsed;

    public Visibility UnpublishButtonVisibility => CanUnpublish ? Visibility.Visible : Visibility.Collapsed;

    public string PublisherLabel => string.IsNullOrWhiteSpace(PublisherName) ? "未发布" : $"发布者：{PublisherName}";

    public bool CanUnpublish => IsPublishedInStore;

    public bool PublishButtonEnabled => !IsOperationBusy;

    public bool UnpublishButtonEnabled => CanUnpublish && !IsOperationBusy;

    public bool EditButtonEnabled => !IsOperationBusy;

    public bool DeleteButtonEnabled => !IsOperationBusy;

    public bool OpenDirectoryButtonEnabled => CanOpenDirectory && !IsOperationBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyPublishStateChanged()
    {
        OnPropertyChanged(nameof(IsPublished));
        OnPropertyChanged(nameof(PublisherName));
        OnPropertyChanged(nameof(IsPublishedInStore));
        OnPropertyChanged(nameof(PublishActionLabel));
        OnPropertyChanged(nameof(PublishButtonText));
        OnPropertyChanged(nameof(PublisherLabel));
        OnPropertyChanged(nameof(CanUnpublish));
        OnPropertyChanged(nameof(PublishNewButtonVisibility));
        OnPropertyChanged(nameof(PublishUpdateButtonVisibility));
        OnPropertyChanged(nameof(UnpublishButtonVisibility));
        OnPropertyChanged(nameof(UnpublishButtonEnabled));
    }

    private void NotifyBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsPublishing));
        OnPropertyChanged(nameof(IsUnpublishing));
        OnPropertyChanged(nameof(IsOperationBusy));
        OnPropertyChanged(nameof(PublishButtonText));
        OnPropertyChanged(nameof(UnpublishButtonText));
        OnPropertyChanged(nameof(PublishSpinnerVisibility));
        OnPropertyChanged(nameof(UnpublishSpinnerVisibility));
        OnPropertyChanged(nameof(PublishButtonEnabled));
        OnPropertyChanged(nameof(UnpublishButtonEnabled));
        OnPropertyChanged(nameof(EditButtonEnabled));
        OnPropertyChanged(nameof(DeleteButtonEnabled));
        OnPropertyChanged(nameof(OpenDirectoryButtonEnabled));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SettingsRecycleBinItem : INotifyPropertyChanged
{
    private bool _isRestoring;
    private bool _isDeletingPermanently;

    public SettingsRecycleBinItem(
        string itemId,
        string extensionId,
        string title,
        string category,
        string version,
        string deletedAtUtc)
    {
        ItemId = itemId;
        ExtensionId = extensionId;
        Title = title;
        Category = category;
        Version = version;
        DeletedAtUtc = deletedAtUtc;
    }

    public string ItemId { get; }

    public string ExtensionId { get; }

    public string Title { get; }

    public string Category { get; }

    public string Version { get; }

    public string DeletedAtUtc { get; }

    public string DeletedAtLabel => DateTimeOffset.TryParse(DeletedAtUtc, out var timestamp)
        ? $"删除时间：{timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
        : "删除时间：未知";

    public bool IsRestoring
    {
        get => _isRestoring;
        set
        {
            if (_isRestoring == value)
            {
                return;
            }

            _isRestoring = value;
            NotifyBusyStateChanged();
        }
    }

    public bool IsDeletingPermanently
    {
        get => _isDeletingPermanently;
        set
        {
            if (_isDeletingPermanently == value)
            {
                return;
            }

            _isDeletingPermanently = value;
            NotifyBusyStateChanged();
        }
    }

    public bool IsOperationBusy => IsRestoring || IsDeletingPermanently;

    public string RestoreButtonText => IsRestoring ? "恢复中..." : "恢复";

    public string DeleteButtonText => IsDeletingPermanently ? "删除中..." : "彻底删除";

    public Visibility RestoreSpinnerVisibility => IsRestoring ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DeleteSpinnerVisibility => IsDeletingPermanently ? Visibility.Visible : Visibility.Collapsed;

    public bool RestoreButtonEnabled => !IsOperationBusy;

    public bool DeleteButtonEnabled => !IsOperationBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsRestoring));
        OnPropertyChanged(nameof(IsDeletingPermanently));
        OnPropertyChanged(nameof(IsOperationBusy));
        OnPropertyChanged(nameof(RestoreButtonText));
        OnPropertyChanged(nameof(DeleteButtonText));
        OnPropertyChanged(nameof(RestoreSpinnerVisibility));
        OnPropertyChanged(nameof(DeleteSpinnerVisibility));
        OnPropertyChanged(nameof(RestoreButtonEnabled));
        OnPropertyChanged(nameof(DeleteButtonEnabled));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
