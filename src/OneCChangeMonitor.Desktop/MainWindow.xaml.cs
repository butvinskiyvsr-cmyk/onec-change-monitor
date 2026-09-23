using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using OneCChangeMonitor.Application;
using OneCChangeMonitor.Domain;
using OneCChangeMonitor.Infrastructure;

namespace OneCChangeMonitor.Desktop;

public partial class MainWindow : Window
{
    private static readonly Brush DiffForeground = new SolidColorBrush(Color.FromRgb(233, 238, 245));
    private static readonly Brush AddedForeground = new SolidColorBrush(Color.FromRgb(185, 246, 210));
    private static readonly Brush AddedBackground = new SolidColorBrush(Color.FromRgb(25, 67, 48));
    private static readonly Brush DeletedForeground = new SolidColorBrush(Color.FromRgb(255, 200, 200));
    private static readonly Brush DeletedBackground = new SolidColorBrush(Color.FromRgb(73, 37, 42));
    private static readonly Brush InfoForeground = new SolidColorBrush(Color.FromRgb(157, 184, 213));
    private static readonly Brush InfoBackground = new SolidColorBrush(Color.FromRgb(28, 47, 66));
    private static readonly Brush ConflictForeground = new SolidColorBrush(Color.FromRgb(255, 218, 151));
    private static readonly Brush ConflictBackground = new SolidColorBrush(Color.FromRgb(71, 53, 26));

    private ChangeMonitorService? _service;
    private RepositoryProject? _project;
    private List<CommitItem> _commits = [];
    private bool _changingSelection;
    private CancellationTokenSource? _detailsCancellation;
    private CancellationTokenSource? _diffCancellation;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStatus("Загрузка настроек…");
            var settings = await DesktopSettings.TryLoadAsync();
            if (settings is null || settings.Projects.Count == 0)
            {
                var setup = new SetupWindow { Owner = this };
                if (setup.ShowDialog() != true)
                {
                    Close();
                    return;
                }

                settings = new DesktopSettings { Projects = [setup.Project] };
                await settings.SaveAsync();
            }
            var projects = settings.Projects.Select(item => item.ToDomain()).ToArray();
            if (projects.Length == 0)
            {
                throw new InvalidOperationException("В настройках нет ни одного Git-проекта.");
            }

