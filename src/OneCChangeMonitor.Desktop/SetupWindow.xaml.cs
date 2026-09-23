using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace OneCChangeMonitor.Desktop;

public partial class SetupWindow : Window
{
    public ProjectSettings Project { get; private set; } = new();

    public SetupWindow()
    {
        InitializeComponent();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку локального Git-репозитория",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FolderName;
            if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = Path.GetFileName(dialog.FolderName);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text.Trim();
        if (!Directory.Exists(path) || !Directory.Exists(Path.Combine(path, ".git")))
        {
            ValidationText.Text = "Выберите папку, содержащую Git-репозиторий (.git).";
            return;
        }

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ValidationText.Text = "Укажите название проекта.";
            return;
        }

        Project = new ProjectSettings
        {
            Id = CreateId(NameBox.Text),
            Name = NameBox.Text.Trim(),
            LocalPath = path,
            RemoteUrl = RemoteBox.Text.Trim(),
            DefaultBranch = string.IsNullOrWhiteSpace(BranchBox.Text) ? "master" : BranchBox.Text.Trim(),
            SourceRoots = ["src/cf", "src/cfe", "src/epf", "src/erf"]
        };
        DialogResult = true;
    }

    private static string CreateId(string name)
    {
        var characters = name.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray();
        return new string(characters).Trim('-');
    }
}
