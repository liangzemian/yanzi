using System.Windows;
using System.Windows.Input;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OpenQuickHost;

public partial class YanmComponentEditorWindow : Window
{
    public YanmComponentEditorWindow(string title, string name, string html)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        NameBox.Text = name;
        HtmlBox.Text = html;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
        PreviewKeyDown += Window_PreviewKeyDown;
    }

    public string ComponentName => NameBox.Text.Trim();

    public string ComponentHtml => HtmlBox.Text;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ComponentName))
        {
            ErrorText.Foreground = System.Windows.Media.Brushes.IndianRed;
            ErrorText.Text = "组件名称不能为空。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrWhiteSpace(ComponentHtml))
        {
            ErrorText.Foreground = System.Windows.Media.Brushes.IndianRed;
            ErrorText.Text = "HTML 内容不能为空。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void CopyPromptButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ClipboardService.SetText(YanmComponentSettings.BuildAiPrompt());
            ErrorText.Text = "已复制燕幕组件提示词。";
            ErrorText.Foreground = System.Windows.Media.Brushes.LightGreen;
            ErrorText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ErrorText.Foreground = System.Windows.Media.Brushes.IndianRed;
            ErrorText.Text = $"复制提示词失败：{ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }

        if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SaveButton_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }
}
