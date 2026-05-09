using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows;
using OpenQuickHost.Sync;
using Forms = System.Windows.Forms;

namespace OpenQuickHost;

public partial class MainWindow
{
    private async Task RefreshCloudStateAsync(bool allowLoginPrompt = true)
    {
        if (_cloudSyncClient == null)
        {
            await SyncPersonalWebDavAsync(showDisabledMessage: true);
            return;
        }

        try
        {
            SyncStatus = "正在读取账号状态和云端配置...";
            if (!await EnsureAuthenticatedAsync(allowPrompt: allowLoginPrompt))
            {
                return;
            }

            var me = await _cloudSyncClient.GetMeAsync();
            var pulledConfig = await PullWebDavConfigFromCloudAsync();
            var pulledQuickPanelConfig = await PullQuickPanelConfigFromCloudAsync();
            _allCommands.RemoveAll(x => x.Source == CommandSource.Cloud);
            foreach (var command in _allCommands)
            {
                command.ClearCloudData();
            }
            ApplyFilter(SearchBox.Text);
            SyncStatus = $"已登录 {me?.Username ?? _cloudSyncClient.CurrentUserLabel}";
            ResetSilentCloudReconnect();
            LastRunMessage = pulledConfig || pulledQuickPanelConfig
                ? "已同步账号状态，并更新了云端配置。"
                : "已同步账号状态。";
            OnPropertyChanged(nameof(SyncSummaryText));
        }
        catch (Exception ex)
        {
            if (!allowLoginPrompt && IsTransientNetworkException(ex))
            {
                ScheduleSilentCloudReconnect("refresh-cloud-failed");
            }

            if (allowLoginPrompt && await TryRecoverAuthenticationAsync(ex))
            {
                await RefreshCloudStateAsync();
                return;
            }

            SyncStatus = $"云同步读取失败：{FormatExceptionMessage(ex)}";
        }
    }

    private Task SyncSelectedCommandAsync()
    {
        SyncStatus = "Cloudflare 当前只同步账号状态和坚果云 / WebDAV 配置，扩展分享稍后接入。";
        return Task.CompletedTask;
    }

    private async Task DownloadSelectedCommandAsync()
    {
        if (_cloudSyncClient == null)
        {
            SyncStatus = "云同步未配置，无法下载。";
            return;
        }

        if (SelectedCommand == null)
        {
            SyncStatus = "没有可下载的命令。";
            return;
        }

        if (!SelectedCommand.HasArchive)
        {
            SyncStatus = "当前命令在云端没有扩展包。";
            return;
        }

        try
        {
            if (!await EnsureAuthenticatedAsync())
            {
                return;
            }

            SyncStatus = $"正在下载 {SelectedCommand.Title} 的扩展包 ...";
            var packageBytes = await _cloudSyncClient.DownloadExtensionArchiveAsync(SelectedCommand.ExtensionId);
            var version = SelectedCommand.CloudVersion ?? "0.1.0";
            var path = await ExtensionPackageService.SavePackageAsync(SelectedCommand.ExtensionId, version, packageBytes);
            SelectedCommand.SetLocalPackagePath(path);
            LastRunMessage = $"扩展包已下载到本地：{path}";
            SyncStatus = $"下载完成：{SelectedCommand.Title}";
        }
        catch (Exception ex)
        {
            if (await TryRecoverAuthenticationAsync(ex))
            {
                await DownloadSelectedCommandAsync();
                return;
            }

            SyncStatus = $"下载失败：{FormatExceptionMessage(ex)}";
        }
    }

    private async Task<bool> PublishSelectedExtensionAsync()
    {
        if (_cloudSyncClient == null)
        {
            SyncStatus = "云同步未配置，无法发布到商店。";
            return false;
        }

        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可发布的扩展。";
            return false;
        }

        var command = ResolveRunnableCommand(sourceCommand);
        if (command.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "只有本地扩展才能发布到商店。";
            return false;
        }

