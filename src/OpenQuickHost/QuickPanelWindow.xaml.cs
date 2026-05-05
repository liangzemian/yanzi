using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Controls;
using System.Linq;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Text;
using System.Diagnostics;

namespace OpenQuickHost;

public partial class QuickPanelWindow : Window, INotifyPropertyChanged
{
    private const int GlobalSlotCount = 12;
    private const int ContextSlotCount = 12;
    private readonly MainWindow _mainWindow;
    private AppSettings _settings;
    private readonly List<SlotViewModel> _allGlobalSlots = new();
    private readonly List<SlotViewModel> _allContextSlots = new();
    private readonly List<QuickPanelGroupItem> _allGlobalGroups = new();
    private readonly List<QuickPanelGroupItem> _allContextGroups = new();
    private bool _isPinned;
    private SlotViewModel? _hoveredSlot;
    private bool _isExecutingSlot;
    private IntPtr _previousForegroundWindow;
    private readonly DispatcherTimer _releaseTargetTimer;
    private ForegroundAppContext? _foregroundAppContext;
    private QuickPanelGroupItem? _selectedGlobalGroup;
    private QuickPanelGroupItem? _selectedContextGroup;
    private bool _isShowingGlobalFavorites;
    private bool _isShowingContextFavorites;
    private DateTime _suppressAutoHideUntilUtc = DateTime.MinValue;
    private bool _isEditMode;
    private System.Windows.Point? _dragStartPoint;
    private SlotViewModel? _dragSourceSlot;
    private readonly DispatcherTimer _folderCreationTimer;
    private SlotViewModel? _folderHoverTarget;
    private ObservableCollection<SlotViewModel> _activeFolderSlots = [];
    private string _activeFolderTitle = string.Empty;
    private bool _isFolderExpanded;
    private DateTimeOffset _suspendReleaseTargetPollingUntilUtc = DateTimeOffset.MinValue;

    public QuickPanelWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _settings = AppSettingsStore.Load();
        _releaseTargetTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _releaseTargetTimer.Tick += (_, _) => PollReleaseTarget();
        _folderCreationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _folderCreationTimer.Tick += FolderCreationTimer_Tick;
        
