using System.Linq;
using System.Windows;

namespace QueryBuilderPlugin;

public partial class PromptWindow : Window
{
    public string? Result { get; private set; }

    public PromptWindow(string message, string defaultValue)
    {
        InitializeComponent();
        MessageText.Text = message;
        InputBox.Text = defaultValue;
        InputBox.SelectAll();
        Loaded += (_, _) => InputBox.Focus();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        Result = string.IsNullOrWhiteSpace(InputBox.Text) ? null : InputBox.Text.Trim();
        DialogResult = Result is not null;
    }

    public static string? Show(string message, string defaultValue)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        var dialog = new PromptWindow(message, defaultValue)
        {
            Owner = owner
        };
        dialog.ShowDialog();
        return dialog.Result;
    }
}