            var catalog = new InMemoryProjectCatalog(projects);
            var classifier = new OneCPathClassifier();
            _service = new ChangeMonitorService(catalog, new GitCliRepositoryReader(classifier));
            ProjectCombo.ItemsSource = projects;
            ProjectCombo.SelectedIndex = 0;
        }
        catch (Exception exception)
        {
            ShowFatalError(exception);
        }
    }

    private async void ProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingSelection || _service is null || ProjectCombo.SelectedItem is not RepositoryProject project) return;
        _project = project;
        try
        {
            SetBusy(true, "Чтение веток…");
            var branches = await _service.GetBranchesAsync(project.Id, CancellationToken.None);
            _changingSelection = true;
            BranchCombo.ItemsSource = branches;
            BranchCombo.SelectedItem = branches.Contains(project.DefaultBranch) ? project.DefaultBranch : branches.FirstOrDefault();
            _changingSelection = false;
            await LoadCommitsAsync();
        }
        catch (Exception exception)
        {
            _changingSelection = false;
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BranchCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingSelection || _service is null || _project is null || BranchCombo.SelectedItem is null) return;
        await RunUiOperationAsync(LoadCommitsAsync, "Чтение истории…");
    }

    private async Task LoadCommitsAsync()
    {
        if (_service is null || _project is null || BranchCombo.SelectedItem is not string branch) return;
        var commits = await _service.GetCommitsAsync(_project.Id, branch, 100, CancellationToken.None);
        _commits = commits.Select(item => new CommitItem(item)).ToList();
        ApplyCommitFilter();
        CommitList.SelectedItem = _commits.FirstOrDefault(item => !item.Subject.StartsWith("Merge ", StringComparison.OrdinalIgnoreCase)) ?? _commits.FirstOrDefault();
        SetStatus($"Загружено коммитов: {_commits.Count}");
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyCommitFilter();

    private void ApplyCommitFilter()
    {
        if (CommitList is null) return;
        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        CommitList.ItemsSource = string.IsNullOrWhiteSpace(query)
            ? _commits
            : _commits.Where(item => $"{item.Subject} {item.Source.Author} {item.ShortSha}".Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private async void CommitList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_service is null || _project is null || CommitList.SelectedItem is not CommitItem item) return;
        _detailsCancellation?.Cancel();
        _detailsCancellation = new CancellationTokenSource();
        try
        {
            SetStatus("Анализ коммита…");
            var details = await _service.GetCommitAsync(_project.Id, item.Sha, _detailsCancellation.Token);
            if (details is null) return;
            RenderCommit(details);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void RenderCommit(CommitDetails details)
    {
        EmptyDetails.Visibility = Visibility.Collapsed;
        CommitDetails.Visibility = Visibility.Visible;
        CommitTitle.Text = details.Commit.Subject;
        CommitMeta.Text = $"{details.Commit.ShortSha} · {details.Commit.Author} · {details.Commit.AuthoredAt.LocalDateTime:g}";
        var files = details.Files.Select(file => new ChangedFileItem(file)).ToList();
        FileList.ItemsSource = files;
        ShowWarning(details.Files.Any(file => file.OneCObject?.IsSuspicious == true)
            ? "Обнаружены резервные или бинарные файлы (.orig/.bin). Их изменения требуют проверки."
            : null);
        ClearDiff();
        FileList.SelectedItem = files.FirstOrDefault(file => file.Source.OneCObject?.IsCode == true)
            ?? files.FirstOrDefault(file => !file.Path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            ?? files.FirstOrDefault();
        SetStatus($"Файлов в коммите: {files.Count}");
    }

    private async void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_service is null || _project is null || CommitList.SelectedItem is not CommitItem commit || FileList.SelectedItem is not ChangedFileItem file) return;
        _diffCancellation?.Cancel();
        _diffCancellation = new CancellationTokenSource();
        try
        {
            SetStatus("Загрузка изменений файла…");
            var diff = await _service.GetDiffAsync(_project.Id, commit.Sha, file.Path, _diffCancellation.Token);
            RenderDiff(diff);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void RenderDiff(FileDiff diff)
    {
        DiffPath.Text = diff.Path;
        DiffPath.Visibility = Visibility.Visible;
        DiffBox.Visibility = Visibility.Visible;
        DiffBox.Document.Blocks.Clear();
        if (diff.IsBinary)
        {
            AddDiffLine("Бинарный файл нельзя сравнить построчно.", InfoForeground, InfoBackground);
            return;
        }

        var hasConflictMarkers = false;
        foreach (var line in diff.Content.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("<<<<<<<", StringComparison.Ordinal) || line.StartsWith("=======", StringComparison.Ordinal) || line.StartsWith(">>>>>>>", StringComparison.Ordinal))
            {
                hasConflictMarkers = true;
                AddDiffLine(line, ConflictForeground, ConflictBackground);
            }
            else if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal)) AddDiffLine(line, AddedForeground, AddedBackground);
            else if (line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal)) AddDiffLine(line, DeletedForeground, DeletedBackground);
            else if (line.StartsWith("@@", StringComparison.Ordinal)) AddDiffLine(line, InfoForeground, InfoBackground);
            else AddDiffLine(line, DiffForeground, Brushes.Transparent);
        }

        if (hasConflictMarkers)
        {
            ShowWarning("В файле обнаружены маркеры незавершённого Git-конфликта: <<<<<<<, =======, >>>>>>>.");
        }
        SetStatus($"Diff загружен: {diff.Content.Length:N0} символов");
    }

    private void AddDiffLine(string text, Brush foreground, Brush background)
    {
        DiffBox.Document.Blocks.Add(new Paragraph(new Run(string.IsNullOrEmpty(text) ? " " : text))
        {
            Margin = new Thickness(0),
            Padding = new Thickness(4, 0, 4, 0),
            Foreground = foreground,
            Background = background
        });
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null || _project is null) return;
        await RunUiOperationAsync(async () =>
        {
            SyncLabel.Text = "Синхронизация…";
            await _service.FetchAsync(_project.Id, CancellationToken.None);
            await LoadCommitsAsync();
            SyncLabel.Text = "Синхронизировано";
        }, "Получение изменений из Gitea…");
    }

    private async Task RunUiOperationAsync(Func<Task> operation, string status)
    {
        try
        {
            SetBusy(true, status);
            await operation();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ClearDiff()
    {
        DiffPath.Text = string.Empty;
        DiffPath.Visibility = Visibility.Collapsed;
        DiffBox.Document.Blocks.Clear();
        DiffBox.Visibility = Visibility.Collapsed;
    }

    private void ShowWarning(string? message)
    {
        WarningPanel.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        WarningText.Text = message ?? string.Empty;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        SyncButton.IsEnabled = !busy;
        ProjectCombo.IsEnabled = !busy;
        BranchCombo.IsEnabled = !busy;
        Mouse.OverrideCursor = busy ? System.Windows.Input.Cursors.Wait : null;
        if (status is not null) SetStatus(status);
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void ShowError(Exception exception)
    {
        SetStatus($"Ошибка: {exception.Message}");
        MessageBox.Show(this, exception.Message, "Контур изменений 1С", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowFatalError(Exception exception)
    {
        ShowError(exception);
        Close();
    }
}
