using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using OpenQuickHost.Sync;
using Microsoft.Win32;

namespace OpenQuickHost;

public partial class MainWindow
{
    private void UpsertLocalExtensionCommand(CommandItem command)
    {
        _allCommands.RemoveAll(x =>
            x.Source == CommandSource.LocalExtension &&
            x.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
        var insertIndex = GetPreferredLocalExtensionInsertIndex(command.ExtensionId);
        if (insertIndex >= 0 && insertIndex <= _allCommands.Count)
        {
            _allCommands.Insert(insertIndex, command);
        }
        else
        {
            _allCommands.Add(command);
        }

        _localExtensionIndex[command.ExtensionId] = command;
        ApplyNewExtensionState(command);
        RefreshExtensionHotkeys();
    }

    private void RemoveLocalExtensionCommand(string extensionId)
    {
        _allCommands.RemoveAll(x =>
            x.Source == CommandSource.LocalExtension &&
            x.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        _localExtensionIndex.Remove(extensionId);
        RemoveExtensionUiTracking(extensionId);
        RefreshExtensionHotkeys();
    }

    public CommandItem PersistJsonExtensionFromDialog(string json, bool isEditMode)
    {
        HostAssets.AppendLog($"PersistJsonExtensionFromDialog: start, editMode={isEditMode}.");
        var command = LocalExtensionCatalog.SaveJsonExtension(json);
        if (string.Equals(command.Runtime, "csharp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command.Runtime, "cs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command.Runtime, "c#", StringComparison.OrdinalIgnoreCase))
        {
            var prepareResult = Task.Run(() => ScriptExtensionRunner.PreparePortableAssetsAsync(command)).GetAwaiter().GetResult();
            if (!prepareResult.Success)
            {
                HostAssets.AppendLog($"PersistJsonExtensionFromDialog: csharp prebuild skipped -> {prepareResult.Error}");
            }
        }

        if (!isEditMode)
        {
            TrackRecentlyAddedExtension(command.ExtensionId);
        }

        UpsertLocalExtensionCommand(command);
        ApplyFilter(SearchBox.Text);
        SelectedCommand = _allCommands.FirstOrDefault(x => x.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
        CommandList.SelectedItem = SelectedCommand;
        LastRunMessage = isEditMode
            ? $"已更新本地 JSON 扩展：{command.Title}"
            : $"已添加本地 JSON 扩展：{command.Title}";
        HostAssets.AppendLog($"PersistJsonExtensionFromDialog: success, extensionId={command.ExtensionId}.");
        return command;
    }

    public void ReloadLocalExtensionsFromExternal()
    {
        LocalExtensionCatalog.EnsureSampleExtension();
        ReplaceLocalExtensions(LocalExtensionCatalog.LoadEntries(), "已通过外部 Agent API 刷新本地扩展。");
    }

    private void ReloadLocalExtensionsFromWebDav()
    {
        ReplaceLocalExtensions(LocalExtensionCatalog.LoadEntries(), statusText: null);
    }

    public void ReloadLocalExtensionsFromEntries(IReadOnlyList<LocalExtensionCatalogEntry> entries, string? statusText = null)
    {
        ReplaceLocalExtensions(entries, statusText);
    }

    public void ReloadLocalExtensionsFromCommands(IReadOnlyList<CommandItem> commands, string? statusText = null)
    {
        ReplaceLocalExtensions(commands, statusText);
    }

    private int GetPreferredLocalExtensionInsertIndex(string extensionId)
    {
        var trackedIds = _appSettings.RecentlyAddedExtensionIds ?? [];
        if (!trackedIds.Contains(extensionId, StringComparer.OrdinalIgnoreCase))
        {
            return -1;
        }

        var orderedTrackedIds = trackedIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        var localCommands = _allCommands
            .Where(x => x.Source == CommandSource.LocalExtension)
            .ToList();

        var precedingTrackedCount = orderedTrackedIds
            .TakeWhile(id => !id.Equals(extensionId, StringComparison.OrdinalIgnoreCase))
            .Count(id => localCommands.Any(command => command.ExtensionId.Equals(id, StringComparison.OrdinalIgnoreCase)));

        if (precedingTrackedCount <= 0)
        {
            return 0;
        }

        var localInsertIndex = Math.Min(precedingTrackedCount, localCommands.Count);
        var localAtIndex = localCommands.ElementAtOrDefault(localInsertIndex);
        return localAtIndex == null ? _allCommands.Count : _allCommands.IndexOf(localAtIndex);
    }

    private int GetLocalExtensionDisplayOrder(CommandItem command)
    {
        if (command.Source != CommandSource.LocalExtension)
        {
            return int.MaxValue;
        }

        var index = (_appSettings.RecentlyAddedExtensionIds ?? []).FindIndex(id =>
            id.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }

    private void TrackRecentlyAddedExtension(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.RecentlyAddedExtensionIds ??= [];
        settings.UnreadNewExtensionIds ??= [];
        settings.RecentlyAddedExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        settings.UnreadNewExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        settings.RecentlyAddedExtensionIds.Insert(0, extensionId);
        settings.UnreadNewExtensionIds.Insert(0, extensionId);
        if (settings.RecentlyAddedExtensionIds.Count > 50)
        {
            settings.RecentlyAddedExtensionIds = settings.RecentlyAddedExtensionIds.Take(50).ToList();
        }

        if (settings.UnreadNewExtensionIds.Count > 50)
        {
            settings.UnreadNewExtensionIds = settings.UnreadNewExtensionIds.Take(50).ToList();
        }

        AppSettingsStore.Save(settings);
        _appSettings = settings;
    }

    private void RemoveExtensionUiTracking(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        var changed = false;
        settings.RecentlyAddedExtensionIds ??= [];
        settings.UnreadNewExtensionIds ??= [];
        changed |= settings.RecentlyAddedExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase)) > 0;
        changed |= settings.UnreadNewExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!changed)
        {
            return;
        }

        AppSettingsStore.Save(settings);
        _appSettings = settings;
    }

    private void ApplyNewExtensionState(CommandItem command)
    {
        command.SetHasNewBadge(
            command.Source == CommandSource.LocalExtension &&
            (_appSettings.UnreadNewExtensionIds ?? []).Contains(command.ExtensionId, StringComparer.OrdinalIgnoreCase));
    }

    private void MarkExtensionAsSeen(CommandItem? command)
    {
        if (command == null ||
            command.Source != CommandSource.LocalExtension ||
            !command.HasNewBadge)
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.UnreadNewExtensionIds ??= [];
        if (settings.UnreadNewExtensionIds.RemoveAll(id => id.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return;
        }

        AppSettingsStore.Save(settings);
        _appSettings = settings;
        command.SetHasNewBadge(false);
    }

    private CommandItem? ShowJsonExtensionEditorAsync(string initialJson, bool isEditMode)
    {
        return ShowJsonExtensionEditorForOwner(initialJson, isEditMode, this);
    }

    private CommandItem? ShowJsonExtensionEditorForOwner(string initialJson, bool isEditMode, Window? owner)
    {
        var currentJson = initialJson;

        while (true)
        {
            var dialog = new AddJsonExtensionWindow(currentJson, isEditMode)
            {
                Owner = owner
            };
            dialog.ShowDialog();
            HostAssets.AppendLog($"ShowJsonExtensionEditorForOwner: dialog closed, accepted={dialog.WasAccepted}, persistedDirectly={dialog.PersistedCommand != null}.");
            if (!dialog.WasAccepted)
            {
                return null;
            }

            if (dialog.PersistedCommand != null)
            {
                return dialog.PersistedCommand;
            }

            try
            {
                HostAssets.AppendLog("ShowJsonExtensionEditorForOwner: persisting after dialog return.");
                var command = LocalExtensionCatalog.SaveJsonExtension(dialog.JsonContent);
                UpsertLocalExtensionCommand(command);
                ApplyFilter(SearchBox.Text);
                SelectedCommand = _allCommands.FirstOrDefault(x => x.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
                CommandList.SelectedItem = SelectedCommand;
                return command;
            }
            catch (Exception ex)
            {
                currentJson = dialog.JsonContent;
                var retryDialog = new AddJsonExtensionWindow(currentJson, isEditMode)
                {
                    Owner = owner
                };
                retryDialog.ShowError(ex.Message);
                retryDialog.ShowDialog();
                if (!retryDialog.WasAccepted)
                {
                    return null;
                }

                currentJson = retryDialog.JsonContent;
            }
        }
    }

    private static string CreateWebSearchTemplateJson()
    {
        var id = $"web-search-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var manifest = new LocalExtensionManifest
        {
            Id = id,
            Name = "自定义网页搜索",
            Version = "1.0.0",
            Category = "网页搜索",
            Description = "输入关键词后打开指定网站的搜索结果。",
            Keywords = ["网页", "搜索", "自定义"],
            QueryPrefixes = ["搜索", "web"],
            QueryTargetTemplate = "https://www.example.com/search?q={query}",
            Icon = "mdi:magnify"
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    private CommandItem ResolveRunnableCommand(CommandItem command)
    {
        if (command.Source != CommandSource.Cloud)
        {
            return command;
        }

        return _localExtensionIndex.TryGetValue(command.ExtensionId, out var localExtension)
            ? localExtension
            : command;
    }

    public CommandItem? OpenAddExtensionForSlot(Window? owner = null)
    {
        return ShowJsonExtensionEditorForOwner(string.Empty, false, owner ?? this);
    }

    public CommandItem CreateQuickOpenExtensionFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("路径为空，无法创建扩展。");
        }

        var fullPath = Path.GetFullPath(path);
        var isDirectory = Directory.Exists(fullPath);
        var isFile = File.Exists(fullPath);
        if (!isDirectory && !isFile)
        {
            throw new FileNotFoundException("目标文件或目录不存在。", fullPath);
        }

        var fileName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = fullPath;
        }

        var extensionId = $"quick-open-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var manifest = new LocalExtensionManifest
        {
            Id = extensionId,
            Name = fileName,
            Version = "1.0.0",
            Category = isDirectory ? "目录" : "文件",
            Description = isDirectory ? $"打开目录：{fullPath}" : $"打开文件：{fullPath}",
            Keywords = new[]
            {
                fileName,
                Path.GetFileNameWithoutExtension(fileName),
                Path.GetExtension(fullPath),
                fullPath,
                isDirectory ? "目录" : "文件",
                "拖拽导入"
            }.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            OpenTarget = fullPath,
            Icon = fullPath
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        var command = PersistJsonExtensionFromDialog(json, isEditMode: false);
        QueueBackgroundWebDavSync("extension-drag-import");
        return command;
    }

    public void MarkExtensionAsNewFromQuickPanel(CommandItem? command)
    {
        if (command == null || command.Source != CommandSource.LocalExtension)
        {
            return;
        }

        TrackRecentlyAddedExtension(command.ExtensionId);
        if (_localExtensionIndex.TryGetValue(command.ExtensionId, out var existing))
        {
            ApplyNewExtensionState(existing);
        }

        ApplyNewExtensionState(command);
        ApplyFilter(SearchBox.Text);
    }

    public void ShowPanel()
    {
        ShowInTaskbar = true;
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Topmost = true;
        BringMainWindowToFront();
        SetSearchScopePopupOpen(true);

        Dispatcher.BeginInvoke(() =>
        {
            BringMainWindowToFront();
            SearchBox.SelectAll();
        }, DispatcherPriority.ApplicationIdle);
    }

    public void ShowMousePanel()
    {
        _quickPanel?.ShowAtMouse();
    }

    private void BringMainWindowToFront()
    {
        Activate();
        Focus();

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(handle);
        }

        Topmost = true;
        Topmost = false;
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private void StartStartupExtensions()
    {
        _ = Task.Run(async () =>
        {
            // 给软件一点初始化时间
            await Task.Delay(3000);
            
            var startupCommands = _allCommands
                .Where(x => x.Startup?.Mode == "on_app_launch")
                .ToList();

            foreach (var command in startupCommands)
            {
                await Dispatcher.InvokeAsync(async () => 
                {
                    try
                    {
                        HostAssets.AppendLog($"Starting startup extension: {command.Title} ({command.ExtensionId})");
                        await ExecuteCommandAsync(command, launchSource: "app-startup");
                    }
                    catch (Exception ex)
                    {
                        HostAssets.AppendLog($"Failed to start extension {command.Title}: {ex.Message}");
                    }
                });
                
                // 多个自启动扩展之间稍微间隔下，避免瞬间压力过大
                await Task.Delay(500);
            }
        });
    }

    public void StartMousePanelService()
    {
        if (_listenerServicesPaused)
        {
            return;
        }

        InputHookService.Start(
            () => _quickPanel?.ShowAtMouse(),
            () => _quickPanel?.ExecuteHoveredSlotFromHoldRelease());
    }

    public void StopMousePanelService()
    {
        InputHookService.Stop();
    }

    public bool IsMousePanelServiceRunning => InputHookService.IsRunning;

    public bool AreListenerServicesPaused => _listenerServicesPaused;

    public void PauseListenerServices()
    {
        _listenerServicesPaused = true;
        StopMousePanelService();
        KeyboardDoubleTapService.Stop();
        UnregisterLauncherHotkey();
        UnregisterExtensionHotkeys();
        SyncStatus = "已暂停快捷键、扩展快捷键和鼠标面板监听。";
    }

    public void ResumeListenerServices()
    {
        _listenerServicesPaused = false;
        InputHookService.ReloadSettings();
        StartMousePanelService();
        KeyboardDoubleTapService.Start(HandleKeyboardDoubleTap);
        RefreshLauncherHotkeyRegistration();
        RefreshExtensionHotkeys();
        SyncStatus = "已恢复快捷键、扩展快捷键和鼠标面板监听。";
    }

    private void PinAutoHideButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        OnPropertyChanged(nameof(PinButtonBrush));
        OnPropertyChanged(nameof(PinButtonTooltip));
        LastRunMessage = _isPinned ? "已固定窗口，失去焦点不会自动关闭。" : "已取消固定，失去焦点将自动关闭。";
    }

    public void HideToTray()
    {
        ShowInTaskbar = false;
        SetSearchScopePopupOpen(false);
        Hide();
    }

    public void TogglePanelVisibility()
    {
        if (IsVisible)
        {
            HideToTray();
        }
        else
        {
            ShowPanel();
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource?)PresentationSource.FromVisual(this);
        if (_source == null)
        {
            return;
        }

        _source.AddHook(WndProc);
        if (!_listenerServicesPaused)
        {
            KeyboardDoubleTapService.Start(HandleKeyboardDoubleTap);
            RefreshLauncherHotkeyRegistration();
            RefreshExtensionHotkeys();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
        {
            NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged;
            _cloudReconnectTimer.Stop();

            if (_source != null)
            {
                UnregisterExtensionHotkeys();
                UnregisterLauncherHotkey();
                KeyboardDoubleTapService.Stop();
                _source.RemoveHook(WndProc);
            }

            return;
        }

        if (!AppSettingsStore.Load().CloseToTray)
        {
            AllowClose = true;
            System.Windows.Application.Current.Shutdown();
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            SetSearchScopePopupOpen(false);
            HideToTray();
        }
        else if (IsActive)
        {
            SetSearchScopePopupOpen(true);
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        SetSearchScopePopupOpen(false);

        if (_isPinned || !IsVisible)
        {
            return;
        }

        if (IsAutoHideSuppressed())
        {
            return;
        }

        if (OwnedWindows.OfType<Window>().Any(static window => window.IsVisible))
        {
            return;
        }

        if (_quickPanel?.IsVisible == true)
        {
            return;
        }

        if (FooterQuickMenuPopup.IsOpen || CommandList.ContextMenu?.IsOpen == true)
        {
            return;
        }

        HideToTray();
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        SetSearchScopePopupOpen(true);
    }

    private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SetSearchScopePopupOpen(IsVisible && IsActive);
    }

    private void SetSearchScopePopupOpen(bool isOpen)
    {
        if (!IsInitialized)
        {
            return;
        }

        SearchScopePopup.IsOpen = isOpen && IsVisible && WindowState != WindowState.Minimized;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            TogglePanelVisibility();
            handled = true;
        }
        else if (msg == WmHotKey && _registeredExtensionHotkeys.TryGetValue(wParam.ToInt32(), out var command))
        {
            ExecuteCommandFromGlobalHotkey(command);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ExecuteCommandFromGlobalHotkey(CommandItem command)
    {
        var runnable = ResolveRunnableCommand(command);
        if (runnable.HasHostedView || string.Equals(runnable.HotkeyBehavior, "show-view", StringComparison.OrdinalIgnoreCase))
        {
            ShowPanel();
        }

        _ = ExecuteCommandAsync(runnable, string.Empty, "hotkey");
    }

    private void HandleKeyboardDoubleTap(string keyName)
    {
        var configuredShortcut = AppSettingsStore.Load().LauncherHotkey;
        var expectedShortcut = keyName switch
        {
            "Control" => "DoubleCtrl",
            "Alt" => "DoubleAlt",
            _ => string.Empty
        };

        if (!string.Equals(configuredShortcut, expectedShortcut, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        HostAssets.AppendLog($"Launcher keyboard double tap invoked: {keyName}.");
        TogglePanelVisibility();
    }

    private void RefreshExtensionHotkeys()
    {
        if (_source == null)
        {
            return;
        }

        UnregisterExtensionHotkeys();
        _nextExtensionHotkeyId = 0x5400;

        foreach (var command in _localExtensionIndex.Values
                     .Where(command => IsExtensionEnabled(command.ExtensionId))
                     .Where(static x => !string.IsNullOrWhiteSpace(x.GlobalShortcut))
                     .OrderBy(static x => x.Title, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryParseHotkey(command.GlobalShortcut!, out var modifiers, out var key))
            {
                HostAssets.AppendLog($"Invalid global shortcut skipped: {command.Title} -> {command.GlobalShortcut}");
                continue;
            }

            if (key == Key.None || modifiers == 0)
            {
                HostAssets.AppendLog($"Unsupported extension shortcut skipped: {command.Title} -> {command.GlobalShortcut}");
                continue;
            }

            if (command.SupportsQueryArgument && !command.HasHostedView)
            {
                HostAssets.AppendLog($"Query shortcut skipped without hosted view: {command.Title} -> {command.GlobalShortcut}");
                continue;
            }

            var id = _nextExtensionHotkeyId++;
            var success = RegisterHotKey(
                _source.Handle,
                id,
                modifiers | ModNoRepeat,
                (uint)KeyInterop.VirtualKeyFromKey(key));
            if (!success)
            {
                HostAssets.AppendLog($"Failed to register global shortcut: {command.Title} -> {command.GlobalShortcut}");
                continue;
            }

            _registeredExtensionHotkeys[id] = command;
        }
    }

    private void UnregisterExtensionHotkeys()
    {
        if (_source == null)
        {
            _registeredExtensionHotkeys.Clear();
            return;
        }

        foreach (var hotkey in _registeredExtensionHotkeys.Keys.ToArray())
        {
            UnregisterHotKey(_source.Handle, hotkey);
        }

        _registeredExtensionHotkeys.Clear();
    }

    private static bool TryParseHotkey(string shortcut, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return false;
        }

        if (IsDoubleTapShortcut(shortcut))
        {
            return true;
        }

        var segments = shortcut
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var isLast = index == segments.Length - 1;
            if (!isLast)
            {
                switch (segment.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= ModControl;
                        continue;
                    case "alt":
                        modifiers |= ModAlt;
                        continue;
                    case "shift":
                        modifiers |= ModShift;
                        continue;
                    case "win":
                    case "windows":
                        modifiers |= ModWin;
                        continue;
                    default:
                        return false;
                }
            }

            try
            {
                key = segment.ToLowerInvariant() switch
                {
                    "space" => Key.Space,
                    "enter" => Key.Enter,
                    "tab" => Key.Tab,
                    "esc" or "escape" => Key.Escape,
                    _ => (Key)new KeyConverter().ConvertFromInvariantString(segment)!
                };
            }
            catch
            {
                return false;
            }
        }

        return modifiers != 0 && key != Key.None;
    }

    private static bool IsDoubleTapShortcut(string shortcut)
    {
        return string.Equals(shortcut, "DoubleCtrl", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(shortcut, "DoubleAlt", StringComparison.OrdinalIgnoreCase);
    }

    private bool RefreshLauncherHotkeyRegistration()
    {
        if (_source == null)
        {
            return false;
        }

        UnregisterLauncherHotkey();
        var shortcut = AppSettingsStore.Load().LauncherHotkey;
        KeyboardDoubleTapService.ApplyConfiguredShortcut(shortcut);
        if (IsDoubleTapShortcut(shortcut))
        {
            HostAssets.AppendLog($"Launcher hotkey registered as double tap: {shortcut}");
            return true;
        }

        if (!TryParseHotkey(shortcut, out var modifiers, out var key))
        {
            HostAssets.AppendLog($"Invalid launcher hotkey skipped: {shortcut}");
            return false;
        }

        var success = RegisterHotKey(
            _source.Handle,
            HotKeyId,
            modifiers | ModNoRepeat,
            (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!success)
        {
            HostAssets.AppendLog($"Failed to register launcher hotkey: {shortcut}");
        }

        return success;
    }

    private void UnregisterLauncherHotkey()
    {
        if (_source == null)
        {
            return;
        }

        UnregisterHotKey(_source.Handle, HotKeyId);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public IReadOnlyList<CommandItem> GetLocalExtensionsForSettings()
    {
        return _localExtensionIndex.Values
            .OrderBy(GetLocalExtensionDisplayOrder)
            .ThenBy(static x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<CommandItem> GetExtensionsForSettings()
    {
        return _localExtensionIndex.Values
            .OrderBy(GetLocalExtensionDisplayOrder)
            .ThenBy(static x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool IsExtensionEnabled(string extensionId) =>
        !_appSettings.DisabledExtensionIds.Contains(extensionId, StringComparer.OrdinalIgnoreCase);

    public void SetExtensionEnabled(string extensionId, bool enabled)
    {
        var settings = AppSettingsStore.Load();
        settings.DisabledExtensionIds ??= [];
        settings.DisabledExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        if (!enabled)
        {
            settings.DisabledExtensionIds.Add(extensionId);
        }

        AppSettingsStore.Save(settings);
        _appSettings = settings;
        RefreshExtensionHotkeys();
        ApplyFilter(SearchBox.Text);
    }

    private void ReplaceLocalExtensions(IReadOnlyList<LocalExtensionCatalogEntry> entries, string? statusText)
    {
        ReplaceLocalExtensions(entries.Select(LocalExtensionCatalog.CreateCommand).ToList(), statusText);
    }

    private void ReplaceLocalExtensions(IReadOnlyList<CommandItem> commands, string? statusText)
    {
        _allCommands.RemoveAll(x => x.Source == CommandSource.LocalExtension);
        _localExtensionIndex.Clear();
        foreach (var command in commands.OrderBy(GetLocalExtensionDisplayOrder).ThenBy(static x => x.Title, StringComparer.OrdinalIgnoreCase))
        {
            UpsertLocalExtensionCommand(command);
        }

        ApplyFilter(SearchBox.Text);
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            SyncStatus = statusText;
        }
    }

    public IReadOnlyList<CommandItem> GetQuickPanelRecommendedCommands(ForegroundAppContext? context, IEnumerable<string> excludeExtensionIds, int maxCount = 8)
    {
        if (context == null || string.IsNullOrWhiteSpace(context.ProcessName))
        {
            return [];
        }

        var exclude = new HashSet<string>(excludeExtensionIds, StringComparer.OrdinalIgnoreCase);
        var aliases = BuildContextAliases(context.ProcessName, context.WindowTitle);

        return _allCommands
            .Where(static command => !IsInternalCommand(command))
            .Where(command => IsExtensionEnabled(command.ExtensionId))
            .Where(command => !exclude.Contains(command.ExtensionId))
            .Select(command => new
            {
                Command = command,
                Score = ScoreQuickPanelRecommendation(command, aliases, context.WindowTitle)
            })
            .Where(static item => item.Score > 0)
            .OrderByDescending(static item => item.Score)
            .ThenBy(item => item.Command.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, maxCount))
            .Select(static item => item.Command)
            .ToList();
    }

    public Task<(bool ok, string message)> EditExtensionFromSettingsAsync(string extensionId, Window? owner = null)
    {
        try
        {
            if (!_localExtensionIndex.TryGetValue(extensionId, out var editable))
            {
                return Task.FromResult((false, "没有找到对应扩展。"));
            }

            var manifestJson = LocalExtensionCatalog.LoadManifestJson(editable.ExtensionId);
            var updated = ShowJsonExtensionEditorForOwner(manifestJson, isEditMode: true, owner);
            if (updated == null)
            {
                return Task.FromResult((false, string.Empty));
            }

            LastRunMessage = $"已更新本地 JSON 扩展：{updated.Title}";
            QueueBackgroundWebDavSync("extension-edit-settings");
            return Task.FromResult((true, $"已更新扩展：{updated.Title}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"编辑失败：{FormatExceptionMessage(ex)}"));
        }
    }

    public Task<(bool ok, string message)> EditExtensionFromQuickPanelAsync(string extensionId, Window? owner = null)
    {
        return EditExtensionFromSettingsAsync(extensionId, owner);
    }

    public Task<(bool ok, string message)> DeleteExtensionFromSettingsAsync(string extensionId, Window? owner = null)
    {
        try
        {
            if (!_localExtensionIndex.TryGetValue(extensionId, out var deletable))
            {
                return Task.FromResult((false, "没有找到对应扩展。"));
            }

            var confirm = System.Windows.MessageBox.Show(
                owner ?? this,
                $"确认删除“{deletable.Title}”吗？",
                "删除扩展",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return Task.FromResult((false, string.Empty));
            }

            WebDavSyncService.MarkExtensionDeletedLocally(deletable.ExtensionId, deletable.DeclaredVersion);
            ExtensionRecycleBinService.MoveToRecycleBin(deletable.ExtensionId);
            RemoveExtensionFromQuickPanelSettings(deletable.ExtensionId);
            RemoveLocalExtensionCommand(deletable.ExtensionId);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = FilteredCommands.FirstOrDefault();
            CommandList.SelectedItem = SelectedCommand;

            LastRunMessage = $"已将扩展移入回收站：{deletable.Title}";
            SyncStatus = $"已将扩展移入回收站：{deletable.Title}";
            QueueBackgroundWebDavSync("extension-delete-settings");
            return Task.FromResult((true, $"已将扩展移入回收站：{deletable.Title}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"删除失败：{FormatExceptionMessage(ex)}"));
        }
    }

    public Task<(bool ok, string message)> DeleteExtensionFromQuickPanelAsync(string extensionId, Window? owner = null)
    {
        return DeleteExtensionFromSettingsAsync(extensionId, owner);
    }

    private void RemoveExtensionFromQuickPanelSettings(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        var changed = false;

        settings.GlobalFavoriteExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        settings.ContextFavoriteExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        settings.DisabledExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        settings.PinnedSearchScopeCommandIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));

        foreach (var group in settings.QuickPanelGlobalGroups.Concat(settings.QuickPanelContextGroups))
        {
            group.SlotItems ??= [];
            while (group.SlotItems.Count < 12)
            {
                group.SlotItems.Add(null);
            }

            for (var index = 0; index < group.SlotItems.Count; index++)
            {
                var item = group.SlotItems[index];
                if (item == null)
                {
                    continue;
                }

                if (!item.IsFolder)
                {
                    if (string.Equals(item.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase))
                    {
                        group.SlotItems[index] = null;
                        changed = true;
                    }

                    continue;
                }

                var removed = item.FolderExtensionIds.RemoveAll(id => id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
                if (removed <= 0)
                {
                    continue;
                }

                changed = true;
                if (item.FolderExtensionIds.Count == 0)
                {
                    group.SlotItems[index] = null;
                }
                else if (item.FolderExtensionIds.Count == 1)
                {
                    group.SlotItems[index] = new QuickPanelSlotItem
                    {
                        ExtensionId = item.FolderExtensionIds[0]
                    };
                }
            }

            group.Slots = group.SlotItems
                .Take(12)
                .Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null)
                .ToList();
        }

        if (!changed)
        {
            return;
        }

        AppSettingsStore.Save(settings);
        _appSettings = settings;
        _quickPanel?.ReloadSlots();
        NotifyQuickPanelSettingsChanged("extension-delete-cleanup");
    }

    public IReadOnlyList<RecycledExtensionEntry> GetRecycleBinEntriesForSettings()
    {
        return ExtensionRecycleBinService.LoadEntries();
    }

    public Task<(bool ok, string message)> RestoreExtensionFromRecycleBinAsync(string itemId)
    {
        try
        {
            var restored = ExtensionRecycleBinService.RestoreFromRecycleBin(itemId);
            var command = LocalExtensionCatalog.LoadCommands()
                .FirstOrDefault(item => item.ExtensionId.Equals(restored.ExtensionId, StringComparison.OrdinalIgnoreCase));
            if (command == null)
            {
                throw new InvalidOperationException("恢复后的扩展清单无效。");
            }

            WebDavSyncService.MarkExtensionRestoredLocally(command.ExtensionId, command.DeclaredVersion);
            UpsertLocalExtensionCommand(command);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = _allCommands.FirstOrDefault(item =>
                item.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
            CommandList.SelectedItem = SelectedCommand;
            LastRunMessage = $"已从回收站恢复扩展：{command.Title}";
            SyncStatus = $"已恢复扩展：{command.Title}";
            QueueBackgroundWebDavSync("extension-restore-settings");
            return Task.FromResult((true, $"已恢复扩展：{command.Title}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"恢复失败：{FormatExceptionMessage(ex)}"));
        }
    }

    public Task<(bool ok, string message)> PurgeExtensionFromRecycleBinAsync(string itemId)
    {
        try
        {
            var deleted = ExtensionRecycleBinService.DeletePermanently(itemId);
            WebDavSyncService.MarkExtensionPurgedLocally(deleted.ExtensionId, deleted.Version);
            RemoveLocalExtensionCommand(deleted.ExtensionId);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = FilteredCommands.FirstOrDefault();
            CommandList.SelectedItem = SelectedCommand;
            LastRunMessage = $"已彻底删除扩展：{deleted.Title}";
            SyncStatus = $"已彻底删除扩展：{deleted.Title}";
            QueueBackgroundWebDavSync("extension-purge-settings");
            return Task.FromResult((true, $"已彻底删除扩展：{deleted.Title}，会同步到其他设备。"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"彻底删除失败：{FormatExceptionMessage(ex)}"));
        }
    }

    private static Dictionary<string, string> ParseProtocolQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var segments = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            var pair = segment.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0]);
            var value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static string? GetProtocolValue(IReadOnlyDictionary<string, string> parameters, string key)
    {
        return parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    public bool TryOpenExtensionDirectory(string extensionId, out string message)
    {
        message = string.Empty;
        if (!_localExtensionIndex.TryGetValue(extensionId, out var command) ||
            string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath) ||
            !Directory.Exists(command.ExtensionDirectoryPath))
        {
            message = "扩展目录不存在。";
            return false;
        }

        var directoryPath = command.ExtensionDirectoryPath!;
        Process.Start(new ProcessStartInfo
        {
            FileName = directoryPath,
            UseShellExecute = true
        });
        return true;
    }

    public Task<(bool ok, string message)> UpdateExtensionShortcutFromSettingsAsync(string extensionId, string? shortcut)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(shortcut) &&
                (!TryParseHotkey(shortcut, out _, out _) || IsDoubleTapShortcut(shortcut)))
            {
                return Task.FromResult((false, "快捷键格式无效。示例：Ctrl+Alt+T"));
            }

            var updated = LocalExtensionCatalog.SetGlobalShortcut(extensionId, shortcut);
            UpsertLocalExtensionCommand(updated);
            ApplyFilter(SearchBox.Text);
            QueueBackgroundWebDavSync("extension-shortcut-settings");

            var message = string.IsNullOrWhiteSpace(updated.GlobalShortcut)
                ? $"已清除快捷键：{updated.Title}"
                : $"已设置快捷键：{updated.Title} -> {updated.GlobalShortcut}";
            return Task.FromResult((true, message));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, $"设置快捷键失败：{FormatExceptionMessage(ex)}"));
        }
    }

    private void OpenCommandActionsMenu()
    {
        CommandList.Focus();
        if (!UpdateCommandContextMenuState() || CommandList.ContextMenu == null || !CommandList.ContextMenu.HasItems)
        {
            return;
        }

        // 获取当前选中的列表项
        var selectedItem = CommandList.ItemContainerGenerator.ContainerFromItem(SelectedCommand) as FrameworkElement;
        
        if (selectedItem != null)
        {
            // 在选中的列表项右侧显示菜单
            CommandList.ContextMenu.PlacementTarget = selectedItem;
            CommandList.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
        }
        else
        {
            // 如果找不到选中项，则在列表控件上显示
            CommandList.ContextMenu.PlacementTarget = CommandList;
            CommandList.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Center;
        }
        
        CommandList.ContextMenu.IsOpen = true;
    }

    public void OpenSettingsWindow(string? sectionKey = null)
    {
        var settingsWindow = new SettingsWindow(this);
        if (sectionKey != null) settingsWindow.NavigateTo(sectionKey);
        settingsWindow.Show();
    }

    private async Task CreateDesktopShortcutAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可创建快捷方式的命令。";
            return;
        }

        var command = ResolveRunnableCommand(sourceCommand);
        if (string.IsNullOrWhiteSpace(command.OpenTarget) || IsInternalCommand(command))
        {
            SyncStatus = "当前命令不支持创建桌面快捷方式。";
            return;
        }

        try
        {
            var path = DesktopShortcutService.CreateShortcut(command.Title, command.OpenTarget);
            LastRunMessage = $"已创建桌面快捷方式：{path}";
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SyncStatus = $"创建快捷方式失败：{FormatExceptionMessage(ex)}";
        }
    }

    private Task RenameSelectedExtensionAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可重命名的扩展。";
            return Task.CompletedTask;
        }

        var extension = ResolveRunnableCommand(sourceCommand);
        if (extension.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地扩展，不能直接重命名。";
            return Task.CompletedTask;
        }

        var dialog = new SimpleTextInputWindow("重命名扩展", "输入新的扩展名称。", extension.Title)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        try
        {
            var renamed = LocalExtensionCatalog.RenameExtension(extension.ExtensionId, dialog.ValueText);
            UpsertLocalExtensionCommand(renamed);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = _allCommands.FirstOrDefault(x => x.ExtensionId.Equals(renamed.ExtensionId, StringComparison.OrdinalIgnoreCase));
            CommandList.SelectedItem = SelectedCommand;
            LastRunMessage = $"已重命名扩展：{renamed.Title}";
            QueueBackgroundWebDavSync("extension-rename");
        }
        catch (Exception ex)
        {
            SyncStatus = $"重命名失败：{FormatExceptionMessage(ex)}";
        }

        return Task.CompletedTask;
    }

    private void AddToQuickPanelMenuItem_Click(object sender, RoutedEventArgs e)
    {
        AddCurrentCommandToQuickPanel();
    }

}