        LoadSlots();
        DataContext = this;

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) Hide();
        };
    }

    public ObservableCollection<SlotViewModel> GlobalSlots { get; } = new();

    public ObservableCollection<SlotViewModel> ContextSlots { get; } = new();

    public ObservableCollection<QuickPanelGroupItem> GlobalGroups { get; } = new();

    public ObservableCollection<QuickPanelGroupItem> ContextGroups { get; } = new();

    public string GlobalSectionTitle => "通用工具";

    public string GlobalHintText => "不管切换到哪个窗口，这些工具一直在。";

    public string ContextSectionTitle => _foregroundAppContext == null
        ? "应用专属"
        : $"应用专属 · {_foregroundAppContext.ProcessName}";

    public string ContextHintText => _foregroundAppContext == null
        ? "你在用什么软件，这里就显示它专属的工具。"
        : $"你在用什么软件，这里就显示它专属的工具。当前识别：{_foregroundAppContext.ProcessName}。";

    public System.Windows.Media.Brush PinButtonBrush => _isPinned
        ? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FFF59E0B")!
        : (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF888888")!;

    public string PinButtonTooltip => _isPinned ? "已常驻，失去焦点时不自动关闭" : "点击后常驻，失去焦点时不自动关闭";

    public QuickPanelGroupItem? SelectedGlobalGroup
    {
        get => _selectedGlobalGroup;
        private set
        {
            if (ReferenceEquals(_selectedGlobalGroup, value))
            {
                return;
            }

            if (_selectedGlobalGroup != null)
            {
                _selectedGlobalGroup.IsSelected = false;
            }

            _selectedGlobalGroup = value;
            if (_selectedGlobalGroup != null)
            {
                _selectedGlobalGroup.IsSelected = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(PanelTitle));
            OnPropertyChanged(nameof(EditModeHintText));
        }
    }

    public QuickPanelGroupItem? SelectedContextGroup
    {
        get => _selectedContextGroup;
        private set
        {
            if (ReferenceEquals(_selectedContextGroup, value))
            {
                return;
            }

            if (_selectedContextGroup != null)
            {
                _selectedContextGroup.IsSelected = false;
            }

            _selectedContextGroup = value;
            if (_selectedContextGroup != null)
            {
                _selectedContextGroup.IsSelected = true;
            }

            OnPropertyChanged();
        }
    }

    public string PanelTitle => _isShowingGlobalFavorites
        ? "通用收藏"
        : SelectedGlobalGroup?.Name ?? "通用工具";

    public bool IsEditMode
    {
        get => _isEditMode;
        private set
        {
            if (value == _isEditMode)
            {
                return;
            }

            _isEditMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EditButtonTooltip));
            OnPropertyChanged(nameof(EditModeHintText));
        }
    }

    public string EditButtonTooltip => IsEditMode ? "完成编辑" : "编辑面板";

    public string EditModeHintText => IsEditMode
        ? "编辑模式：拖动图标可移动；拖到另一个图标上停留 2 秒可自动成组。"
        : PanelTitle;

    public ObservableCollection<SlotViewModel> ActiveFolderSlots
    {
        get => _activeFolderSlots;
        private set
        {
            _activeFolderSlots = value;
            OnPropertyChanged();
        }
    }

    public string ActiveFolderTitle
    {
        get => _activeFolderTitle;
        private set
        {
            _activeFolderTitle = value;
            OnPropertyChanged();
        }
    }

    public bool IsFolderExpanded
    {
        get => _isFolderExpanded;
        private set
        {
            if (value == _isFolderExpanded)
            {
                return;
            }

            _isFolderExpanded = value;
            OnPropertyChanged();
        }
    }

    private void LoadSlots()
    {
        _settings = AppSettingsStore.Load();
        LoadGroups();
        GlobalSlots.Clear();
        ContextSlots.Clear();
        var allCommands = _mainWindow.GetAllCommands();

        if (_isShowingGlobalFavorites)
        {
            var favIds = _settings.GlobalFavoriteExtensionIds;
            foreach (var favId in favIds)
            {
                var command = allCommands.FirstOrDefault(c => c.ExtensionId == favId);
                if (command != null)
                    GlobalSlots.Add(new SlotViewModel(GlobalSlots.Count, command, true));
            }
            while (GlobalSlots.Count < GlobalSlotCount)
                GlobalSlots.Add(new SlotViewModel(GlobalSlots.Count, null, false));
        }
        else
        {
            var group = GetSelectedGlobalGroupSettings();
            for (int i = 0; i < GlobalSlotCount; i++)
            {
                var slotItem = group?.SlotItems.ElementAtOrDefault(i);
                GlobalSlots.Add(CreateSlotViewModel(i, slotItem, allCommands, isContextual: false));
            }
        }

        if (_isShowingContextFavorites)
        {
            var favIds = _settings.ContextFavoriteExtensionIds;
            foreach (var favId in favIds)
            {
                var command = allCommands.FirstOrDefault(c => c.ExtensionId == favId);
                if (command != null)
                    ContextSlots.Add(new SlotViewModel(ContextSlots.Count, command, true, isContextual: true));
            }
        }
        else
        {
            var group = GetSelectedContextGroupSettings();
            for (int i = 0; i < ContextSlotCount; i++)
            {
                var slotItem = group?.SlotItems.ElementAtOrDefault(i);
                ContextSlots.Add(CreateSlotViewModel(i, slotItem, allCommands, isContextual: true));
            }
        }

        while (ContextSlots.Count < ContextSlotCount)
            ContextSlots.Add(new SlotViewModel(ContextSlots.Count, null, false, isContextual: true));

        _allGlobalSlots.Clear();
        _allGlobalSlots.AddRange(GlobalSlots);
        _allContextSlots.Clear();
        _allContextSlots.AddRange(ContextSlots);
    }

    private SlotViewModel CreateSlotViewModel(int index, QuickPanelSlotItem? item, IReadOnlyList<CommandItem> allCommands, bool isContextual)
    {
        if (item == null)
        {
            return new SlotViewModel(index, null, false, isContextual: isContextual);
        }

        if (item.IsFolder)
        {
            var resolvedIds = item.FolderExtensionIds
                .Where(id => allCommands.Any(command => string.Equals(command.ExtensionId, id, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var previewCommand = resolvedIds.Count == 0
                ? null
                : allCommands.FirstOrDefault(command => string.Equals(command.ExtensionId, resolvedIds[0], StringComparison.OrdinalIgnoreCase));
            return SlotViewModel.CreateFolder(index, item.FolderName ?? "新分组", resolvedIds, previewCommand, isContextual);
        }

        var command = string.IsNullOrWhiteSpace(item.ExtensionId)
            ? null
            : allCommands.FirstOrDefault(c => string.Equals(c.ExtensionId, item.ExtensionId, StringComparison.OrdinalIgnoreCase));
        var isFav = command != null &&
                    (isContextual
                        ? _settings.ContextFavoriteExtensionIds.Contains(command.ExtensionId)
                        : _settings.GlobalFavoriteExtensionIds.Contains(command.ExtensionId));
        return new SlotViewModel(index, command, isFav, isContextual: isContextual);
    }

    private void LoadGroups()
    {
        GlobalGroups.Clear();
        ContextGroups.Clear();
        _allGlobalGroups.Clear();
        _allContextGroups.Clear();
        foreach (var group in _settings.QuickPanelGlobalGroups)
        {
            var item = new QuickPanelGroupItem(group.Id, group.Name);
            _allGlobalGroups.Add(item);
            GlobalGroups.Add(item);
        }

        foreach (var group in GetVisibleContextGroups())
        {
            var item = new QuickPanelGroupItem(group.Id, group.Name);
            _allContextGroups.Add(item);
            ContextGroups.Add(item);
        }

        SelectedGlobalGroup = GlobalGroups.FirstOrDefault(group => string.Equals(group.Id, _settings.SelectedQuickPanelGlobalGroupId, StringComparison.OrdinalIgnoreCase))
            ?? GlobalGroups.FirstOrDefault();
        SelectedContextGroup = ContextGroups.FirstOrDefault(group => string.Equals(group.Id, _settings.SelectedQuickPanelContextGroupId, StringComparison.OrdinalIgnoreCase))
            ?? ContextGroups.FirstOrDefault();
    }

    private void HubSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = HubSearchBox.Text.Trim().ToLower();
        if (string.IsNullOrEmpty(query))
        {
            RestoreSlotCollections();
            return;
        }

        var filteredGlobal = _allGlobalSlots
            .Where(s => s.IsOccupied && s.Title.ToLower().Contains(query))
            .ToList();
        var filteredContext = _allContextSlots
            .Where(s => s.IsOccupied && s.Title.ToLower().Contains(query))
            .ToList();

        GlobalSlots.Clear();
        foreach (var slot in filteredGlobal) GlobalSlots.Add(slot);
        ContextSlots.Clear();
        foreach (var slot in filteredContext) ContextSlots.Add(slot);
    }

    private void SaveSlots(bool isContextual)
    {
        var group = isContextual ? EnsureContextGroupForCurrentApp() : GetSelectedGlobalGroupSettings();
        if (group == null)
        {
            return;
        }

        group.SlotItems.Clear();
        var sourceSlots = isContextual ? ContextSlots : GlobalSlots;
        var slotCount = isContextual ? ContextSlotCount : GlobalSlotCount;
        for (int i = 0; i < slotCount; i++)
        {
            var vm = sourceSlots.ElementAtOrDefault(i);
            group.SlotItems.Add(vm?.CloneSlotItem());
        }
        group.Slots = group.SlotItems.Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null).ToList();
        if (isContextual)
        {
            _settings.SelectedQuickPanelContextGroupId = group.Id;
        }
        else
        {
            _settings.SelectedQuickPanelGlobalGroupId = group.Id;
        }
        SaveQuickPanelSettings(isContextual ? "quickpanel-save-context-slots" : "quickpanel-save-global-slots");
    }

    private QuickPanelGroupSettings? GetSelectedGlobalGroupSettings()
    {
        var selectedGroupId = SelectedGlobalGroup?.Id ?? _settings.SelectedQuickPanelGlobalGroupId;
        return _settings.QuickPanelGlobalGroups.FirstOrDefault(group => string.Equals(group.Id, selectedGroupId, StringComparison.OrdinalIgnoreCase));
    }

    private QuickPanelGroupSettings? GetSelectedContextGroupSettings()
    {
        var selectedGroupId = SelectedContextGroup?.Id ?? _settings.SelectedQuickPanelContextGroupId;
        return GetVisibleContextGroups().FirstOrDefault(group => string.Equals(group.Id, selectedGroupId, StringComparison.OrdinalIgnoreCase))
            ?? GetVisibleContextGroups().FirstOrDefault();
    }

    private void RestoreSlotCollections()
    {
        GlobalSlots.Clear();
        foreach (var slot in _allGlobalSlots)
        {
            GlobalSlots.Add(slot);
        }

        ContextSlots.Clear();
        foreach (var slot in _allContextSlots)
        {
            ContextSlots.Add(slot);
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        HidePanel();
        _mainWindow.OpenSettingsWindow("quickpanel");
    }

    private void AddGlobalGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SimpleTextInputWindow("新建分组", "输入新分组名称。", string.Empty)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var group = new QuickPanelGroupSettings
        {
            Name = dialog.ValueText
        };
        _settings.QuickPanelGlobalGroups.Add(group);
        _settings.SelectedQuickPanelGlobalGroupId = group.Id;
        SaveQuickPanelSettings("quickpanel-add-global-group");
        LoadSlots();
    }

    private void AddContextGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SimpleTextInputWindow("新建专属分组", "输入新的专属分组名称。", string.Empty)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var group = new QuickPanelGroupSettings
        {
            Name = dialog.ValueText,
            ContextProcessName = NormalizeProcessName(_foregroundAppContext?.ProcessName),
            ContextDisplayName = _foregroundAppContext?.ProcessName
        };
        _settings.QuickPanelContextGroups.Add(group);
        _settings.SelectedQuickPanelContextGroupId = group.Id;
        SaveQuickPanelSettings("quickpanel-add-context-group");
        LoadSlots();
    }

    private void GlobalGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: QuickPanelGroupItem group })
        {
            return;
        }

        _isShowingGlobalFavorites = false;
        _settings.SelectedQuickPanelGlobalGroupId = group.Id;
        SaveQuickPanelSettings("quickpanel-select-global-group");
        OnPropertyChanged(nameof(PanelTitle));
        LoadSlots();
    }

    private void ContextGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: QuickPanelGroupItem group })
        {
            return;
        }

        _isShowingContextFavorites = false;
        _settings.SelectedQuickPanelContextGroupId = group.Id;
        SaveQuickPanelSettings("quickpanel-select-context-group");
        LoadSlots();
    }

    private void RenameGlobalGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: QuickPanelGroupItem groupItem })
        {
            return;
        }

        var group = _settings.QuickPanelGlobalGroups.FirstOrDefault(item => string.Equals(item.Id, groupItem.Id, StringComparison.OrdinalIgnoreCase));
        if (group == null)
        {
            return;
        }

        var dialog = new SimpleTextInputWindow("重命名分组", "输入新的分组名称。", group.Name)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        group.Name = dialog.ValueText;
        SaveQuickPanelSettings("quickpanel-rename-global-group");
        LoadSlots();
    }

    private void RenameContextGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: QuickPanelGroupItem groupItem })
        {
            return;
        }

        var group = _settings.QuickPanelContextGroups.FirstOrDefault(item => string.Equals(item.Id, groupItem.Id, StringComparison.OrdinalIgnoreCase));
        if (group == null)
        {
            return;
        }

        var dialog = new SimpleTextInputWindow("重命名专属分组", "输入新的分组名称。", group.Name)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        group.Name = dialog.ValueText;
        SaveQuickPanelSettings("quickpanel-rename-context-group");
        LoadSlots();
    }

    private void DeleteGlobalGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: QuickPanelGroupItem groupItem })
        {
            return;
        }

        if (_settings.QuickPanelGlobalGroups.Count <= 1)
        {
            System.Windows.MessageBox.Show(this, "至少保留一个分组。", "无法删除", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(this, $"确认删除分组“{groupItem.Name}”吗？", "删除分组", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _settings.QuickPanelGlobalGroups.RemoveAll(group => string.Equals(group.Id, groupItem.Id, StringComparison.OrdinalIgnoreCase));
        _settings.SelectedQuickPanelGlobalGroupId = _settings.QuickPanelGlobalGroups[0].Id;
        SaveQuickPanelSettings("quickpanel-delete-global-group");
        LoadSlots();
    }

    private void DeleteContextGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: QuickPanelGroupItem groupItem })
        {
            return;
        }

        if (_settings.QuickPanelContextGroups.Count <= 1)
        {
            System.Windows.MessageBox.Show(this, "至少保留一个专属分组。", "无法删除", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = System.Windows.MessageBox.Show(this, $"确认删除分组“{groupItem.Name}”吗？", "删除分组", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _settings.QuickPanelContextGroups.RemoveAll(group => string.Equals(group.Id, groupItem.Id, StringComparison.OrdinalIgnoreCase));
        _settings.SelectedQuickPanelContextGroupId = _settings.QuickPanelContextGroups[0].Id;
        SaveQuickPanelSettings("quickpanel-delete-context-group");
        LoadSlots();
    }

    private void PinAutoHideButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        _suppressAutoHideUntilUtc = DateTime.UtcNow.AddMilliseconds(350);
        OnPropertyChanged(nameof(PinButtonBrush));
        OnPropertyChanged(nameof(PinButtonTooltip));
        Activate();
        BringToFront();
    }

    private void SlotButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is SlotViewModel vm)
        {
            if (IsEditMode)
            {
                return;
            }

            if (vm.IsFolder)
            {
                ExpandFolder(vm);
            }
            else if (vm.Command != null)
            {
                _ = ExecuteSlotCommandAsync(vm, "quick-panel-click");
            }
            else if (!vm.IsContextual)
            {
                _suppressAutoHideUntilUtc = DateTime.UtcNow.AddSeconds(2);
                var newCommand = _mainWindow.OpenAddExtensionForSlot(this);
                if (newCommand != null)
                {
                    _mainWindow.MarkExtensionAsNewFromQuickPanel(newCommand);
                    vm.SetCommand(newCommand, false);
                    SaveSlots(isContextual: false);
                    LoadSlots();
                    BringToFront();
                }
            }
            else
            {
                _suppressAutoHideUntilUtc = DateTime.UtcNow.AddSeconds(2);
                var newCommand = _mainWindow.OpenAddExtensionForSlot(this);
                if (newCommand != null)
                {
                    _mainWindow.MarkExtensionAsNewFromQuickPanel(newCommand);
                    vm.SetCommand(newCommand, false, isContextual: true);
                    SaveSlots(isContextual: true);
                    LoadSlots();
                    BringToFront();
                }
            }
        }
    }

    private void EditModeButton_Click(object sender, RoutedEventArgs e)
    {
        IsEditMode = !IsEditMode;
        if (!IsEditMode)
        {
            StopFolderHoverTimer();
        }
    }

    private void FolderBackButton_Click(object sender, RoutedEventArgs e)
    {
        CollapseFolder();
    }

    private void SlotButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SlotViewModel vm } && vm.Command != null)
        {
            SetReleaseTarget(vm);
        }
    }

    private void SlotButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SlotViewModel vm } && ReferenceEquals(_hoveredSlot, vm))
        {
            ClearReleaseTarget();
        }
    }

    private void SlotButton_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SlotViewModel vm } && vm.Command != null)
        {
            SetReleaseTarget(vm);
        }
    }

    private void SlotButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEditMode || sender is not FrameworkElement { Tag: SlotViewModel vm } || vm.Item == null)
        {
            _dragStartPoint = null;
            _dragSourceSlot = null;
            return;
        }

        _dragStartPoint = e.GetPosition(this);
        _dragSourceSlot = vm;
    }

    private void SlotButton_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!IsEditMode ||
            e.LeftButton != MouseButtonState.Pressed ||
            _dragStartPoint == null ||
            _dragSourceSlot?.Item == null)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        if (Math.Abs(currentPoint.X - _dragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - _dragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        StopFolderHoverTimer();
        var payload = new System.Windows.DataObject(typeof(SlotViewModel), _dragSourceSlot);
        DragDrop.DoDragDrop((DependencyObject)sender, payload, System.Windows.DragDropEffects.Move);
        _dragStartPoint = null;
        _dragSourceSlot = null;
        ClearReleaseTarget();
    }

    private void SlotButton_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SlotViewModel target })
        {
            e.Effects = System.Windows.DragDropEffects.None;
            StopFolderHoverTimer();
            return;
        }

        if (e.Data.GetDataPresent(typeof(CommandItem)))
        {
            var command = e.Data.GetData(typeof(CommandItem)) as CommandItem;
            if (command == null || target.Item != null)
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
            else
            {
                _suspendReleaseTargetPollingUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(350);
                SetReleaseTarget(target);
                e.Effects = System.Windows.DragDropEffects.Copy;
            }

            StopFolderHoverTimer();
            return;
        }

        if (!IsEditMode || !e.Data.GetDataPresent(typeof(SlotViewModel)))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            StopFolderHoverTimer();
            return;
        }

        var source = e.Data.GetData(typeof(SlotViewModel)) as SlotViewModel;
        if (source == null || ReferenceEquals(source, target))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            StopFolderHoverTimer();
            return;
        }

        e.Effects = System.Windows.DragDropEffects.Move;
        _suspendReleaseTargetPollingUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(350);
        SetReleaseTarget(target);

        if (!source.IsFolder && !target.IsFolder && source.Command != null && target.Command != null)
        {
            StartFolderHoverTimer(source, target);
        }
        else
        {
            StopFolderHoverTimer();
        }

        e.Handled = true;
    }

    private void SlotButton_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        StopFolderHoverTimer();
    }

    private void SlotButton_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SlotViewModel target })
        {
            return;
        }

        if (e.Data.GetDataPresent(typeof(CommandItem)))
        {
            var command = e.Data.GetData(typeof(CommandItem)) as CommandItem;
            if (command != null && target.Item == null)
            {
                AddCommandToSlot(target, command);
            }

            StopFolderHoverTimer();
            ClearReleaseTarget();
            e.Handled = true;
            return;
        }

        if (!IsEditMode || !e.Data.GetDataPresent(typeof(SlotViewModel)))
        {
            return;
        }

        var source = e.Data.GetData(typeof(SlotViewModel)) as SlotViewModel;
        if (source == null || ReferenceEquals(source, target))
        {
            return;
        }

        StopFolderHoverTimer();
        MoveOrSwapSlot(source, target);
        e.Handled = true;
    }

    public void ExecuteHoveredSlotFromHoldRelease()
    {
        if (!IsVisible)
        {
            HostAssets.AppendLog("Quick panel hold release: panel is not visible.");
            return;
        }

        var slot = _hoveredSlot ?? ResolveSlotUnderCursor();
        if (slot?.Command == null)
        {
            HostAssets.AppendLog("Quick panel hold release: no occupied slot under cursor.");
            return;
        }

        HostAssets.AppendLog($"Quick panel hold release: executing slot {slot.Index}, extension={slot.Command.ExtensionId}.");
        _ = ExecuteSlotCommandAsync(slot, "quick-panel-hold-release");
    }

    private void SetReleaseTarget(SlotViewModel? slot)
    {
        if (ReferenceEquals(_hoveredSlot, slot))
        {
            return;
        }

        if (_hoveredSlot != null)
        {
            _hoveredSlot.IsReleaseTarget = false;
        }

        _hoveredSlot = slot;
        if (_hoveredSlot != null)
        {
            _hoveredSlot.IsReleaseTarget = true;
        }
    }

    private void ClearReleaseTarget()
    {
        if (_hoveredSlot != null)
        {
            _hoveredSlot.IsReleaseTarget = false;
        }

        _hoveredSlot = null;
    }

    private async Task ExecuteSlotCommandAsync(SlotViewModel vm, string launchSource)
    {
        if (_isExecutingSlot || vm.Command == null)
        {
            return;
        }

        _isExecutingSlot = true;
        try
        {
            var command = vm.Command;
            HostAssets.AppendLog($"Quick panel execute: source={launchSource}, slot={vm.Index}, extension={command.ExtensionId}.");
            _releaseTargetTimer.Stop();
            HidePanel();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (_previousForegroundWindow != IntPtr.Zero)
            {
                var restored = NativeMethods.SetForegroundWindow(_previousForegroundWindow);
                HostAssets.AppendLog($"Quick panel execute: restored previous foreground={restored}, {DescribeWindow(_previousForegroundWindow)}.");
            }

            await Task.Delay(120);
            var input = await SelectionCaptureService.CaptureSelectedInputAsync();
            HostAssets.AppendLog($"Quick panel execute: captured input length={input.Length}.");
            _mainWindow.ExecuteCommandExternally(command, input, launchSource);
        }
        finally
        {
            _isExecutingSlot = false;
            ClearReleaseTarget();
        }
    }

    private async void RemoveExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is SlotViewModel vm)
        {
            if (!vm.IsFolder && vm.Command?.Source == CommandSource.LocalExtension)
            {
                var result = await _mainWindow.DeleteExtensionFromQuickPanelAsync(vm.Command.ExtensionId, this);
                if (!result.ok && !string.IsNullOrWhiteSpace(result.message))
                {
                    System.Windows.MessageBox.Show(this, result.message, "删除扩展失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                LoadSlots();
                return;
            }

            vm.SetCommand(null, false, vm.IsContextual);
            SaveSlots(vm.IsContextual);
        }
    }

    private void CopySlotExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel { Command: not null } vm })
        {
            return;
        }

        _mainWindow.SetQuickPanelClipboard(vm.Command!, isCut: false, BuildSlotReference(vm));
    }

    private void CutSlotExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel { Command: not null } vm })
        {
            return;
        }

        _mainWindow.SetQuickPanelClipboard(vm.Command!, isCut: true, BuildSlotReference(vm));
    }

    private void PasteSlotExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel vm })
        {
            return;
        }

        var clipboard = _mainWindow.GetQuickPanelClipboard();
        if (clipboard == null)
        {
            if (!_mainWindow.TryImportExtensionFromSystemClipboard(out var importedCommand, out var importMessage) ||
                importedCommand == null)
            {
                _mainWindow.SyncStatus = string.IsNullOrWhiteSpace(importMessage)
                    ? "扩展剪贴板为空。先复制扩展，或把扩展 JSON 放进系统剪贴板后再粘贴。"
                    : importMessage;
                return;
            }

            clipboard = new QuickPanelClipboardItem(importedCommand.ExtensionId, importedCommand.Title, false, null);
        }

        if (!TryPasteClipboardIntoSlot(vm, clipboard, out var message))
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                _mainWindow.SyncStatus = message;
            }
            return;
        }

        _mainWindow.LastRunMessage = message;
    }

    private async void EditExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel { Command: not null } vm } ||
            !vm.CanEdit)
        {
            return;
        }

        var result = await _mainWindow.EditExtensionFromQuickPanelAsync(vm.Command!.ExtensionId, this);
        if (!result.ok && !string.IsNullOrWhiteSpace(result.message))
        {
            System.Windows.MessageBox.Show(this, result.message, "编辑扩展失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoadSlots();
    }

    private void OpenExtensionDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel { Command: not null } vm } ||
            !vm.CanOpenDirectory)
        {
            return;
        }

        if (!_mainWindow.TryOpenExtensionDirectory(vm.Command!.ExtensionId, out var message) &&
            !string.IsNullOrWhiteSpace(message))
        {
            System.Windows.MessageBox.Show(this, message, "打开目录失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void PublishExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: SlotViewModel { Command: not null } vm } ||
            !vm.CanPublish)
        {
            return;
        }

        var result = await _mainWindow.PublishExtensionFromSettingsAsync(vm.Command!.ExtensionId);
        System.Windows.MessageBox.Show(
            this,
            result.message,
            result.ok ? "发布到商店" : "发布到商店失败",
            MessageBoxButton.OK,
            result.ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is SlotViewModel vm && vm.Command != null)
        {
            var id = vm.Command.ExtensionId;
            var favorites = vm.IsContextual ? _settings.ContextFavoriteExtensionIds : _settings.GlobalFavoriteExtensionIds;
            if (favorites.Contains(id))
                favorites.Remove(id);
            else
                favorites.Add(id);

            SaveQuickPanelSettings(vm.IsContextual ? "quickpanel-toggle-context-favorite" : "quickpanel-toggle-global-favorite");
            vm.SetFavorite(favorites.Contains(id));
        }
    }

    private void ToggleGlobalFavorites_Click(object sender, RoutedEventArgs e)
    {
        _isShowingGlobalFavorites = !_isShowingGlobalFavorites;
        OnPropertyChanged(nameof(PanelTitle));
        OnPropertyChanged(nameof(EditModeHintText));
        LoadSlots();
    }

    private void ToggleContextFavorites_Click(object sender, RoutedEventArgs e)
    {
        _isShowingContextFavorites = !_isShowingContextFavorites;
        LoadSlots();
    }

    private void GlobalPanel_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isShowingGlobalFavorites || GlobalGroups.Count <= 1)
        {
            return;
        }

        CycleGroups(GlobalGroups, SelectedGlobalGroup, e.Delta, isContextual: false);
        e.Handled = true;
    }

    private void ContextPanel_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isShowingContextFavorites || ContextGroups.Count <= 1)
        {
            return;
        }

        CycleGroups(ContextGroups, SelectedContextGroup, e.Delta, isContextual: true);
        e.Handled = true;
    }

    private void CycleGroups(IReadOnlyList<QuickPanelGroupItem> groups, QuickPanelGroupItem? selectedGroup, int delta, bool isContextual)
    {
        if (groups.Count == 0)
        {
            return;
        }

        var currentIndex = selectedGroup == null
            ? 0
            : groups.ToList().FindIndex(group => string.Equals(group.Id, selectedGroup.Id, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var direction = delta < 0 ? 1 : -1;
        var nextIndex = (currentIndex + direction + groups.Count) % groups.Count;
        var nextGroup = groups[nextIndex];
        if (isContextual)
        {
            _settings.SelectedQuickPanelContextGroupId = nextGroup.Id;
        }
        else
        {
            _settings.SelectedQuickPanelGlobalGroupId = nextGroup.Id;
            OnPropertyChanged(nameof(PanelTitle));
        }

        SaveQuickPanelSettings(isContextual ? "quickpanel-cycle-context-group" : "quickpanel-cycle-global-group");
        LoadSlots();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEditMode && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_isPinned)
        {
            return;
        }

        if (DateTime.UtcNow <= _suppressAutoHideUntilUtc)
        {
            return;
        }

        if (OwnedWindows.OfType<Window>().Any(static window => window.IsVisible))
        {
            return;
        }

        _releaseTargetTimer.Stop();
        HidePanel();
    }

    public void ShowAtMouse()
    {
        try
        {
            HostAssets.AppendLog("Quick panel show requested.");
            _previousForegroundWindow = NativeMethods.GetForegroundWindow();
            _foregroundAppContext = BuildForegroundAppContext(_previousForegroundWindow);
            var cursorPixels = NativeMethods.GetCursorPosition();
            var cursorDips = DeviceToDips(cursorPixels);
            const double safeAnchorY = 310;
            Left = cursorDips.X - Width / 2;
            Top = cursorDips.Y - safeAnchorY;

            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)cursorPixels.X, (int)cursorPixels.Y));
            var screenBounds = DeviceRectToDips(screen.Bounds);
            if (Left < screenBounds.Left) Left = screenBounds.Left;
            if (Top < screenBounds.Top) Top = screenBounds.Top;
            if (Left + Width > screenBounds.Right) Left = screenBounds.Right - Width;
            if (Top + Height > screenBounds.Bottom) Top = screenBounds.Bottom - Height;

            HubSearchBox.Text = string.Empty; // Reset search on show
            _hoveredSlot = null;
            LoadSlots(); // Refresh
            var occupiedGlobal = GlobalSlots.Count(slot => slot.IsOccupied);
            var occupiedContext = ContextSlots.Count(slot => slot.IsOccupied);
            HostAssets.AppendLog($"Quick panel showing at ({Left:0},{Top:0}), cursorPixels=({cursorPixels.X:0},{cursorPixels.Y:0}), cursorDips=({cursorDips.X:0},{cursorDips.Y:0}), screenDips=({screenBounds.Left:0},{screenBounds.Top:0},{screenBounds.Right:0},{screenBounds.Bottom:0}), occupiedGlobal={occupiedGlobal}, occupiedContext={occupiedContext}, totalGlobal={GlobalSlots.Count}, totalContext={ContextSlots.Count}.");
            OnPropertyChanged(nameof(ContextSectionTitle));
            OnPropertyChanged(nameof(ContextHintText));
            Topmost = true;
            Show();
            _releaseTargetTimer.Start();
            Activate();
            BringToFront();
            Dispatcher.BeginInvoke(() =>
            {
                BringToFront();
                HubSearchBox.Focus();
                Keyboard.Focus(HubSearchBox);
                HubSearchBox.Select(0, 0);
                HubSearchBox.CaretIndex = 0;
            }, DispatcherPriority.ApplicationIdle);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Quick panel show failed: {ex}");
        }
    }

    public void ReloadSlots()
    {
        LoadSlots();
    }

    public void RefreshSettingsFromStore()
    {
        _settings = AppSettingsStore.Load();
        LoadSlots();
    }

    private IReadOnlyList<QuickPanelGroupSettings> GetVisibleContextGroups()
    {
        var normalizedProcessName = NormalizeProcessName(_foregroundAppContext?.ProcessName);
        if (string.IsNullOrWhiteSpace(normalizedProcessName))
        {
            return [];
        }

        return _settings.QuickPanelContextGroups
            .Where(group => string.Equals(NormalizeProcessName(group.ContextProcessName), normalizedProcessName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private QuickPanelGroupSettings? EnsureContextGroupForCurrentApp()
    {
        var current = GetSelectedContextGroupSettings();
        if (current != null)
        {
            return current;
        }

        var normalizedProcessName = NormalizeProcessName(_foregroundAppContext?.ProcessName);
        if (string.IsNullOrWhiteSpace(normalizedProcessName))
        {
            return null;
        }

        var existingUnbound = _settings.QuickPanelContextGroups.FirstOrDefault(group =>
            string.IsNullOrWhiteSpace(group.ContextProcessName) &&
            group.SlotItems.Any(static slot => slot != null));
        if (existingUnbound != null)
        {
            existingUnbound.ContextProcessName = normalizedProcessName;
            existingUnbound.ContextDisplayName = _foregroundAppContext?.ProcessName;
            _settings.SelectedQuickPanelContextGroupId = existingUnbound.Id;
            SaveQuickPanelSettings("quickpanel-bind-existing-context-group");
            LoadGroups();
            return existingUnbound;
        }

        var autoGroup = new QuickPanelGroupSettings
        {
            Name = _foregroundAppContext?.ProcessName ?? "专属",
            ContextProcessName = normalizedProcessName,
            ContextDisplayName = _foregroundAppContext?.ProcessName
        };
        _settings.QuickPanelContextGroups.Add(autoGroup);
        _settings.SelectedQuickPanelContextGroupId = autoGroup.Id;
        SaveQuickPanelSettings("quickpanel-auto-create-context-group");
        LoadGroups();
        return autoGroup;
    }

    private static string NormalizeProcessName(string? processName)
    {
        return string.IsNullOrWhiteSpace(processName)
            ? string.Empty
            : processName.Trim().ToLowerInvariant();
    }

    private QuickPanelSlotReference? BuildSlotReference(SlotViewModel vm)
    {
        var group = vm.IsContextual ? GetSelectedContextGroupSettings() : GetSelectedGlobalGroupSettings();
        return group == null ? null : new QuickPanelSlotReference(vm.IsContextual, group.Id, vm.Index);
    }

    private bool TryPasteClipboardIntoSlot(SlotViewModel targetSlot, QuickPanelClipboardItem clipboard, out string message)
    {
        var command = _mainWindow.GetAllCommands()
            .FirstOrDefault(item => string.Equals(item.ExtensionId, clipboard.ExtensionId, StringComparison.OrdinalIgnoreCase));
        if (command == null)
        {
            message = $"找不到扩展：{clipboard.Title}";
            _mainWindow.ClearQuickPanelClipboard();
            return false;
        }

        var targetGroup = targetSlot.IsContextual ? EnsureContextGroupForCurrentApp() : GetSelectedGlobalGroupSettings();
        if (targetGroup == null)
        {
            message = "当前鼠标面板分组不可用。";
            return false;
        }

        while (targetGroup.SlotItems.Count < (targetSlot.IsContextual ? ContextSlotCount : GlobalSlotCount))
        {
            targetGroup.SlotItems.Add(null);
        }

        if (clipboard.IsCut && clipboard.SourceSlot != null)
        {
            var sourceGroup = clipboard.SourceSlot.IsContextual
                ? _settings.QuickPanelContextGroups.FirstOrDefault(group => string.Equals(group.Id, clipboard.SourceSlot.GroupId, StringComparison.OrdinalIgnoreCase))
                : _settings.QuickPanelGlobalGroups.FirstOrDefault(group => string.Equals(group.Id, clipboard.SourceSlot.GroupId, StringComparison.OrdinalIgnoreCase));
            if (sourceGroup != null)
            {
                while (sourceGroup.SlotItems.Count < (clipboard.SourceSlot.IsContextual ? ContextSlotCount : GlobalSlotCount))
                {
                    sourceGroup.SlotItems.Add(null);
                }

                if (clipboard.SourceSlot.Index == targetSlot.Index &&
                    clipboard.SourceSlot.IsContextual == targetSlot.IsContextual &&
                    string.Equals(clipboard.SourceSlot.GroupId, targetGroup.Id, StringComparison.OrdinalIgnoreCase))
                {
                    message = $"扩展已在当前位置：{clipboard.Title}";
                    _mainWindow.ClearQuickPanelClipboard();
                    return true;
                }

                var targetExisting = targetGroup.SlotItems[targetSlot.Index];
                targetGroup.SlotItems[targetSlot.Index] = new QuickPanelSlotItem
                {
                    ExtensionId = clipboard.ExtensionId
                };
                if (clipboard.SourceSlot.Index >= 0 && clipboard.SourceSlot.Index < sourceGroup.SlotItems.Count)
                {
                    sourceGroup.SlotItems[clipboard.SourceSlot.Index] = targetExisting;
                }

                targetGroup.Slots = targetGroup.SlotItems.Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null).ToList();
                sourceGroup.Slots = sourceGroup.SlotItems.Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null).ToList();
                SaveQuickPanelSettings("quickpanel-move-slot");
                _mainWindow.ClearQuickPanelClipboard();
                LoadSlots();
                message = targetExisting == null
                    ? $"已移动到第 {targetSlot.Index + 1} 个槽位：{clipboard.Title}"
                    : $"已与第 {targetSlot.Index + 1} 个槽位交换位置：{clipboard.Title}";
                return true;
            }
        }

        targetGroup.SlotItems[targetSlot.Index] = new QuickPanelSlotItem
        {
            ExtensionId = clipboard.ExtensionId
        };
        targetGroup.Slots = targetGroup.SlotItems.Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null).ToList();
        SaveQuickPanelSettings("quickpanel-paste-slot");
        LoadSlots();
        message = targetSlot.Item == null
            ? $"已粘贴到第 {targetSlot.Index + 1} 个槽位：{clipboard.Title}"
            : $"已替换第 {targetSlot.Index + 1} 个槽位为：{clipboard.Title}";
        return true;
    }

    private void BringToFront()
    {
        Activate();
        Focus();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    private void HidePanel()
    {
        _releaseTargetTimer.Stop();
        StopFolderHoverTimer();
        CollapseFolder();
        ClearReleaseTarget();
        Topmost = false;
        Hide();
    }

    private System.Windows.Point DeviceToDips(System.Windows.Point point)
    {
        var transform = GetTransformFromDevice();
        return transform.Transform(point);
    }

    private Rect DeviceRectToDips(System.Drawing.Rectangle rectangle)
    {
        var topLeft = DeviceToDips(new System.Windows.Point(rectangle.Left, rectangle.Top));
        var bottomRight = DeviceToDips(new System.Windows.Point(rectangle.Right, rectangle.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private Matrix GetTransformFromDevice()
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        var source = HwndSource.FromHwnd(handle);
        return source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
    }

    private void SaveQuickPanelSettings(string reason)
    {
        AppSettingsStore.Save(_settings);
        _mainWindow.NotifyQuickPanelSettingsChanged(reason);
    }

    private void ExpandFolder(SlotViewModel folderSlot)
    {
        if (!folderSlot.IsFolder)
        {
            return;
        }

        var commands = _mainWindow.GetAllCommands();
        ActiveFolderTitle = folderSlot.Title;
        ActiveFolderSlots = new ObservableCollection<SlotViewModel>(
            folderSlot.FolderExtensionIds
                .Select((id, index) => CreateSlotViewModel(
                    index,
                    new QuickPanelSlotItem { ExtensionId = id },
                    commands,
                    folderSlot.IsContextual))
                .Where(static slot => slot.Command != null));
        IsFolderExpanded = true;
    }

    private void CollapseFolder()
    {
        ActiveFolderTitle = string.Empty;
        ActiveFolderSlots = [];
        IsFolderExpanded = false;
    }

    private void StartFolderHoverTimer(SlotViewModel source, SlotViewModel target)
    {
        _dragSourceSlot = source;
        _folderHoverTarget = target;
        if (!_folderCreationTimer.IsEnabled)
        {
            _folderCreationTimer.Start();
        }
    }

    private void StopFolderHoverTimer()
    {
        _folderCreationTimer.Stop();
        _folderHoverTarget = null;
    }

    private void FolderCreationTimer_Tick(object? sender, EventArgs e)
    {
        var source = _dragSourceSlot;
        var target = _folderHoverTarget;
        StopFolderHoverTimer();
        if (source == null || target == null)
        {
            return;
        }

        CreateFolderFromSlots(source, target);
    }

    private void MoveOrSwapSlot(SlotViewModel source, SlotViewModel target)
    {
        var sourceGroup = source.IsContextual ? EnsureContextGroupForCurrentApp() : GetSelectedGlobalGroupSettings();
        var targetGroup = target.IsContextual ? EnsureContextGroupForCurrentApp() : GetSelectedGlobalGroupSettings();
        if (sourceGroup == null || targetGroup == null)
        {
            return;
        }

        while (sourceGroup.SlotItems.Count < (source.IsContextual ? ContextSlotCount : GlobalSlotCount))
        {
            sourceGroup.SlotItems.Add(null);
        }

        while (targetGroup.SlotItems.Count < (target.IsContextual ? ContextSlotCount : GlobalSlotCount))
        {
            targetGroup.SlotItems.Add(null);
        }

        var sourceItem = source.CloneSlotItem();
        var targetItem = target.CloneSlotItem();
        targetGroup.SlotItems[target.Index] = sourceItem;
        sourceGroup.SlotItems[source.Index] = targetItem;
        sourceGroup.Slots = sourceGroup.SlotItems.Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null).ToList();
        targetGroup.Slots = targetGroup.SlotItems.Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null).ToList();
        SaveQuickPanelSettings("quickpanel-drag-swap-slot");
        LoadSlots();
    }

    private void CreateFolderFromSlots(SlotViewModel source, SlotViewModel target)
    {
        if (source.Command == null || target.Command == null || source.IsFolder || target.IsFolder)
        {
            return;
        }

        var sourceGroup = source.IsContextual ? EnsureContextGroupForCurrentApp() : GetSelectedGlobalGroupSettings();
        var targetGroup = target.IsContextual ? EnsureContextGroupForCurrentApp() : GetSelectedGlobalGroupSettings();
        if (sourceGroup == null || targetGroup == null)
        {
            return;
        }

        while (sourceGroup.SlotItems.Count < (source.IsContextual ? ContextSlotCount : GlobalSlotCount))
        {
            sourceGroup.SlotItems.Add(null);
        }

        while (targetGroup.SlotItems.Count < (target.IsContextual ? ContextSlotCount : GlobalSlotCount))
        {
            targetGroup.SlotItems.Add(null);
        }

        targetGroup.SlotItems[target.Index] = new QuickPanelSlotItem
        {
            ItemType = "folder",
            FolderName = $"{target.Title}组",
            FolderExtensionIds = [target.Command.ExtensionId, source.Command.ExtensionId]
        };
        sourceGroup.SlotItems[source.Index] = null;
        sourceGroup.Slots = sourceGroup.SlotItems.Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null).ToList();
        targetGroup.Slots = targetGroup.SlotItems.Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null).ToList();
        SaveQuickPanelSettings("quickpanel-auto-create-folder");
        LoadSlots();
    }

    private void AddCommandToSlot(SlotViewModel target, CommandItem command)
    {
        var targetGroup = target.IsContextual ? EnsureContextGroupForCurrentApp() : GetSelectedGlobalGroupSettings();
        if (targetGroup == null)
        {
            return;
        }

        while (targetGroup.SlotItems.Count < (target.IsContextual ? ContextSlotCount : GlobalSlotCount))
        {
            targetGroup.SlotItems.Add(null);
        }

        if (targetGroup.SlotItems.Any(slot =>
                slot != null &&
                ((!slot.IsFolder && string.Equals(slot.ExtensionId, command.ExtensionId, StringComparison.OrdinalIgnoreCase)) ||
                 (slot.IsFolder && slot.FolderExtensionIds.Any(id => string.Equals(id, command.ExtensionId, StringComparison.OrdinalIgnoreCase))))))
        {
            return;
        }

        targetGroup.SlotItems[target.Index] = new QuickPanelSlotItem
        {
            ExtensionId = command.ExtensionId
        };
        targetGroup.Slots = targetGroup.SlotItems.Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null).ToList();
        SaveQuickPanelSettings(target.IsContextual ? "quickpanel-drop-from-launcher-context" : "quickpanel-drop-from-launcher-global");
        LoadSlots();
    }

    private void PollReleaseTarget()
    {
        if (!IsVisible)
        {
            _releaseTargetTimer.Stop();
            ClearReleaseTarget();
            return;
        }

        if (DateTimeOffset.UtcNow < _suspendReleaseTargetPollingUntilUtc)
        {
            return;
        }

        _ = ResolveSlotUnderCursor(occupiedOnly: true, updateTarget: true);
    }

    private SlotViewModel? ResolveSlotUnderCursor(bool occupiedOnly = false, bool updateTarget = true)
    {
        var point = NativeMethods.GetCursorPosition();
        var localPoint = PointFromScreen(point);
        var hit = InputHitTest(localPoint) as DependencyObject;
        while (hit != null)
        {
            if (hit is FrameworkElement { Tag: SlotViewModel taggedSlot })
            {
                if (occupiedOnly && taggedSlot.Command == null)
                {
                    if (updateTarget)
                    {
                        ClearReleaseTarget();
                    }

                    return null;
                }

                if (updateTarget)
                {
                    SetReleaseTarget(taggedSlot);
                }

                return taggedSlot;
            }

            if (hit is FrameworkElement { DataContext: SlotViewModel contextSlot })
            {
                if (occupiedOnly && contextSlot.Command == null)
                {
                    if (updateTarget)
                    {
                        ClearReleaseTarget();
                    }

                    return null;
                }

                if (updateTarget)
                {
                    SetReleaseTarget(contextSlot);
                }

                return contextSlot;
            }

            hit = VisualTreeHelper.GetParent(hit);
        }

        if (updateTarget)
        {
            ClearReleaseTarget();
        }

        return null;
    }

    private static string DescribeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return "hwnd=0x0";
        }

        var titleBuilder = new StringBuilder(256);
        _ = NativeMethods.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return $"hwnd=0x{hwnd.ToInt64():X}, pid={processId}, title=\"{titleBuilder}\"";
    }

    private static ForegroundAppContext? BuildForegroundAppContext(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var titleBuilder = new StringBuilder(256);
        _ = NativeMethods.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        try
        {
            var process = Process.GetProcessById((int)processId);
            return new ForegroundAppContext(process.ProcessName, titleBuilder.ToString().Trim());
        }
        catch
        {
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) => 
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class QuickPanelGroupItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public QuickPanelGroupItem(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }

    public string ShortName => Name.Length <= 2 ? Name : Name[..2];

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value == _isSelected)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class SlotViewModel : INotifyPropertyChanged
{
    public int Index { get; }
    private QuickPanelSlotItem? _item;
    private CommandItem? _command;
    private bool _isFavorite;
    private bool _isReleaseTarget;
    private bool _isContextual;
    private string _folderName = string.Empty;
    private List<string> _folderExtensionIds = [];
    private static readonly Geometry FolderGeometry = Geometry.Parse("M10,4L12,6H20A2,2 0 0,1 22,8V18A2,2 0 0,1 20,20H4A2,2 0 0,1 2,18V6A2,2 0 0,1 4,4H10Z");

    public CommandItem? Command => _command;

    public QuickPanelSlotItem? Item => _item;

    public SlotViewModel(int index, CommandItem? command, bool isFavorite = false, bool isContextual = false)
    {
        Index = index;
        SetCommand(command, isFavorite, isContextual);
    }

    public static SlotViewModel CreateFolder(int index, string folderName, IReadOnlyList<string> folderExtensionIds, CommandItem? previewCommand, bool isContextual)
    {
        var vm = new SlotViewModel(index, null, false, isContextual)
        {
            _item = new QuickPanelSlotItem
            {
                ItemType = "folder",
                FolderName = folderName,
                FolderExtensionIds = folderExtensionIds.ToList()
            },
            _command = null,
            _folderName = folderName,
            _folderExtensionIds = folderExtensionIds.ToList(),
            _isFavorite = false,
            _isContextual = isContextual
        };
        vm.NotifyAll();
        return vm;
    }

    public void SetCommand(CommandItem? command, bool isFavorite = false, bool isContextual = false)
    {
        DetachCommandEvents();
        _item = command == null
            ? null
            : new QuickPanelSlotItem
            {
                ExtensionId = command.ExtensionId
            };
        _command = command;
        _isFavorite = isFavorite;
        _isContextual = isContextual;
        _folderName = string.Empty;
        _folderExtensionIds = [];
        AttachCommandEvents();
        NotifyAll();
    }

    public void SetFavorite(bool isFavorite)
    {
        _isFavorite = isFavorite;
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteLabel));
    }

    public bool IsReleaseTarget
    {
        get => _isReleaseTarget;
        set
        {
            if (value == _isReleaseTarget)
            {
                return;
            }

            _isReleaseTarget = value;
            OnPropertyChanged();
        }
    }

    public bool IsFolder => _item?.IsFolder ?? false;
    public bool IsEmpty => _item == null;
    public bool IsOccupied => _item != null;
    public bool IsFavorite => _isFavorite && !IsFolder;
    public bool IsContextual => _isContextual;
    public bool CanEdit => !IsFolder && _command?.Source == CommandSource.LocalExtension;
    public bool CanPublish => !IsFolder && _command?.Source == CommandSource.LocalExtension;
    public bool CanOpenDirectory => CanEdit && !string.IsNullOrWhiteSpace(_command?.ExtensionDirectoryPath);
    public bool CanRemoveFromFixedSlots => _item != null;
    public string FavoriteLabel => _isFavorite ? "取消收藏" : "收藏";
    public string Title => IsFolder ? _folderName : _command?.Title ?? string.Empty;
    public ImageSource? Icon => IsFolder ? null : _command?.IconSource;
    public Geometry? VectorIcon => IsFolder ? FolderGeometry : _command?.VectorIcon;
    public bool HasImageIcon => !IsFolder && (_command?.HasImageIcon ?? false);
    public bool HasVectorIcon => IsFolder || (_command?.HasVectorIcon ?? false);
    public bool UseGlyphIcon => !IsFolder && (_command?.UseGlyphIcon ?? false);
    public string DisplayGlyph => _command?.DisplayGlyph ?? string.Empty;
    public bool HasNewBadge => !IsFolder && (_command?.HasNewBadge ?? false);
    public bool HasFolderBadge => IsFolder;
    public string FolderBadgeText => _folderExtensionIds.Count > 99 ? "99+" : _folderExtensionIds.Count.ToString();
    public IReadOnlyList<string> FolderExtensionIds => _folderExtensionIds;

    public QuickPanelSlotItem? CloneSlotItem()
    {
        return _item == null
            ? null
            : new QuickPanelSlotItem
            {
                ItemType = _item.ItemType,
                ExtensionId = _item.ExtensionId,
                FolderName = _folderName,
                FolderExtensionIds = _folderExtensionIds.ToList()
            };
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(Item));
        OnPropertyChanged(nameof(Command));
        OnPropertyChanged(nameof(IsFolder));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsOccupied));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(VectorIcon));
        OnPropertyChanged(nameof(HasImageIcon));
        OnPropertyChanged(nameof(HasVectorIcon));
        OnPropertyChanged(nameof(UseGlyphIcon));
        OnPropertyChanged(nameof(DisplayGlyph));
        OnPropertyChanged(nameof(HasNewBadge));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteLabel));
        OnPropertyChanged(nameof(IsContextual));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanPublish));
        OnPropertyChanged(nameof(CanOpenDirectory));
        OnPropertyChanged(nameof(CanRemoveFromFixedSlots));
        OnPropertyChanged(nameof(HasFolderBadge));
        OnPropertyChanged(nameof(FolderBadgeText));
        OnPropertyChanged(nameof(FolderExtensionIds));
    }

    private void AttachCommandEvents()
    {
        if (_command != null)
        {
            _command.PropertyChanged += Command_PropertyChanged;
        }
    }

    private void DetachCommandEvents()
    {
        if (_command != null)
        {
            _command.PropertyChanged -= Command_PropertyChanged;
        }
    }

    private void Command_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName) ||
            string.Equals(e.PropertyName, nameof(CommandItem.HasNewBadge), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(HasNewBadge));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record QuickPanelSlotReference(bool IsContextual, string GroupId, int Index);

public sealed record QuickPanelClipboardItem(string ExtensionId, string Title, bool IsCut, QuickPanelSlotReference? SourceSlot);

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public static System.Windows.Point GetCursorPosition()
    {
        GetCursorPos(out var lpPoint);
        return new System.Windows.Point(lpPoint.X, lpPoint.Y);
    }
}

public class NullToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value == null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}

public class NotNullToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value != null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}

public class BooleanToColorConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not bool val) return System.Windows.Media.Brushes.Transparent;
        string[] colors = (parameter as string ?? "#FF555555|White").Split('|');
        var colorStr = val ? colors[0] : colors[1];
        return (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(colorStr)!;
    }
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}

