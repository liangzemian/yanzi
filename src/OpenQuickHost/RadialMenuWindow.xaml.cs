using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace OpenQuickHost;

public partial class RadialMenuWindow : Window, INotifyPropertyChanged
{
    private readonly MainWindow _mainWindow;
    private readonly DispatcherTimer _selectionTimer;
    private System.Drawing.Point _centerPixels;
    private RadialMenuItemViewModel? _selectedItem;
    private RadialMenuItemViewModel? _selectedChildItem;
    private RadialMenuItemViewModel? _selectedGrandChildItem;
    private List<RadialMenuPageSettings> _pages = [];
    private string _currentPageId = string.Empty;
    private readonly Stack<string> _pageStack = new();
    private bool _isExecuting;
    private string _activeTitle = "取消";
    private string _pageTitle = "燕环";
    private bool _hasChildRing;
    private string _childRingTitle = string.Empty;
    private string _grandChildRingTitle = string.Empty;
    private double _childRingCenterX;
    private double _childRingCenterY;
    private double _grandChildRingCenterX;
    private double _grandChildRingCenterY;
    private bool _hasGrandChildRing;

    public RadialMenuWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _selectionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _selectionTimer.Tick += (_, _) => UpdateSelectionFromCursor();
        DataContext = this;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
            }
        };
        MouseWheel += RadialMenuWindow_MouseWheel;
        MouseLeftButtonDown += (_, _) => ReturnToParentPage();
    }

    public ObservableCollection<RadialMenuItemViewModel> Items { get; } = [];

    public ObservableCollection<RadialMenuItemViewModel> ChildItems { get; } = [];

    public ObservableCollection<RadialMenuItemViewModel> GrandChildItems { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> MainSeparators { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> ChildSeparators { get; } = [];

    public ObservableCollection<RadialSeparatorViewModel> GrandChildSeparators { get; } = [];

    public bool HasChildRing
    {
        get => _hasChildRing;
        private set
        {
            if (value == _hasChildRing)
            {
                return;
            }

            _hasChildRing = value;
            OnPropertyChanged();
        }
    }

    public string ChildRingTitle
    {
        get => _childRingTitle;
        private set
        {
            if (value == _childRingTitle)
            {
                return;
            }

            _childRingTitle = value;
            OnPropertyChanged();
        }
    }

    public bool HasGrandChildRing
    {
        get => _hasGrandChildRing;
        private set
        {
            if (value == _hasGrandChildRing)
            {
                return;
            }

            _hasGrandChildRing = value;
            OnPropertyChanged();
        }
    }

    public string GrandChildRingTitle
    {
        get => _grandChildRingTitle;
        private set
        {
            if (value == _grandChildRingTitle)
            {
                return;
            }

            _grandChildRingTitle = value;
            OnPropertyChanged();
        }
    }

    public double ChildRingEllipseX => _childRingCenterX - 120;

    public double ChildRingEllipseY => _childRingCenterY - 120;

    public double ChildRingCenterEllipseX => _childRingCenterX - 32;

    public double ChildRingCenterEllipseY => _childRingCenterY - 32;

    public double ChildRingTitleX => _childRingCenterX - 75;

    public double ChildRingTitleY => _childRingCenterY - 10;

    public double GrandChildRingEllipseX => _grandChildRingCenterX - 110;

    public double GrandChildRingEllipseY => _grandChildRingCenterY - 110;

    public double GrandChildRingCenterEllipseX => _grandChildRingCenterX - 29;

    public double GrandChildRingCenterEllipseY => _grandChildRingCenterY - 29;

    public double GrandChildRingTitleX => _grandChildRingCenterX - 70;

    public double GrandChildRingTitleY => _grandChildRingCenterY - 10;

    public string ActiveTitle
    {
        get => _activeTitle;
        private set
        {
            if (value == _activeTitle)
            {
                return;
            }

            _activeTitle = value;
            OnPropertyChanged();
        }
    }

    public string PageTitle
    {
        get => _pageTitle;
        private set
        {
            if (value == _pageTitle)
            {
                return;
            }

            _pageTitle = value;
            OnPropertyChanged();
        }
    }

    public void ShowAtMouse()
    {
        _isExecuting = false;
        var settings = AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings();
        _pages = _mainWindow.GetRadialMenuPages().ToList();
        _currentPageId = string.IsNullOrWhiteSpace(settings.SelectedPageId)
            ? _pages.FirstOrDefault()?.Id ?? string.Empty
            : settings.SelectedPageId;
        _pageStack.Clear();
        BuildItems(settings.RadiusPixels);

        _centerPixels = Forms.Cursor.Position;
        var source = PresentationSource.FromVisual(_mainWindow);
        var centerDips = source?.CompositionTarget?.TransformFromDevice.Transform(
            new System.Windows.Point(_centerPixels.X, _centerPixels.Y)) ?? new System.Windows.Point(_centerPixels.X, _centerPixels.Y);

        Left = centerDips.X - Width / 2;
        Top = centerDips.Y - Height / 2;
        ActiveTitle = "取消";
        if (!IsVisible)
        {
            Show();
        }

        Activate();
        _selectionTimer.Start();
        HostAssets.AppendLog($"Radial menu shown: page={_currentPageId}, items={Items.Count}, center=({_centerPixels.X},{_centerPixels.Y}).");
    }

    public void ExecuteSelectedFromHoldRelease()
    {
        if (_isExecuting)
        {
            return;
        }

        _selectionTimer.Stop();
        var selected = _selectedItem;
        var selectedChild = _selectedChildItem;
        var selectedGrandChild = _selectedGrandChildItem;
        Hide();
        if (selectedGrandChild?.Command != null)
        {
            _isExecuting = true;
            HostAssets.AppendLog($"Radial menu executing grandchild: index={selectedGrandChild.Index}, command={selectedGrandChild.Command.Title}.");
            _mainWindow.ExecuteCommandExternally(selectedGrandChild.Command, string.Empty, "radial-menu-grandchild");
            return;
        }

        if (selectedChild?.Command != null)
        {
            _isExecuting = true;
            HostAssets.AppendLog($"Radial menu executing child: index={selectedChild.Index}, command={selectedChild.Command.Title}.");
            _mainWindow.ExecuteCommandExternally(selectedChild.Command, string.Empty, "radial-menu-child");
            return;
        }

        if (selected == null)
        {
            HostAssets.AppendLog("Radial menu release: no selected command.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(selected.ChildPageId))
        {
            HostAssets.AppendLog($"Radial menu release: parent child slot selected without child command, childPage={selected.ChildPageId}.");
            return;
        }

        if (selected.Command == null)
        {
            HostAssets.AppendLog("Radial menu release: selected empty slot.");
            return;
        }

        _isExecuting = true;
        HostAssets.AppendLog($"Radial menu executing: index={selected.Index}, command={selected.Command.Title}.");
        _mainWindow.ExecuteCommandExternally(selected.Command, string.Empty, "radial-menu");
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        _selectionTimer.Stop();
        Hide();
    }

    private void BuildItems(int radius)
    {
        var effectiveRadius = Math.Clamp(radius - 10, 82, 96);
        Items.Clear();
        ChildItems.Clear();
        GrandChildItems.Clear();
        ClearChildSelection();
        ClearGrandChildSelection();
        HasChildRing = false;
        HasGrandChildRing = false;
        SetSelectedItem(null);
        var items = _mainWindow.GetRadialMenuItems(_currentPageId);
        var page = _pages.FirstOrDefault(item => item.Id.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase));
        PageTitle = page?.Name ?? "燕环";
        var center = new System.Windows.Point(Width / 2, Height / 2);
        BuildSeparators(MainSeparators, center.X, center.Y, 34, 135);
        for (var index = 0; index < 8; index++)
        {
            var angle = (-90 + index * 45) * Math.PI / 180.0;
            var x = center.X + Math.Cos(angle) * effectiveRadius - 42;
            var y = center.Y + Math.Sin(angle) * effectiveRadius - 33;
            var item = items.ElementAtOrDefault(index);
            var command = item?.Command;
            var childPageId = item?.ChildPageId ?? string.Empty;
            Items.Add(new RadialMenuItemViewModel(index, command, childPageId, ResolvePageName(childPageId), x, y));
        }
    }

    private void UpdateSelectionFromCursor()
    {
        var cursorPoint = GetCursorWindowPoint();
        if (HasGrandChildRing && TryUpdateGrandChildSelection(cursorPoint))
        {
            return;
        }

        if (HasChildRing && TryUpdateChildSelection(cursorPoint))
        {
            return;
        }

        var center = new System.Windows.Point(Width / 2, Height / 2);
        var dx = cursorPoint.X - center.X;
        var dy = cursorPoint.Y - center.Y;
        var settings = AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings();
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < settings.DeadZonePixels)
        {
            SetSelectedItem(null);
            ClearChildRing();
            ActiveTitle = "取消";
            return;
        }

        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        var index = ((int)Math.Round((angle + 90) / 45.0) % 8 + 8) % 8;
        var item = Items.ElementAtOrDefault(index);
        SetSelectedItem(item);
        ActiveTitle = item?.Command?.Title ?? "空槽位";
        if (!string.IsNullOrWhiteSpace(item?.ChildPageId))
        {
            ActiveTitle = $"展开：{item.ChildPageTitle}";
            BuildChildRing(item);
        }
        else
        {
            ClearChildRing();
        }
    }

    private System.Windows.Point GetCursorWindowPoint()
    {
        var cursor = Forms.Cursor.Position;
        var source = PresentationSource.FromVisual(this);
        var screenDips = source?.CompositionTarget?.TransformFromDevice.Transform(
            new System.Windows.Point(cursor.X, cursor.Y)) ?? new System.Windows.Point(cursor.X, cursor.Y);
        return new System.Windows.Point(screenDips.X - Left, screenDips.Y - Top);
    }

    private bool TryUpdateChildSelection(System.Windows.Point cursorPoint)
    {
        var dx = cursorPoint.X - _childRingCenterX;
        var dy = cursorPoint.Y - _childRingCenterY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > 150)
        {
            ClearChildSelection();
            ClearGrandChildRing();
            return false;
        }

        if (distance < 26)
        {
            ClearChildSelection();
            ActiveTitle = $"{ChildRingTitle} · 中心取消";
            return true;
        }

        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        var index = ((int)Math.Round((angle + 90) / 45.0) % 8 + 8) % 8;
        var item = ChildItems.ElementAtOrDefault(index);
        SetSelectedChildItem(item);
        ActiveTitle = item?.Command?.Title ?? "子环空槽位";
        if (!string.IsNullOrWhiteSpace(item?.ChildPageId))
        {
            ActiveTitle = $"展开：{item.ChildPageTitle}";
            BuildGrandChildRing(item);
        }
        else
        {
            ClearGrandChildRing();
        }
        return true;
    }

    private bool TryUpdateGrandChildSelection(System.Windows.Point cursorPoint)
    {
        var dx = cursorPoint.X - _grandChildRingCenterX;
        var dy = cursorPoint.Y - _grandChildRingCenterY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > 138)
        {
            ClearGrandChildSelection();
            return false;
        }

        if (distance < 24)
        {
            ClearGrandChildSelection();
            ActiveTitle = $"{GrandChildRingTitle} · 中心取消";
            return true;
        }

        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        var index = ((int)Math.Round((angle + 90) / 45.0) % 8 + 8) % 8;
        var item = GrandChildItems.ElementAtOrDefault(index);
        SetSelectedGrandChildItem(item);
        ActiveTitle = item?.Command?.Title ?? "二级子环空槽位";
        return true;
    }

    private void RadialMenuWindow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_pages.Count <= 1)
        {
            return;
        }

        var currentIndex = Math.Max(0, _pages.FindIndex(page => page.Id.Equals(_currentPageId, StringComparison.OrdinalIgnoreCase)));
        var delta = e.Delta < 0 ? 1 : -1;
        var nextIndex = (currentIndex + delta + _pages.Count) % _pages.Count;
        _currentPageId = _pages[nextIndex].Id;
        _pageStack.Clear();
        BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
        e.Handled = true;
    }

    private void EnterChildPage(string childPageId)
    {
        if (_pages.All(page => !page.Id.Equals(childPageId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _pageStack.Push(_currentPageId);
        _currentPageId = childPageId;
        BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
        HostAssets.AppendLog($"Radial menu entered child page: {childPageId}.");
    }

    private void ReturnToParentPage()
    {
        if (_pageStack.Count == 0)
        {
            return;
        }

        _currentPageId = _pageStack.Pop();
        BuildItems((AppSettingsStore.Load().RadialMenu ?? new RadialMenuSettings()).RadiusPixels);
    }

    private string ResolvePageName(string? pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            return string.Empty;
        }

        return _pages.FirstOrDefault(page => page.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase))?.Name ?? pageId;
    }

    private void SetSelectedItem(RadialMenuItemViewModel? item)
    {
        if (ReferenceEquals(_selectedItem, item))
        {
            return;
        }

        if (_selectedItem != null)
        {
            _selectedItem.IsSelected = false;
        }

        _selectedItem = item;
        if (_selectedItem != null)
        {
            _selectedItem.IsSelected = true;
        }
    }

    private void BuildChildRing(RadialMenuItemViewModel parent)
    {
        if (string.IsNullOrWhiteSpace(parent.ChildPageId))
        {
            ClearChildRing();
            return;
        }

        var items = _mainWindow.GetRadialMenuItems(parent.ChildPageId);
        var angle = (-90 + parent.Index * 45) * Math.PI / 180.0;
        var center = new System.Windows.Point(Width / 2, Height / 2);
        _childRingCenterX = center.X + Math.Cos(angle) * 250;
        _childRingCenterY = center.Y + Math.Sin(angle) * 250;
        ClampRingCenter(ref _childRingCenterX, ref _childRingCenterY, 134);
        ChildRingTitle = parent.ChildPageTitle;
        OnPropertyChanged(nameof(ChildRingEllipseX));
        OnPropertyChanged(nameof(ChildRingEllipseY));
        OnPropertyChanged(nameof(ChildRingCenterEllipseX));
        OnPropertyChanged(nameof(ChildRingCenterEllipseY));
        OnPropertyChanged(nameof(ChildRingTitleX));
        OnPropertyChanged(nameof(ChildRingTitleY));
        BuildSeparators(ChildSeparators, _childRingCenterX, _childRingCenterY, 32, 120);

        ChildItems.Clear();
        const double radius = 78;
        for (var index = 0; index < 8; index++)
        {
            var childAngle = (-90 + index * 45) * Math.PI / 180.0;
            var x = _childRingCenterX + Math.Cos(childAngle) * radius - 38;
            var y = _childRingCenterY + Math.Sin(childAngle) * radius - 30;
            var item = items.ElementAtOrDefault(index);
            var command = item?.Command;
            var childPageId = item?.ChildPageId ?? string.Empty;
            ChildItems.Add(new RadialMenuItemViewModel(index, command, childPageId, ResolvePageName(childPageId), x, y));
        }

        HasChildRing = true;
    }

    private void BuildGrandChildRing(RadialMenuItemViewModel parent)
    {
        if (string.IsNullOrWhiteSpace(parent.ChildPageId))
        {
            ClearGrandChildRing();
            return;
        }

        var items = _mainWindow.GetRadialMenuItems(parent.ChildPageId);
        var angle = (-90 + parent.Index * 45) * Math.PI / 180.0;
        _grandChildRingCenterX = _childRingCenterX + Math.Cos(angle) * 220;
        _grandChildRingCenterY = _childRingCenterY + Math.Sin(angle) * 220;
        ClampRingCenter(ref _grandChildRingCenterX, ref _grandChildRingCenterY, 124);
        GrandChildRingTitle = parent.ChildPageTitle;
        OnPropertyChanged(nameof(GrandChildRingEllipseX));
        OnPropertyChanged(nameof(GrandChildRingEllipseY));
        OnPropertyChanged(nameof(GrandChildRingCenterEllipseX));
        OnPropertyChanged(nameof(GrandChildRingCenterEllipseY));
        OnPropertyChanged(nameof(GrandChildRingTitleX));
        OnPropertyChanged(nameof(GrandChildRingTitleY));
        BuildSeparators(GrandChildSeparators, _grandChildRingCenterX, _grandChildRingCenterY, 29, 110);

        GrandChildItems.Clear();
        const double radius = 72;
        for (var index = 0; index < 8; index++)
        {
            var childAngle = (-90 + index * 45) * Math.PI / 180.0;
            var x = _grandChildRingCenterX + Math.Cos(childAngle) * radius - 36;
            var y = _grandChildRingCenterY + Math.Sin(childAngle) * radius - 28;
            var item = items.ElementAtOrDefault(index);
            var command = item?.Command;
            var childPageId = item?.ChildPageId ?? string.Empty;
            GrandChildItems.Add(new RadialMenuItemViewModel(index, command, childPageId, ResolvePageName(childPageId), x, y));
        }

        HasGrandChildRing = true;
    }

    private void ClearChildRing()
    {
        ClearChildSelection();
        ChildItems.Clear();
        ChildSeparators.Clear();
        ClearGrandChildRing();
        HasChildRing = false;
        ChildRingTitle = string.Empty;
    }

    private void ClearGrandChildRing()
    {
        ClearGrandChildSelection();
        GrandChildItems.Clear();
        GrandChildSeparators.Clear();
        HasGrandChildRing = false;
        GrandChildRingTitle = string.Empty;
    }

    private void SetSelectedChildItem(RadialMenuItemViewModel? item)
    {
        if (ReferenceEquals(_selectedChildItem, item))
        {
            return;
        }

        ClearChildSelection();
        _selectedChildItem = item;
        if (_selectedChildItem != null)
        {
            _selectedChildItem.IsSelected = true;
        }
    }

    private void ClearChildSelection()
    {
        if (_selectedChildItem != null)
        {
            _selectedChildItem.IsSelected = false;
            _selectedChildItem = null;
        }
    }

    private void SetSelectedGrandChildItem(RadialMenuItemViewModel? item)
    {
        if (ReferenceEquals(_selectedGrandChildItem, item))
        {
            return;
        }

        ClearGrandChildSelection();
        _selectedGrandChildItem = item;
        if (_selectedGrandChildItem != null)
        {
            _selectedGrandChildItem.IsSelected = true;
        }
    }

    private void ClearGrandChildSelection()
    {
        if (_selectedGrandChildItem != null)
        {
            _selectedGrandChildItem.IsSelected = false;
            _selectedGrandChildItem = null;
        }
    }

    private static void BuildSeparators(ObservableCollection<RadialSeparatorViewModel> target, double centerX, double centerY, double innerRadius, double outerRadius)
    {
        target.Clear();
        for (var index = 0; index < 8; index++)
        {
            var angle = (-112.5 + index * 45) * Math.PI / 180.0;
            target.Add(new RadialSeparatorViewModel(
                centerX + Math.Cos(angle) * innerRadius,
                centerY + Math.Sin(angle) * innerRadius,
                centerX + Math.Cos(angle) * outerRadius,
                centerY + Math.Sin(angle) * outerRadius));
        }
    }

    private void ClampRingCenter(ref double x, ref double y, double radius)
    {
        x = Math.Clamp(x, radius + 8, Width - radius - 8);
        y = Math.Clamp(y, radius + 8, Height - radius - 8);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class RadialMenuItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public RadialMenuItemViewModel(int index, CommandItem? command, string childPageId, string childPageTitle, double x, double y)
    {
        Index = index;
        Command = command;
        ChildPageId = childPageId;
        ChildPageTitle = childPageTitle;
        X = x;
        Y = y;
    }

    public int Index { get; }

    public CommandItem? Command { get; }

    public string ChildPageId { get; }

    public string ChildPageTitle { get; }

    public double X { get; }

    public double Y { get; }

    public string Title => !string.IsNullOrWhiteSpace(ChildPageId) ? ChildPageTitle : Command?.Title ?? "空";

    public ImageSource? IconSource => Command?.IconSource;

    public Geometry? VectorIcon => Command?.VectorIcon;

    public bool HasImageIcon => Command?.HasImageIcon == true;

    public bool HasVectorIcon => Command?.HasVectorIcon == true;

    public bool UseGlyphIcon => Command == null || Command.UseGlyphIcon;

    public string DisplayGlyph => !string.IsNullOrWhiteSpace(ChildPageId) ? "›" : Command?.DisplayGlyph ?? "+";

    public System.Windows.Media.Brush AccentBrush => Command?.AccentBrush ?? System.Windows.Media.Brushes.Transparent;

    public double Scale => IsSelected ? 1.12 : 1.0;

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
            OnPropertyChanged(nameof(Scale));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record RadialSeparatorViewModel(double X1, double Y1, double X2, double Y2);

public sealed record RadialMenuRuntimeItem(CommandItem? Command, string ChildPageId);
