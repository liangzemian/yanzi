using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Forms;
using System.Windows.Input;

namespace OpenQuickHost;

public partial class MobileMessageToastWindow : Window
{
    private static readonly Regex UrlRegex = new(
        @"https?://[^\s<>""]+|www\.[^\s<>""]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly StringBuilder _conversationText = new();
    private string? _lastUrl;

    public MobileMessageToastWindow(string title, string messageText, string sourceDeviceId, DateTimeOffset receivedAt, string? screenshotDataUrl = null, string? screenshotFilePath = null)
    {
        InitializeComponent();
        TitleText.Text = string.IsNullOrWhiteSpace(title) ? "手机发来消息" : title.Trim();
        AppendMessage(title, messageText, sourceDeviceId, receivedAt, screenshotDataUrl, screenshotFilePath);

        Loaded += (_, _) => PositionBottomRight();
    }

    public void AppendMessage(string title, string messageText, string sourceDeviceId, DateTimeOffset receivedAt, string? screenshotDataUrl = null, string? screenshotFilePath = null)
    {
        _lastUrl = ExtractUrl(messageText) ?? _lastUrl;
        if (_conversationText.Length > 0)
        {
            _conversationText.AppendLine();
        }

        _conversationText.Append('[').Append(receivedAt.ToString("HH:mm:ss")).Append("] ")
            .Append(sourceDeviceId).Append(": ").Append(messageText);

        TitleText.Text = string.IsNullOrWhiteSpace(title) ? "手机发来消息" : title.Trim();
        MetaText.Text = $"最近来自 {sourceDeviceId} · {receivedAt:HH:mm:ss}";
        AddMessageBubble(messageText, sourceDeviceId, receivedAt, screenshotDataUrl, screenshotFilePath);
        UpdateUrlActions();
        Dispatcher.InvokeAsync(() => MessageScrollViewer.ScrollToEnd());
    }

    private void AddMessageBubble(string messageText, string sourceDeviceId, DateTimeOffset receivedAt, string? screenshotDataUrl, string? screenshotFilePath)
    {
        var container = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(190, 17, 24, 39)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(36, 56, 189, 248)),
            BorderThickness = new Thickness(1)
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"{sourceDeviceId} · {receivedAt:HH:mm:ss}",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)),
            FontSize = 11
        });
        panel.Children.Add(new System.Windows.Controls.TextBox
        {
            Text = messageText,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 231, 235)),
            FontSize = 14,
            Padding = new Thickness(0)
        });

        var screenshot = TryCreateScreenshotImage(screenshotDataUrl, screenshotFilePath, receivedAt);
        if (screenshot.Image != null)
        {
            panel.Children.Add(screenshot.Image);
        }
        else if (!string.IsNullOrWhiteSpace(screenshotDataUrl) || !string.IsNullOrWhiteSpace(screenshotFilePath))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "截图预览加载失败，详情请查看 host.log。",
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }
        if (!string.IsNullOrWhiteSpace(screenshot.FilePath))
        {
            var pathBox = new System.Windows.Controls.TextBox
            {
                Text = $"已保存：{screenshot.FilePath}",
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(103, 232, 249)),
                FontSize = 11,
                Padding = new Thickness(0)
            };
            panel.Children.Add(pathBox);
        }

        container.Child = panel;
        MessageStack.Children.Add(container);
    }

    private static (System.Windows.Controls.Image? Image, string? FilePath) TryCreateScreenshotImage(string? dataUrl, string? existingFilePath, DateTimeOffset receivedAt)
    {
        try
        {
            byte[] bytes;
            string filePath;
            if (!string.IsNullOrWhiteSpace(existingFilePath) && File.Exists(existingFilePath))
            {
                bytes = File.ReadAllBytes(existingFilePath);
                filePath = existingFilePath;
            }
            else if (!string.IsNullOrWhiteSpace(dataUrl))
            {
                const string marker = "base64,";
                var index = dataUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    return (null, null);
                }

                bytes = Convert.FromBase64String(dataUrl[(index + marker.Length)..]);
                filePath = SaveScreenshotToDownloads(bytes, receivedAt);
            }
            else
            {
                return (null, null);
            }
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.DecodePixelWidth = 300;
            bitmap.EndInit();
            bitmap.Freeze();

            var image = new System.Windows.Controls.Image
            {
                Source = bitmap,
                Margin = new Thickness(0, 10, 0, 0),
                MaxWidth = 300,
                MaxHeight = 180,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left
            };
            image.ContextMenu = BuildScreenshotContextMenu(bitmap, bytes, filePath);
            image.MouseRightButtonUp += (_, e) =>
            {
                image.ContextMenu.IsOpen = true;
                e.Handled = true;
            };
            HostAssets.AppendLog($"Mobile screenshot preview loaded: local={filePath}, bytes={bytes.Length}.");
            return (image, filePath);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile screenshot preview failed: {ex.GetType().Name}: {ex.Message}");
            return (null, null);
        }
    }

    private static string SaveScreenshotToDownloads(byte[] bytes, DateTimeOffset receivedAt)
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        Directory.CreateDirectory(downloads);
        var path = Path.Combine(downloads, $"yanzi-mobile-screenshot-{receivedAt:yyyyMMdd-HHmmss-fff}.jpg");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static System.Windows.Controls.ContextMenu BuildScreenshotContextMenu(BitmapSource bitmap, byte[] bytes, string filePath)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var copy = new System.Windows.Controls.MenuItem { Header = "复制图片" };
        copy.Click += (_, _) => System.Windows.Clipboard.SetImage(bitmap);
        var copyPath = new System.Windows.Controls.MenuItem { Header = "复制文件路径" };
        copyPath.Click += (_, _) => ClipboardService.SetText(filePath);
        var open = new System.Windows.Controls.MenuItem { Header = "打开图片" };
        open.Click += (_, _) => Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        var saveAs = new System.Windows.Controls.MenuItem { Header = "另存为..." };
        saveAs.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "JPEG 图片 (*.jpg)|*.jpg|所有文件 (*.*)|*.*",
                FileName = Path.GetFileName(filePath),
                InitialDirectory = Path.GetDirectoryName(filePath)
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                File.WriteAllBytes(dialog.FileName, bytes);
            }
        };
        menu.Items.Add(copy);
        menu.Items.Add(copyPath);
        menu.Items.Add(open);
        menu.Items.Add(saveAs);
        return menu;
    }

    private void UpdateUrlActions()
    {
        if (!string.IsNullOrWhiteSpace(_lastUrl))
        {
            OpenLinkButton.Visibility = Visibility.Visible;
            UrlHintText.Visibility = Visibility.Visible;
            UrlHintText.Text = $"最近链接：{_lastUrl}";
            return;
        }

        OpenLinkButton.Visibility = Visibility.Collapsed;
        UrlHintText.Visibility = Visibility.Collapsed;
    }

    private void PositionBottomRight()
    {
        var area = Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        Left = area.Right / GetDpiScaleX() - ActualWidth - 18;
        Top = area.Bottom / GetDpiScaleY() - ActualHeight - 18;
    }

    private double GetDpiScaleX()
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
    }

    private double GetDpiScaleY()
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice.M22 ?? 1;
    }

    private void OpenLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastUrl))
        {
            return;
        }

        var url = _lastUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? _lastUrl : $"https://{_lastUrl}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        ClipboardService.SetText(_conversationText.ToString());
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string? ExtractUrl(string text)
    {
        var match = UrlRegex.Match(text ?? string.Empty);
        return match.Success ? match.Value.TrimEnd('.', ',', ';', ')', ']', '}') : null;
    }
}