        try
        {
            if (!await EnsureAuthenticatedAsync())
            {
                SyncStatus = "发布已取消：未完成登录。";
                return false;
            }

            SyncStatus = $"正在发布扩展：{command.Title} ...";
            var version = string.IsNullOrWhiteSpace(command.DeclaredVersion) ? "0.1.0" : command.DeclaredVersion;
            var publishedIcon = await _cloudSyncClient.PublishIconAsync(command, version);
            var packageBytes = ExtensionPackageService.BuildPackage(command, version, publishedIcon);
            await _cloudSyncClient.UpsertExtensionAsync(command, publishedIcon);
            await _cloudSyncClient.UploadExtensionArchiveAsync(command, packageBytes, version);
            await _cloudSyncClient.UpsertUserExtensionAsync(command);
            command.MarkAsSynced(version);
            LastRunMessage = $"已发布到扩展商店：{command.Title} (v{version})";
            SyncStatus = $"发布成功：{command.Title}";
            return true;
        }
        catch (Exception ex)
        {
            if (await TryRecoverAuthenticationAsync(ex))
            {
                return await PublishSelectedExtensionAsync();
            }

            SyncStatus = $"发布失败：{FormatExceptionMessage(ex)}";
            return false;
        }
    }

    public async Task<(bool ok, string message)> PublishExtensionFromSettingsAsync(string extensionId)
    {
        try
        {
            if (!_localExtensionIndex.TryGetValue(extensionId, out var command))
            {
                return (false, "没有找到对应扩展。");
            }

            SelectedCommand = command;
            CommandList.SelectedItem = command;
            var ok = await PublishSelectedExtensionAsync();
            return (ok, SyncStatus);
        }
        catch (Exception ex)
        {
            return (false, $"发布失败：{FormatExceptionMessage(ex)}");
        }
    }

    public async Task<(bool ok, string message)> UnpublishExtensionFromSettingsAsync(string extensionId)
    {
        try
        {
            if (_cloudSyncClient == null)
            {
                return (false, "云同步未配置，无法下线扩展。");
            }

            if (!_localExtensionIndex.TryGetValue(extensionId, out var command))
            {
                return (false, "没有找到对应扩展。");
            }

            if (!await EnsureAuthenticatedAsync())
            {
                return (false, "下线已取消：未完成登录。");
            }

            await _cloudSyncClient.DeleteExtensionAsync(command.ExtensionId);
            SyncStatus = $"已下线扩展：{command.Title}";
            LastRunMessage = $"扩展已从商店下线：{command.Title}";
            return (true, SyncStatus);
        }
        catch (Exception ex)
        {
            if (await TryRecoverAuthenticationAsync(ex))
            {
                return await UnpublishExtensionFromSettingsAsync(extensionId);
            }

            return (false, $"下线失败：{FormatExceptionMessage(ex)}");
        }
    }

    public async Task<IReadOnlyDictionary<string, CloudExtensionRecord>> GetOwnedPublishedExtensionsForSettingsAsync()
    {
        if (_cloudSyncClient == null)
        {
            return new Dictionary<string, CloudExtensionRecord>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            if (!await EnsureAuthenticatedAsync(allowPrompt: false))
            {
                return new Dictionary<string, CloudExtensionRecord>(StringComparer.OrdinalIgnoreCase);
            }

            var me = await _cloudSyncClient.GetMeAsync();
            if (me == null || string.IsNullOrWhiteSpace(me.UserId))
            {
                return new Dictionary<string, CloudExtensionRecord>(StringComparer.OrdinalIgnoreCase);
            }

            var items = await _cloudSyncClient.GetExtensionsAsync();
            var owned = items
                .Where(item =>
                    item.IsPublished != 0 &&
                    item.PublisherUserId.Equals(me.UserId, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(item => item.ExtensionId, item => item, StringComparer.OrdinalIgnoreCase);
            HostAssets.AppendLog($"Owned published extensions fetched for settings: userId={me.UserId}, count={owned.Count}");
            return owned;
        }
        catch
        {
            return new Dictionary<string, CloudExtensionRecord>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task HandleProtocolLaunchAsync(string protocolArgument)
    {
        if (!Uri.TryCreate(protocolArgument, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "yanzi", StringComparison.OrdinalIgnoreCase))
        {
            SyncStatus = "收到的本地协议无效。";
            return;
        }

        if (!string.Equals(uri.Host, "install", StringComparison.OrdinalIgnoreCase))
        {
            SyncStatus = $"暂不支持的协议动作：{uri.Host}";
            return;
        }

        var parameters = ParseProtocolQuery(uri.Query);
        var source = GetProtocolValue(parameters, "source");
        var extensionId = GetProtocolValue(parameters, "extensionId") ?? GetProtocolValue(parameters, "id");
        if (string.IsNullOrWhiteSpace(source))
        {
            SyncStatus = "安装协议缺少 source 参数。";
            return;
        }

        try
        {
            SyncStatus = "正在通过本地协议下载安装扩展 ...";
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            var packageBytes = await httpClient.GetByteArrayAsync(source);
            var result = await ExtensionInstallService.InstallPackageAsync(packageBytes, extensionId);
            ReloadLocalExtensionsFromExternal();
            RevealInstalledExtension(result.ExtensionId, result.Name);
            LastRunMessage = $"已安装扩展：{result.Name} ({result.ExtensionId})";
            SyncStatus = $"安装成功：{result.Name}";
            if (System.Windows.Application.Current is App app)
            {
                app.ShowDesktopNotification("燕子扩展已安装", $"{result.Name} 已安装到本地扩展目录。");
            }
            ShowPanel();
        }
        catch (Exception ex)
        {
            SyncStatus = $"安装失败：{FormatExceptionMessage(ex)}";
            HostAssets.AppendLog($"Protocol install failed: source={source}, extensionId={extensionId}, error={ex}");
            if (System.Windows.Application.Current is App app)
            {
                app.ShowDesktopNotification("燕子扩展安装失败", FormatExceptionMessage(ex), Forms.ToolTipIcon.Error);
            }
            System.Windows.MessageBox.Show(
                this,
                $"扩展安装失败：{FormatExceptionMessage(ex)}",
                "燕子扩展安装失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ShowPanel();
        }
    }

    private void RevealInstalledExtension(string extensionId, string? extensionName = null)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return;
        }

        ShowPanel();
        var displayQuery = string.IsNullOrWhiteSpace(extensionName) ? extensionId : extensionName.Trim();
        SearchBox.Text = $"@扩展 {displayQuery}";
        SearchBox.CaretIndex = SearchBox.Text.Length;
        ApplyFilter(SearchBox.Text);

        var installedCommand = FilteredCommands.FirstOrDefault(command =>
            command.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        if (installedCommand == null)
        {
            return;
        }

        var currentIndex = FilteredCommands.IndexOf(installedCommand);
        if (currentIndex > 0)
        {
            FilteredCommands.Move(currentIndex, 0);
        }

        SelectedCommand = installedCommand;
        CommandList.SelectedItem = installedCommand;
        CommandList.ScrollIntoView(installedCommand);
    }

    private Task AddJsonExtensionAsync()
    {
        try
        {
            var command = ShowJsonExtensionEditorAsync(
                string.Empty,
                isEditMode: false);
            if (command == null)
            {
                return Task.CompletedTask;
            }

            LastRunMessage = $"已添加本地 JSON 扩展：{command.Title}";
            QueueBackgroundWebDavSync("extension-add");
        }
        catch (Exception ex)
        {
            HostAssets.AppendDevLog($"AddJsonExtensionAsync failed: {ex}");
            SyncStatus = $"添加扩展失败：{FormatExceptionMessage(ex)}";
        }

        return Task.CompletedTask;
    }

    private Task EditSelectedExtensionAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可编辑的扩展。";
            return Task.CompletedTask;
        }

        var editable = ResolveRunnableCommand(sourceCommand);
        if (editable.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地 JSON 扩展，不能直接编辑。";
            return Task.CompletedTask;
        }

        try
        {
            var manifestJson = LocalExtensionCatalog.LoadManifestJson(editable.ExtensionId);
            var updated = ShowJsonExtensionEditorAsync(manifestJson, isEditMode: true);
            if (updated == null)
            {
                return Task.CompletedTask;
            }

            LastRunMessage = $"已更新本地 JSON 扩展：{updated.Title}";
            QueueBackgroundWebDavSync("extension-edit");
        }
        catch (Exception ex)
        {
            SyncStatus = $"编辑失败：{FormatExceptionMessage(ex)}";
        }

        return Task.CompletedTask;
    }

    private async Task DeleteSelectedExtensionAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可删除的扩展。";
            return;
        }

        var deletable = ResolveRunnableCommand(sourceCommand);
        if (deletable.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地 JSON 扩展，不能直接删除。";
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"确认将扩展“{deletable.Title}”移入回收站吗？\n如果已启用坚果云/WebDAV，同步器会在后台把这次删除同步到其他设备。",
            "移入扩展回收站",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            WebDavSyncService.MarkExtensionDeletedLocally(deletable.ExtensionId, deletable.DeclaredVersion);
            ExtensionRecycleBinService.MoveToRecycleBin(deletable.ExtensionId);
            RemoveLocalExtensionCommand(deletable.ExtensionId);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = FilteredCommands.FirstOrDefault();
            CommandList.SelectedItem = SelectedCommand;

            LastRunMessage = $"已将扩展移入回收站：{deletable.Title}";
            SyncStatus = $"已将扩展移入回收站：{deletable.Title}";
            QueueBackgroundWebDavSync("extension-delete");
        }
        catch (Exception ex)
        {
            if (await TryRecoverAuthenticationAsync(ex))
            {
                await DeleteSelectedExtensionAsync();
                return;
            }

            SyncStatus = $"删除失败：{FormatExceptionMessage(ex)}";
        }
    }

    private async Task<bool> EnsureAuthenticatedAsync(bool forcePrompt = false, bool allowPrompt = true)
    {
        if (_cloudSyncClient == null)
        {
            return false;
        }

        if (forcePrompt || !_cloudSyncClient.HasCredential)
        {
            if (!allowPrompt)
            {
                SyncStatus = "未登录，已跳过云端账号同步。";
                return false;
            }

            if (!ShowLoginDialog())
            {
                SyncStatus = "未登录，云同步不可用。";
                return false;
            }
        }

        try
        {
            await _cloudSyncClient.EnsureAuthenticatedAsync();
            OnPropertyChanged(nameof(SyncSummaryText));
            return true;
        }
        catch (Exception ex)
        {
            if (!allowPrompt)
            {
                SyncStatus = $"云端账号同步失败，已跳过登录弹窗：{FormatExceptionMessage(ex)}";
                HostAssets.AppendLog($"Cloud silent auth failed: {FormatExceptionMessage(ex)}");
                if (IsTransientNetworkException(ex))
                {
                    ScheduleSilentCloudReconnect("silent-auth-failed");
                }
                return false;
            }

            if (ShowLoginDialog(FormatExceptionMessage(ex)))
            {
                await _cloudSyncClient.EnsureAuthenticatedAsync();
                OnPropertyChanged(nameof(SyncSummaryText));
                return true;
            }

            SyncStatus = "未登录，云同步不可用。";
            return false;
        }
    }

    private async Task<bool> TryRecoverAuthenticationAsync(Exception ex)
    {
        if (_cloudSyncClient == null)
        {
            return false;
        }

        var message = ex.Message ?? string.Empty;
        if (!message.Contains("401", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("登录", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("凭据", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _cloudSyncClient.ClearSessionOnly();
        if (await EnsureAuthenticatedAsync(allowPrompt: false))
        {
            return true;
        }

        return await EnsureAuthenticatedAsync(forcePrompt: true);
    }

    private bool ShowLoginDialog(string? errorMessage = null)
    {
        if (_cloudSyncClient == null || _authPromptActive)
        {
            return false;
        }

        _authPromptActive = true;
        try
        {
            var saved = SecureCredentialStore.Load();
            var dialog = new LoginWindow(saved?.LoginEmail);
            dialog.SendRegistrationCodeAsync = (email, username) => _cloudSyncClient.SendRegistrationCodeAsync(email, username);
            dialog.SendPasswordResetCodeAsync = (email) => _cloudSyncClient.SendPasswordResetCodeAsync(email);
            dialog.RegisterAsyncHandler = (email, username, password, code) => _cloudSyncClient.RegisterAsync(email, username, password, code);
            dialog.ResetPasswordAsyncHandler = (email, password, code) => _cloudSyncClient.ResetPasswordAsync(email, password, code);
            if (IsVisible)
            {
                dialog.Owner = this;
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                dialog.ShowError(errorMessage);
            }

            var result = dialog.ShowDialog();
            if (result != true)
            {
                return false;
            }

            _cloudSyncClient.SetCredential(dialog.LoginEmail, dialog.Password, dialog.RememberCredential);
            return true;
        }
        finally
        {
            _authPromptActive = false;
        }
    }

    private static string FormatExceptionMessage(Exception ex)
    {
        var messages = new List<string>();
        Exception? current = ex;
        while (current != null)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                messages.Add(current.Message.Trim());
            }

            current = current.InnerException;
        }

        return string.Join(" | ", messages.Distinct(StringComparer.Ordinal));
    }

    private void NetworkChange_NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!e.IsAvailable)
            {
                return;
            }

            HostAssets.AppendLog("Network availability restored, scheduling silent cloud reconnect.");
            ScheduleSilentCloudReconnect("network-available", immediate: true);
        });
    }

    private void NetworkChange_NetworkAddressChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return;
            }

            HostAssets.AppendLog("Network address changed, scheduling silent cloud reconnect.");
            ScheduleSilentCloudReconnect("network-address-changed", immediate: true);
        });
    }

    private void ScheduleSilentCloudReconnect(string reason, bool immediate = false)
    {
        if (_cloudSyncClient == null || !_cloudSyncClient.HasCredential || !_appSettings.RefreshCloudOnStartup)
        {
            return;
        }

        _cloudReconnectPendingReason = reason;
        if (_cloudReconnectInProgress)
        {
            HostAssets.AppendLog($"Silent cloud reconnect already running, marked pending: {reason}");
            return;
        }

        var delay = immediate ? TimeSpan.FromSeconds(1) : GetSilentCloudReconnectDelay(_cloudReconnectAttemptCount);
        if (_cloudReconnectTimer.IsEnabled && _cloudReconnectTimer.Interval <= delay)
        {
            return;
        }

        _cloudReconnectTimer.Stop();
        _cloudReconnectTimer.Interval = delay;
        _cloudReconnectTimer.Start();
        HostAssets.AppendLog($"Silent cloud reconnect scheduled: reason={reason}, delay={delay}.");
    }

    private async void CloudReconnectTimer_Tick(object? sender, EventArgs e)
    {
        _cloudReconnectTimer.Stop();
        if (_cloudReconnectInProgress || _cloudSyncClient == null || !_cloudSyncClient.HasCredential || !_appSettings.RefreshCloudOnStartup)
        {
            return;
        }

        _cloudReconnectInProgress = true;
        var reason = _cloudReconnectPendingReason ?? "timer";
        try
        {
            HostAssets.AppendLog($"Silent cloud reconnect attempt started: reason={reason}, attempt={_cloudReconnectAttemptCount + 1}.");
            await RefreshCloudStateAsync(allowLoginPrompt: false);
        }
        catch
        {
            // RefreshCloudStateAsync records failures and schedules follow-up retries.
        }
        finally
        {
            _cloudReconnectInProgress = false;
        }
    }

    private void ResetSilentCloudReconnect()
    {
        _cloudReconnectAttemptCount = 0;
        _cloudReconnectPendingReason = null;
        _cloudReconnectTimer.Stop();
    }

    private TimeSpan GetSilentCloudReconnectDelay(int attemptCount)
    {
        var seconds = attemptCount switch
        {
            <= 0 => 5,
            1 => 15,
            2 => 30,
            3 => 60,
            4 => 120,
            _ => 300
        };

        _cloudReconnectAttemptCount = Math.Min(attemptCount + 1, 5);
        return TimeSpan.FromSeconds(seconds);
    }

    private static bool IsTransientNetworkException(Exception ex)
    {
        var message = FormatExceptionMessage(ex);
        return message.Contains("SSL connection could not be established", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unexpected EOF", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("0 bytes from the transport stream", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("connection attempt failed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("timed out", StringComparison.OrdinalIgnoreCase);
    }

    private async Task SyncPersonalWebDavAsync(bool showDisabledMessage)
    {
        var settings = AppSettingsStore.Load();
        if (!settings.EnableWebDavSync)
        {
            if (showDisabledMessage)
            {
                SyncStatus = "未启用个人 WebDAV 扩展同步。";
            }

            return;
        }

        try
        {
            var service = new WebDavSyncService(settings);
            var result = await service.SyncExtensionsAsync();
            ReloadLocalExtensionsFromWebDav();
            LastRunMessage = $"个人扩展同步完成：上传 {result.UploadedCount} 个，拉取 {result.PulledCount} 个。";
        }
        catch (Exception ex)
        {
            SyncStatus = $"个人扩展同步失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void StartBackgroundWebDavSync()
    {
        if (AppSettingsStore.Load().EnableWebDavSync && !_backgroundWebDavSyncTimer.IsEnabled)
        {
            _backgroundWebDavSyncTimer.Start();
        }
    }

    private void QueueBackgroundWebDavSync(string reason)
    {
        var settings = AppSettingsStore.Load();
        if (!settings.EnableWebDavSync)
        {
            return;
        }

        StartBackgroundWebDavSync();
        if (_backgroundWebDavSyncRunning)
        {
            _backgroundWebDavSyncRequested = true;
            HostAssets.AppendLog($"WebDAV background sync queued while running: {reason}");
            return;
        }

        _ = RunBackgroundWebDavSyncAsync(reason);
    }

    private async Task RunBackgroundWebDavSyncAsync(string reason)
    {
        _backgroundWebDavSyncRunning = true;
        try
        {
            HostAssets.AppendLog($"WebDAV background sync started: {reason}");
            var service = new WebDavSyncService(AppSettingsStore.Load());
            var result = await service.SyncExtensionsAsync();
            ReloadLocalExtensionsFromWebDav();
            SyncStatus = $"个人扩展后台同步完成：上传 {result.UploadedCount} 个，拉取 {result.PulledCount} 个。";
            HostAssets.AppendLog($"WebDAV background sync completed: {reason}, uploaded={result.UploadedCount}, pulled={result.PulledCount}");
        }
        catch (Exception ex)
        {
            var message = FormatExceptionMessage(ex);
            SyncStatus = $"个人扩展后台同步失败：{message}";
            HostAssets.AppendLog($"WebDAV background sync failed: {reason} -> {message}");
        }
        finally
        {
            _backgroundWebDavSyncRunning = false;
            if (_backgroundWebDavSyncRequested)
            {
                _backgroundWebDavSyncRequested = false;
                QueueBackgroundWebDavSync("queued");
            }
        }
    }

    private async Task<bool> PullWebDavConfigFromCloudAsync()
    {
        if (_cloudSyncClient == null)
        {
            return false;
        }

        var snapshot = await _cloudSyncClient.GetUserConfigAsync<CloudWebDavConfigSnapshot>(CloudWebDavConfigId);
        if (snapshot == null)
        {
            HostAssets.AppendLog("WebDAV cloud pull: no user config found.");
            if (ShouldSyncLocalWebDavConfigToCloud())
            {
                await PushWebDavConfigToCloudAsync("cloud-refresh-bootstrap");
            }
            return false;
        }

        HostAssets.AppendLog(
            $"WebDAV cloud pull: enabled={snapshot.EnableWebDavSync}, serverUrl={snapshot.WebDavServerUrl}, rootPath={snapshot.WebDavRootPath}, username={snapshot.WebDavUsername}, hasPassword={!string.IsNullOrWhiteSpace(snapshot.WebDavPassword)}");

        var settings = AppSettingsStore.Load();
        var shouldDefaultEnable = snapshot.EnableWebDavSync || HasWebDavConfigValues(snapshot.WebDavServerUrl, snapshot.WebDavRootPath, snapshot.WebDavUsername, snapshot.WebDavPassword);
        var resolvedEnabled = settings.WebDavSyncManuallyDisabled ? false : shouldDefaultEnable;
        var changed =
            settings.EnableWebDavSync != resolvedEnabled ||
            !string.Equals(settings.WebDavServerUrl, snapshot.WebDavServerUrl, StringComparison.Ordinal) ||
            !string.Equals(settings.WebDavRootPath, snapshot.WebDavRootPath, StringComparison.Ordinal) ||
            !string.Equals(settings.WebDavUsername, snapshot.WebDavUsername, StringComparison.Ordinal);
        var credential = WebDavCredentialStore.Load();
        var passwordChanged = !string.Equals(credential?.Password, snapshot.WebDavPassword, StringComparison.Ordinal);
        if (!changed)
        {
            if (passwordChanged && !string.IsNullOrWhiteSpace(snapshot.WebDavPassword))
            {
                HostAssets.AppendLog("WebDAV cloud pull: applying password-only update.");
                SaveWebDavCredential(snapshot.WebDavUsername ?? string.Empty, snapshot.WebDavPassword);
                NotifySettingsWindowWebDavConfigChanged();
                return true;
            }

            HostAssets.AppendLog("WebDAV cloud pull: no local changes detected.");
            return false;
        }

        settings.EnableWebDavSync = resolvedEnabled;
        settings.WebDavServerUrl = string.IsNullOrWhiteSpace(snapshot.WebDavServerUrl)
            ? settings.WebDavServerUrl
            : snapshot.WebDavServerUrl.Trim();
        settings.WebDavRootPath = string.IsNullOrWhiteSpace(snapshot.WebDavRootPath)
            ? "/yanzi"
            : snapshot.WebDavRootPath.Trim();
        settings.WebDavUsername = snapshot.WebDavUsername?.Trim() ?? string.Empty;
        if (resolvedEnabled)
        {
            settings.WebDavSyncManuallyDisabled = false;
        }
        AppSettingsStore.Save(settings);
        _appSettings = settings;
        if (!string.IsNullOrWhiteSpace(snapshot.WebDavPassword))
        {
            SaveWebDavCredential(snapshot.WebDavUsername ?? string.Empty, snapshot.WebDavPassword);
        }
        if (settings.EnableWebDavSync)
        {
            StartBackgroundWebDavSync();
        }
        else
        {
            _backgroundWebDavSyncTimer.Stop();
        }

        HostAssets.AppendLog(
            $"WebDAV cloud pull applied: enabled={settings.EnableWebDavSync}, serverUrl={settings.WebDavServerUrl}, rootPath={settings.WebDavRootPath}, username={settings.WebDavUsername}, passwordSaved={!string.IsNullOrWhiteSpace(snapshot.WebDavPassword)}");
        NotifySettingsWindowWebDavConfigChanged();
        return true;
    }

    private void QueueCloudWebDavConfigSync(string reason)
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        _ = PushWebDavConfigToCloudSafeAsync(reason);
    }

    private void QueueCloudQuickPanelConfigSync(string reason)
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        _ = PushQuickPanelConfigToCloudSafeAsync(reason);
    }

    private async Task PushWebDavConfigToCloudSafeAsync(string reason)
    {
        try
        {
            await PushWebDavConfigToCloudAsync(reason);
            HostAssets.AppendLog($"Cloud WebDAV config synced: {reason}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Cloud WebDAV config sync skipped: {reason} -> {FormatExceptionMessage(ex)}");
        }
    }

    private async Task PushQuickPanelConfigToCloudSafeAsync(string reason)
    {
        try
        {
            await PushQuickPanelConfigToCloudAsync(reason);
            HostAssets.AppendLog($"Cloud quick panel config synced: {reason}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Cloud quick panel config sync skipped: {reason} -> {FormatExceptionMessage(ex)}");
        }
    }

    private async Task PushWebDavConfigToCloudAsync(string reason)
    {
        if (_cloudSyncClient == null || !_cloudSyncClient.HasCredential || !ShouldSyncLocalWebDavConfigToCloud())
        {
            HostAssets.AppendLog($"WebDAV cloud push skipped: {reason}");
            return;
        }

        await _cloudSyncClient.EnsureAuthenticatedAsync();
        var settings = AppSettingsStore.Load();
        var credential = WebDavCredentialStore.Load();
        HostAssets.AppendLog(
            $"WebDAV cloud push: reason={reason}, enabled={settings.EnableWebDavSync}, serverUrl={settings.WebDavServerUrl}, rootPath={settings.WebDavRootPath}, username={settings.WebDavUsername}, hasPassword={!string.IsNullOrWhiteSpace(credential?.Password)}");
        await _cloudSyncClient.UpsertUserConfigAsync(CloudWebDavConfigId, new CloudWebDavConfigSnapshot
        {
            EnableWebDavSync = settings.EnableWebDavSync,
            WebDavServerUrl = settings.WebDavServerUrl,
            WebDavRootPath = settings.WebDavRootPath,
            WebDavUsername = settings.WebDavUsername,
            WebDavPassword = credential?.Password
        });
    }

    private async Task<bool> PullQuickPanelConfigFromCloudAsync()
    {
        if (_cloudSyncClient == null)
        {
            return false;
        }

        var snapshot = await _cloudSyncClient.GetUserConfigAsync<CloudQuickPanelConfigSnapshot>(CloudQuickPanelConfigId);
        if (snapshot == null)
        {
            HostAssets.AppendLog("Quick panel cloud pull: no user config found.");
            if (ShouldSyncLocalQuickPanelConfigToCloud())
            {
                await PushQuickPanelConfigToCloudAsync("cloud-refresh-bootstrap");
            }

            return false;
        }

        var settings = AppSettingsStore.Load();
        var incoming = snapshot.ToAppSettings();
        var changed =
            !AreStringListsEqual(settings.GlobalFavoriteExtensionIds, incoming.GlobalFavoriteExtensionIds) ||
            !AreStringListsEqual(settings.ContextFavoriteExtensionIds, incoming.ContextFavoriteExtensionIds) ||
            !AreNullableStringListsEqual(settings.QuickPanelSlots, incoming.QuickPanelSlots) ||
            !string.Equals(settings.SelectedQuickPanelGlobalGroupId, incoming.SelectedQuickPanelGlobalGroupId, StringComparison.Ordinal) ||
            !string.Equals(settings.SelectedQuickPanelContextGroupId, incoming.SelectedQuickPanelContextGroupId, StringComparison.Ordinal) ||
            !AreQuickPanelGroupsEqual(settings.QuickPanelGlobalGroups, incoming.QuickPanelGlobalGroups) ||
            !AreQuickPanelGroupsEqual(settings.QuickPanelContextGroups, incoming.QuickPanelContextGroups) ||
            !AreQuickPanelMouseTriggersEqual(settings.QuickPanelMouseTriggers, incoming.QuickPanelMouseTriggers);
        if (!changed)
        {
            HostAssets.AppendLog("Quick panel cloud pull: no local changes detected.");
            return false;
        }

        settings.QuickPanelSlots = incoming.QuickPanelSlots;
        settings.QuickPanelGlobalGroups = incoming.QuickPanelGlobalGroups;
        settings.QuickPanelContextGroups = incoming.QuickPanelContextGroups;
        settings.SelectedQuickPanelGlobalGroupId = incoming.SelectedQuickPanelGlobalGroupId;
        settings.SelectedQuickPanelContextGroupId = incoming.SelectedQuickPanelContextGroupId;
        settings.GlobalFavoriteExtensionIds = incoming.GlobalFavoriteExtensionIds;
        settings.ContextFavoriteExtensionIds = incoming.ContextFavoriteExtensionIds;
        settings.QuickPanelMouseTriggers = incoming.QuickPanelMouseTriggers;
        AppSettingsStore.Save(settings);
        _appSettings = AppSettingsStore.Load();
        if (!_listenerServicesPaused)
        {
            InputHookService.ReloadSettings();
        }

        _quickPanel?.RefreshSettingsFromStore();
        HostAssets.AppendLog(
            $"Quick panel cloud pull applied: globalGroups={settings.QuickPanelGlobalGroups.Count}, contextGroups={settings.QuickPanelContextGroups.Count}, globalFavs={settings.GlobalFavoriteExtensionIds.Count}, contextFavs={settings.ContextFavoriteExtensionIds.Count}");
        return true;
    }

    private async Task PushQuickPanelConfigToCloudAsync(string reason)
    {
        if (_cloudSyncClient == null || !_cloudSyncClient.HasCredential)
        {
            HostAssets.AppendLog($"Quick panel cloud push skipped: {reason}");
            return;
        }

        await _cloudSyncClient.EnsureAuthenticatedAsync();
        var settings = AppSettingsStore.Load();
        await _cloudSyncClient.UpsertUserConfigAsync(CloudQuickPanelConfigId, CloudQuickPanelConfigSnapshot.FromSettings(settings));
    }

    private static bool ShouldSyncLocalWebDavConfigToCloud()
    {
        var settings = AppSettingsStore.Load();
        return !string.IsNullOrWhiteSpace(settings.WebDavServerUrl) &&
               !string.IsNullOrWhiteSpace(settings.WebDavRootPath) &&
               !string.IsNullOrWhiteSpace(settings.WebDavUsername);
    }

    private static bool ShouldSyncLocalQuickPanelConfigToCloud()
    {
        var settings = AppSettingsStore.Load();
        return settings.QuickPanelGlobalGroups.Any(group => group.SlotItems.Any(static slot => slot != null)) ||
               settings.QuickPanelContextGroups.Any(group => group.SlotItems.Any(static slot => slot != null)) ||
               settings.GlobalFavoriteExtensionIds.Count > 0 ||
               settings.ContextFavoriteExtensionIds.Count > 0;
    }

    private static bool AreStringListsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count &&
               !left.Where((item, index) => !string.Equals(item, right[index], StringComparison.Ordinal)).Any();
    }

    private static bool AreNullableStringListsEqual(IReadOnlyList<string?> left, IReadOnlyList<string?> right)
    {
        return left.Count == right.Count &&
               !left.Where((item, index) => !string.Equals(item, right[index], StringComparison.Ordinal)).Any();
    }

    private static bool AreQuickPanelGroupsEqual(IReadOnlyList<QuickPanelGroupSettings> left, IReadOnlyList<QuickPanelGroupSettings> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var l = left[index];
            var r = right[index];
            if (!string.Equals(l.Id, r.Id, StringComparison.Ordinal) ||
                !string.Equals(l.Name, r.Name, StringComparison.Ordinal) ||
                !string.Equals(l.ContextProcessName, r.ContextProcessName, StringComparison.Ordinal) ||
                !string.Equals(l.ContextDisplayName, r.ContextDisplayName, StringComparison.Ordinal) ||
                !AreNullableStringListsEqual(l.Slots, r.Slots) ||
                !AreQuickPanelSlotItemsEqual(l.SlotItems, r.SlotItems))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreQuickPanelSlotItemsEqual(IReadOnlyList<QuickPanelSlotItem?> left, IReadOnlyList<QuickPanelSlotItem?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var l = left[index];
            var r = right[index];
            if (l == null || r == null)
            {
                if (l != r)
                {
                    return false;
                }

                continue;
            }

            if (!string.Equals(l.ItemType, r.ItemType, StringComparison.Ordinal) ||
                !string.Equals(l.ExtensionId, r.ExtensionId, StringComparison.Ordinal) ||
                !string.Equals(l.FolderName, r.FolderName, StringComparison.Ordinal) ||
                !AreStringListsEqual(l.FolderExtensionIds, r.FolderExtensionIds))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreQuickPanelMouseTriggersEqual(QuickPanelMouseTriggerSettings left, QuickPanelMouseTriggerSettings right)
    {
        return left.MiddleButtonDown == right.MiddleButtonDown &&
               left.X1ButtonDown == right.X1ButtonDown &&
               left.X2ButtonDown == right.X2ButtonDown &&
               left.CtrlLeftClick == right.CtrlLeftClick &&
               left.CtrlRightClick == right.CtrlRightClick &&
               left.MiddleButtonLongPress == right.MiddleButtonLongPress &&
               left.RightButtonLongPress == right.RightButtonLongPress &&
               left.RightButtonDrag == right.RightButtonDrag &&
               left.HorizontalWheel == right.HorizontalWheel &&
               left.ExecuteOnButtonRelease == right.ExecuteOnButtonRelease &&
               left.LongPressMilliseconds == right.LongPressMilliseconds &&
               left.DragThresholdPixels == right.DragThresholdPixels;
    }

    public async Task<bool> PromptLoginFromSettingsAsync()
    {
        if (_cloudSyncClient == null)
        {
            SyncStatus = "云同步未配置。";
            return false;
        }

        try
        {
            var ok = ShowLoginDialog();
            if (!ok)
            {
                return false;
            }

            await _cloudSyncClient.EnsureAuthenticatedAsync();
            OnPropertyChanged(nameof(SyncSummaryText));
            SyncStatus = "已登录，可进行云同步。";
            HostAssets.AppendLog("PromptLoginFromSettingsAsync: authentication succeeded, pulling cloud configs.");
            await PullWebDavConfigFromCloudAsync();
            await PullQuickPanelConfigFromCloudAsync();
            NotifySettingsWindowWebDavConfigChanged();
            
            return true;
        }
        catch (Exception ex)
        {
            SyncStatus = $"登录失败：{FormatExceptionMessage(ex)}";
            return false;
        }
    }

    private async Task SyncWebDavConfigFromCloudAsync()
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        try
        {
            var config = await _cloudSyncClient.FetchWebDavConfigAsync();
            if (config != null)
            {
                var localSettings = AppSettingsStore.Load();
                var resolvedEnabled = localSettings.WebDavSyncManuallyDisabled
                    ? false
                    : (config.Enabled || HasWebDavConfigValues(config.ServerUrl, config.RootPath, config.Username, config.Password));
                // Apply configuration to local settings
                SaveWebDavSettings(
                    resolvedEnabled,
                    config.ServerUrl ?? string.Empty,
                    config.RootPath ?? string.Empty,
                    config.Username ?? string.Empty
                );
                
                // Save credential if provided
                if (!string.IsNullOrWhiteSpace(config.Password))
                {
                    SaveWebDavCredential(config.Username ?? string.Empty, config.Password);
                }
                
                // Notify SettingsWindow to refresh UI if open
                NotifySettingsWindowWebDavConfigChanged();
                
                System.Diagnostics.Debug.WriteLine("WebDAV configuration synced from cloud successfully.");
            }
        }
        catch (Exception ex)
        {
            // Log error but don't block login process
            System.Diagnostics.Debug.WriteLine($"Failed to sync WebDAV config from cloud: {ex.Message}");
        }
    }

    private static bool HasWebDavConfigValues(string? serverUrl, string? rootPath, string? username, string? password)
    {
        return !string.IsNullOrWhiteSpace(serverUrl) ||
               !string.IsNullOrWhiteSpace(rootPath) ||
               !string.IsNullOrWhiteSpace(username) ||
               !string.IsNullOrWhiteSpace(password);
    }

    private void NotifySettingsWindowWebDavConfigChanged()
    {
        // If SettingsWindow is open, refresh its WebDAV UI
        var settingsWindow = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        settingsWindow?.RefreshWebDavConfigFromExternal();
    }

    public async Task RefreshCloudFromSettingsAsync()
    {
        await RefreshCloudStateAsync();
    }

    public void SignOutFromSettings()
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        HostAssets.AppendLog(
            $"SignOutFromSettings: before clear sessionExists={File.Exists(SyncSessionStore.SessionPath)}, credentialExists={File.Exists(SecureCredentialStore.CredentialPath)}");
        _cloudSyncClient.ClearCredential();
        SyncStatus = "已退出登录。";
        OnPropertyChanged(nameof(SyncSummaryText));
        NotifySettingsWindowAccountChanged();
        HostAssets.AppendLog(
            $"SignOutFromSettings: after clear sessionExists={File.Exists(SyncSessionStore.SessionPath)}, credentialExists={File.Exists(SecureCredentialStore.CredentialPath)}");
    }

    private void NotifySettingsWindowAccountChanged()
    {
        var settingsWindow = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        settingsWindow?.RefreshAccountFromExternal();
    }

    public void RefreshAppSettings()
    {
        var settings = AppSettingsStore.Load();
        _appSettings = settings;
        if (!_listenerServicesPaused)
        {
            InputHookService.ReloadSettings();
            RefreshLauncherHotkeyRegistration();
            RefreshExtensionHotkeys();
        }
        SyncStatus = settings.LaunchAtStartup
            ? "设置已保存。开机启动已启用。"
            : settings.RefreshCloudOnStartup
                ? "设置已保存。"
                : "设置已保存。启动后自动刷新云状态已关闭。";
    }

    public string GetLauncherHotkey() => AppSettingsStore.Load().LauncherHotkey;

    public AppSettings GetCurrentAppSettings() => AppSettingsStore.Load();

    public void SaveWebDavSettings(bool enabled, string serverUrl, string rootPath, string username)
    {
        var settings = AppSettingsStore.Load();
        settings.EnableWebDavSync = enabled;
        settings.WebDavSyncManuallyDisabled = !enabled && HasWebDavConfigValues(serverUrl, rootPath, username, null);
        settings.WebDavServerUrl = serverUrl.Trim();
        settings.WebDavRootPath = string.IsNullOrWhiteSpace(rootPath) ? "/yanzi" : rootPath.Trim();
        settings.WebDavUsername = username.Trim();
        AppSettingsStore.Save(settings);
        _appSettings = settings;
        if (enabled)
        {
            StartBackgroundWebDavSync();
        }
        else
        {
            _backgroundWebDavSyncTimer.Stop();
        }

        QueueCloudWebDavConfigSync("settings-saved");
    }

    public void SaveWebDavCredential(string username, string password)
    {
        WebDavCredentialStore.Save(new SavedWebDavCredential
        {
            Username = username.Trim(),
            Password = password
        });

        var settings = AppSettingsStore.Load();
        settings.WebDavUsername = username.Trim();
        AppSettingsStore.Save(settings);
        _appSettings = settings;
        StartBackgroundWebDavSync();
        QueueBackgroundWebDavSync("credential-saved");
        QueueCloudWebDavConfigSync("credential-saved");
    }

    public void NotifyQuickPanelSettingsChanged(string reason)
    {
        _appSettings = AppSettingsStore.Load();
        if (!_listenerServicesPaused)
        {
            InputHookService.ReloadSettings();
        }

        QueueCloudQuickPanelConfigSync(reason);
    }

    public bool HasWebDavCredential()
    {
        var credential = WebDavCredentialStore.Load();
        return !string.IsNullOrWhiteSpace(credential?.Username) &&
               !string.IsNullOrWhiteSpace(credential?.Password);
    }

    public async Task<(bool ok, string message)> ProbeWebDavAsync()
    {
        try
        {
            var service = new WebDavSyncService(AppSettingsStore.Load());
            await service.ProbeAsync();
            return (true, $"WebDAV 连接正常：{service.SyncRootDisplay}");
        }
        catch (Exception ex)
        {
            return (false, $"WebDAV 测试失败：{FormatExceptionMessage(ex)}");
        }
    }

    public async Task<(bool ok, string message)> SyncWebDavNowAsync()
    {
        try
        {
            var service = new WebDavSyncService(AppSettingsStore.Load());
            var result = await service.SyncExtensionsAsync();
            ReloadLocalExtensionsFromWebDav();
            return (true, $"个人扩展同步完成：上传 {result.UploadedCount} 个，拉取 {result.PulledCount} 个。");
        }
        catch (Exception ex)
        {
            return (false, $"个人扩展同步失败：{FormatExceptionMessage(ex)}");
        }
    }

    public bool TryUpdateLauncherHotkey(string shortcut, out string message)
    {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(shortcut) || !TryParseHotkey(shortcut, out _, out _))
        {
            message = "快捷键格式无效。示例：Alt+Space 或 DoubleCtrl";
            return false;
        }

        var settings = AppSettingsStore.Load();
        var previous = settings.LauncherHotkey;
        settings.LauncherHotkey = shortcut.Trim();
        AppSettingsStore.Save(settings);

        if (!RefreshLauncherHotkeyRegistration())
        {
            settings.LauncherHotkey = previous;
            AppSettingsStore.Save(settings);
            RefreshLauncherHotkeyRegistration();
            message = "主程序快捷键注册失败，可能与系统或其他程序冲突。";
            return false;
        }

        message = $"主程序快捷键已更新为 {settings.LauncherHotkey}";
        return true;
    }

}
