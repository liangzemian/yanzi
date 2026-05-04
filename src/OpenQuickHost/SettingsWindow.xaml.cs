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
    private string _launcherHotkey = "Alt+Space";
    private string _syncStatusText = "同步服务状态未知。";
    private string _webDavServerUrl = "https://dav.jianguoyun.com/dav/";
    private string _webDavRootPath = "/yanzi";
    private string _webDavUsername = string.Empty;
    private string _webDavStatusText = "未启用个人扩展同步。";
    private string _syncActivityLogText = "暂无同步记录。";
    private string _recycleBinSummary = "回收站为空。";
    private string _recycleBinSearchText = string.Empty;
    private bool _isExtensionsLoading;
    private int _extensionsRefreshVersion;
    private IReadOnlyList<SettingsExtensionItem> _cachedExtensionItems = [];
    private IReadOnlyList<SettingsRecycleBinItem> _cachedRecycleBinItems = [];
    private bool _suppressWindowBoundsPersistence;

    public SettingsWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _settings = AppSettingsStore.Load();
        _settings.QuickPanelMouseTriggers ??= new QuickPanelMouseTriggerSettings();
        NavigationItems =
        [
            new SettingsNavigationItem("general", "M12,15.5A3.5,3.5 0 1,1 12,8.5A3.5,3.5 0 1,1 12,15.5M19.4,15L21.7,13.5L19.9,8.9L17.3,10L15.7,8.6L16.1,5.8L11.2,5.8L10.8,8.6L9.2,10L6.6,8.9L4.7,13.5L7,15L7,17L4.7,18.5L6.6,23.1L9.2,22L10.8,23.4L11.2,26.2L16.1,26.2L16.5,23.4L18.1,22L20.7,23.1L22.6,18.5L20.3,17Z", "常规", "#FF3B82F6"),
            new SettingsNavigationItem("sync", "M12,6V9L16,5L12,1V4A8,8 0 0,0 4,12C4,13.43 4.37,14.77 5.03,15.94L6.47,14.5C6.17,13.73 6,12.89 6,12A6,6 0 0,1 12,6M18.97,8.06L17.53,9.5C17.83,10.27 18,11.11 18,12A6,6 0 0,1 12,18V15L8,19L12,23V20A8,8 0 0,0 20,12C20,10.57 19.63,9.23 18.97,8.06Z", "同步", "#FF22C55E"),
            new SettingsNavigationItem("extensions", "M20.5,11H19V7C19,5.89 18.11,5 17,5H13V3.5A1.5,1.5 0 0,0 11.5,2A1.5,1.5 0 0,0 10,3.5V5H6C4.89,5 4,5.89 4,7V11H2.5A1.5,1.5 0 0,0 1,12.5A1.5,1.5 0 0,0 2.5,14H4V18C4,19.11 4.89,20 6,20H10V21.5A1.5,1.5 0 0,0 11.5,23A1.5,1.5 0 0,0 13,21.5V20H17C18.11,20 19,19.11 19,18V14H20.5A1.5,1.5 0 0,0 22,12.5A1.5,1.5 0 0,0 20.5,11Z", "扩展", "#FFF97316"),
            new SettingsNavigationItem("recycle", "M9,3H15L16,5H20A1,1 0 0,1 21,6V8H3V6A1,1 0 0,1 4,5H8L9,3M5,10H19L18.2,20.2A2,2 0 0,1 16.21,22H7.79A2,2 0 0,1 5.8,20.2L5,10M9,12V19H11V12H9M13,12V19H15V12H13Z", "回收站", "#FFEF4444"),
            new SettingsNavigationItem("shortcuts", "M7,7H17V9H7V7M7,11H13V13H7V11M15,11H17V13H15V11M7,15H11V17H7V15M13,15H17V17H13V15M5,3H19A2,2 0 0,1 21,5V19A2,2 0 0,1 19,21H5A2,2 0 0,1 3,19V5A2,2 0 0,1 5,3Z", "快捷键", "#FFEAB308"),
            new SettingsNavigationItem("quickpanel", "M4,4H20V20H4V4M6,6V18H18V6H6M8,8H10V10H8V8M14,8H16V10H14V8M8,14H10V16H8V14M14,14H16V16H14V14Z", "鼠标面板", "#FFEC4899"),
            new SettingsNavigationItem("about", "M11,9H13V7H11M12,20C7.59,20 4,16.41 4,12C4,7.59 7.59,4 12,4C16.41,4 20,7.59 20,12C20,16.41 16.41,20 12,20M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M11,17H13V11H11V17Z", "关于", "#FF8B5CF6")
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
        BaseUrl = _mainWindow.SyncBaseUrl;
        ExtensionsRootPath = LocalExtensionCatalog.CatalogRootPath;
        AppVersionText = AppVersionInfo.DisplayText;
        ShortcutItems = new ObservableCollection<SettingsShortcutItem>();
        ExtensionItems = new ObservableCollection<SettingsExtensionItem>();
        RecycleBinItems = new ObservableCollection<SettingsRecycleBinItem>();
        DataContext = this;
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
            OnPropertyChanged(nameof(IsSyncSelected));
            OnPropertyChanged(nameof(IsExtensionsSelected));
            OnPropertyChanged(nameof(IsRecycleBinSelected));
            OnPropertyChanged(nameof(IsShortcutsSelected));
            OnPropertyChanged(nameof(IsQuickPanelSelected));
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
        "sync" => "管理云账号状态、同步入口和当前服务端连接信息。",
        "extensions" => "查看本地扩展目录和当前机器已发现的扩展数量。",
        "recycle" => "查看已删除扩展，支持恢复和彻底删除。",
        "shortcuts" => "查看和管理主程序与扩展的全局快捷键。",
        "quickpanel" => "控制悬浮网格的操作面板，包括触发逻辑和槽位预设。",
        "about" => "查看当前版本与这套设置窗口的结构定位。",
        _ => "燕子设置"
    };

    public bool IsGeneralSelected => SelectedNavigation?.Key == "general";

    public bool IsSyncSelected => SelectedNavigation?.Key == "sync";

    public bool IsExtensionsSelected => SelectedNavigation?.Key == "extensions";

    public bool IsRecycleBinSelected => SelectedNavigation?.Key == "recycle";

    public bool IsShortcutsSelected => SelectedNavigation?.Key == "shortcuts";

    public bool IsQuickPanelSelected => SelectedNavigation?.Key == "quickpanel";

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
        SyncStatusText = _mainWindow.SyncStatus;
        RefreshVisibleSectionData();
    }

    private void SettingsWindow_Activated(object? sender, EventArgs e)
    {
        _settings = AppSettingsStore.Load();
        OnPropertyChanged(nameof(LaunchAtStartup));
        OnPropertyChanged(nameof(RefreshCloudOnStartup));
        OnPropertyChanged(nameof(CloseToTray));
        LauncherHotkey = _settings.LauncherHotkey;
        RefreshQuickPanelTriggerBindings();
        EnableWebDavSync = _settings.EnableWebDavSync;
        WebDavServerUrl = string.IsNullOrWhiteSpace(_settings.WebDavServerUrl) ? "https://dav.jianguoyun.com/dav/" : _settings.WebDavServerUrl;
        WebDavRootPath = _settings.WebDavRootPath;
        WebDavUsername = _settings.WebDavUsername;
        
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

    private void SaveQuickPanelTriggerSettings()
    {


        AppSettingsStore.Save(_settings);
        _mainWindow.RefreshAppSettings();
        _mainWindow.NotifyQuickPanelSettingsChanged("quickpanel-trigger-settings-saved");
        SyncStatusText = $"鼠标面板触发已保存：{QuickPanelTriggerSummary}";
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

public sealed record SettingsNavigationItem(string Key, string Glyph, string Title, string Accent);

public sealed record SettingsShortcutItem(string ExtensionId, string Title, string Category, string? Shortcut)
{
    public string ShortcutValue => Shortcut ?? string.Empty;

    public string ShortcutLabel => string.IsNullOrWhiteSpace(Shortcut) ? "未设置" : Shortcut;

    public bool HasShortcut => !string.IsNullOrWhiteSpace(Shortcut);
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
