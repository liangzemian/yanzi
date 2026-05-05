using System.Collections.ObjectModel;
using System.Windows;

namespace OpenQuickHost;

public partial class RunningExtensionsWindow : Window
{
    private readonly ObservableCollection<RunningExtensionItemViewModel> _items = [];

    public RunningExtensionsWindow()
    {
        InitializeComponent();
        ExtensionListView.ItemsSource = _items;
        Loaded += RunningExtensionsWindow_Loaded;
        Closed += RunningExtensionsWindow_Closed;
    }

    private void RunningExtensionsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RunningExtensionRegistry.Changed += RunningExtensionRegistry_Changed;
        RefreshItems();
    }

    private void RunningExtensionsWindow_Closed(object? sender, EventArgs e)
    {
        RunningExtensionRegistry.Changed -= RunningExtensionRegistry_Changed;
    }

    private void RunningExtensionRegistry_Changed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(RefreshItems);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshItems();
    }

    private void TerminateButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Guid instanceId })
        {
            return;
        }

        var target = _items.FirstOrDefault(item => item.InstanceId == instanceId);
        if (target == null)
        {
            RefreshItems();
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            this,
            $"确定要强制结束扩展“{target.Title}”吗？",
            "结束扩展",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var success = RunningExtensionRegistry.TryTerminate(instanceId, out var message);
        System.Windows.MessageBox.Show(
            this,
            message,
            success ? "操作完成" : "操作失败",
            MessageBoxButton.OK,
            success ? MessageBoxImage.Information : MessageBoxImage.Error);
        RefreshItems();
    }

    private void RefreshItems()
    {
        var snapshot = RunningExtensionRegistry.GetSnapshot();
        _items.Clear();
        foreach (var item in snapshot)
        {
            _items.Add(new RunningExtensionItemViewModel(item));
        }

        SummaryTextBlock.Text = $"当前共 {_items.Count} 个正在运行的独立窗口扩展";
        EmptyStateTextBlock.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed class RunningExtensionItemViewModel
    {
        public RunningExtensionItemViewModel(RunningExtensionInfo info)
        {
            InstanceId = info.InstanceId;
            Title = info.Title;
            ExtensionId = info.ExtensionId;
            ProcessId = info.ProcessId.ToString();
            Runtime = info.Runtime;
            LaunchSource = info.LaunchSource;
            StartedAtText = info.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        public Guid InstanceId { get; }

        public string Title { get; }

        public string ExtensionId { get; }

        public string ProcessId { get; }

        public string Runtime { get; }

        public string LaunchSource { get; }

        public string StartedAtText { get; }
    }
}
