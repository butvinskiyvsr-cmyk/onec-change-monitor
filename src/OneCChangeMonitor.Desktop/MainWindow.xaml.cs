using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OneCChangeMonitor.Application;
using OneCChangeMonitor.Domain;
using OneCChangeMonitor.Infrastructure;

namespace OneCChangeMonitor.Desktop;

public partial class MainWindow : Window
{
    private readonly UpdateService _updateService = new();
    private ChangeMonitorService? _service;
    private DesktopSettings _settings = new();
    private RepositoryProject? _project;
    private UpdateInfo? _latestUpdate;
    private List<CommitItem> _commits = [];
    private List<QualityFinding> _qualityFindings = [];
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
            _settings = await DesktopSettings.TryLoadAsync() ?? new DesktopSettings();
            if (_settings.Projects.Count == 0)
            {
                var setup = new SetupWindow { Owner = this };
                if (setup.ShowDialog() != true)
                {
                    Close();
                    return;
                }

                _settings.Projects.Add(setup.Project);
                await _settings.SaveAsync();
            }

            InitializeProjects(_settings.Projects[0].Id);
            CheckUpdatesAtStartupBox.IsChecked = _settings.CheckForUpdatesOnStartup;
            if (_settings.CheckForUpdatesOnStartup) _ = CheckForUpdatesAsync(silent: true);
        }
        catch (Exception exception)
        {
            ShowFatalError(exception);
        }
    }

    private void InitializeProjects(string? selectedProjectId = null)
    {
        var projects = _settings.Projects.Select(item => item.ToDomain()).ToArray();
        if (projects.Length == 0) throw new InvalidOperationException("В настройках нет ни одного Git-проекта.");

        _changingSelection = true;
        _service = new ChangeMonitorService(new InMemoryProjectCatalog(projects), new GitCliRepositoryReader(new OneCPathClassifier()));
        ProjectCombo.ItemsSource = projects;
        ProjectList.ItemsSource = _settings.Projects;
        ProjectCombo.SelectedItem = projects.FirstOrDefault(item => item.Id == selectedProjectId) ?? projects[0];
        _changingSelection = false;
        _ = SelectProjectAsync((RepositoryProject)ProjectCombo.SelectedItem);
    }

    private async void ProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingSelection || ProjectCombo.SelectedItem is not RepositoryProject project) return;
        await SelectProjectAsync(project);
    }

    private async Task SelectProjectAsync(RepositoryProject project)
    {
        if (_service is null) return;
        _project = project;
        TitleProjectText.Text = $"/ {project.Name}";
        OverviewProjectName.Text = project.Name;
        OverviewProjectPath.Text = project.LocalPath;
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
        CommitCountBadge.Text = _commits.Count.ToString();
        HistoryCaption.Text = $"{_commits.Count} комм.";
        OverviewCommitCount.Text = _commits.Count.ToString();
        CommitList.SelectedItem = _commits.FirstOrDefault(item => !item.Subject.StartsWith("Merge ", StringComparison.OrdinalIgnoreCase)) ?? _commits.FirstOrDefault();
        SetStatus($"Загружено коммитов: {_commits.Count}");
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyCommitFilter();

    private void ApplyCommitFilter()
    {
        if (CommitList is null) return;
        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        var items = string.IsNullOrWhiteSpace(query)
            ? _commits
            : _commits.Where(item => $"{item.Subject} {item.Author} {item.ShortSha}".Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        CommitList.ItemsSource = items;
        HistoryCaption.Text = $"{items.Count} комм.";
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
            if (details is not null) RenderCommit(details);
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
        CommitTitle.Text = details.Commit.Subject;
        CommitMeta.Text = $"{details.Commit.ShortSha} · {details.Commit.Author} · {details.Commit.AuthoredAt.LocalDateTime:g}";
        var files = details.Files.Select(file => new ChangedFileItem(file)).ToList();
        FileList.ItemsSource = files;
        FileCountText.Text = files.Count.ToString();

        var objects = details.Files.Where(file => file.OneCObject is not null)
            .GroupBy(file => $"{file.OneCObject!.ObjectType}.{file.OneCObject.ObjectName}")
            .Select(group => new ObjectListItem(group.Key, string.Join(" · ", group.Select(file => file.OneCObject!.Component ?? file.Path).Distinct())))
            .OrderBy(item => item.Title).ToList();
        ObjectList.ItemsSource = objects;
        OverviewObjectCount.Text = objects.Count.ToString();

        _qualityFindings = AnalyzeFiles(details.Files);
        RenderQuality();
        ShowWarning(_qualityFindings.Count > 0 ? $"Проверки обнаружили замечаний: {_qualityFindings.Count}" : null);
        ClearDiff();
        FileList.SelectedItem = files.FirstOrDefault(file => file.Source.OneCObject?.IsCode == true)
            ?? files.FirstOrDefault(file => !file.Path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            ?? files.FirstOrDefault();
        SetStatus($"Файлов в коммите: {files.Count}");
    }

    private static List<QualityFinding> AnalyzeFiles(IEnumerable<ChangedFile> files)
    {
        var result = new List<QualityFinding>();
        foreach (var file in files)
        {
            if (file.OneCObject?.IsSuspicious == true)
                result.Add(new QualityFinding("Резервный или бинарный файл", file.Path, "Требует проверки"));
            if (file.Path.Contains("Roles/", StringComparison.OrdinalIgnoreCase) || file.Path.EndsWith("Rights.xml", StringComparison.OrdinalIgnoreCase))
                result.Add(new QualityFinding("Изменены права доступа", file.Path, "Средний риск"));
        }
        return result;
    }

    private void RenderQuality()
    {
        QualityList.ItemsSource = null;
        QualityList.ItemsSource = _qualityFindings;
        QualityEmptyText.Visibility = _qualityFindings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        QualityBadge.Visibility = _qualityFindings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        QualityCountBadge.Text = _qualityFindings.Count.ToString();
        OverviewWarningCount.Text = _qualityFindings.Count.ToString();
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
            RenderDiff(diff, file);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void RenderDiff(FileDiff diff, ChangedFileItem file)
    {
        DiffPath.Text = diff.Path;
        DiffObjectTitle.Text = file.Title;
        EmptyDiffPanel.Visibility = Visibility.Collapsed;
        if (diff.IsBinary)
        {
            DiffList.ItemsSource = new[] { new DiffDisplayRow(new SideBySideDiffRow(null, "Бинарный файл нельзя сравнить построчно.", DiffLineKind.Header, null, string.Empty, DiffLineKind.Header)) };
            SetStatus("Выбран бинарный файл");
            return;
        }

        var rows = UnifiedDiffParser.Parse(diff.Content);
        DiffList.ItemsSource = rows.Select(row => new DiffDisplayRow(row)).ToList();
        if (rows.Any(row => row.OldKind == DiffLineKind.Conflict || row.NewKind == DiffLineKind.Conflict) && _qualityFindings.All(item => item.Title != "Незавершённый Git-конфликт"))
        {
            _qualityFindings.Add(new QualityFinding("Незавершённый Git-конфликт", diff.Path, "Высокий риск"));
            RenderQuality();
            ShowWarning("В файле обнаружены маркеры незавершённого Git-конфликта.");
        }
        SetStatus($"Diff загружен: {rows.Count} строк");
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null || _project is null) return;
        await RunUiOperationAsync(async () =>
        {
            SyncLabel.Text = "Синхронизация…";
            SyncDot.Fill = (Brush)FindResource("WarningBrush");
            await _service.FetchAsync(_project.Id, CancellationToken.None);
            await LoadCommitsAsync();
            SyncLabel.Text = $"Синхронизировано {DateTime.Now:HH:mm}";
            SyncDot.Fill = (Brush)FindResource("SuccessBrush");
        }, "Получение изменений из Git…");
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string page }) ShowPage(page);
    }

    private void ShowPage(string page)
    {
        ChangesPage.Visibility = page == "Changes" ? Visibility.Visible : Visibility.Collapsed;
        OverviewPage.Visibility = page == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        ObjectsPage.Visibility = page == "Objects" ? Visibility.Visible : Visibility.Collapsed;
        ReleasesPage.Visibility = page == "Releases" ? Visibility.Visible : Visibility.Collapsed;
        QualityPage.Visibility = page == "Quality" ? Visibility.Visible : Visibility.Collapsed;
        ProjectsPage.Visibility = page == "Projects" ? Visibility.Visible : Visibility.Collapsed;
        UpdatesPage.Visibility = page == "Updates" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenChanges_Click(object sender, RoutedEventArgs e)
    {
        ChangesNav.IsChecked = true;
        ShowPage("Changes");
    }

    private async void AddProject_Click(object sender, RoutedEventArgs e)
    {
        var setup = new SetupWindow { Owner = this };
        if (setup.ShowDialog() != true) return;
        if (_settings.Projects.Any(project => project.Id.Equals(setup.Project.Id, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Проект с таким названием уже подключён.", "OneC Change Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _settings.Projects.Add(setup.Project);
        await _settings.SaveAsync();
        InitializeProjects(setup.Project.Id);
        ProjectsNav.IsChecked = true;
        ShowPage("Projects");
        SetStatus($"Проект «{setup.Project.Name}» добавлен");
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(silent: false);

    private async Task CheckForUpdatesAsync(bool silent)
    {
        try
        {
            CheckUpdatesButton.IsEnabled = false;
            LatestVersionText.Text = "ПРОВЕРКА ВЕРСИИ…";
            _latestUpdate = await _updateService.CheckAsync(CancellationToken.None);
            OpenReleaseButton.IsEnabled = true;
            ReleaseNotesText.Text = TrimReleaseNotes(_latestUpdate.Notes);
            if (_latestUpdate.IsNewer)
            {
                LatestVersionText.Text = $"ДОСТУПНА {_latestUpdate.Tag}";
                UpdateTitleText.Text = _latestUpdate.Title;
                UpdateDescriptionText.Text = "Новая версия готова к загрузке. Откройте страницу релиза, чтобы ознакомиться с пакетом и контрольными суммами.";
                UpdateAvailableButton.Content = $"Доступна {_latestUpdate.Tag}";
                UpdateAvailableButton.Visibility = Visibility.Visible;
            }
            else
            {
                LatestVersionText.Text = "УСТАНОВЛЕНА ПОСЛЕДНЯЯ ВЕРСИЯ";
                UpdateTitleText.Text = "Приложение актуально";
                UpdateDescriptionText.Text = $"Текущая версия {UpdateService.CurrentVersion} не требует обновления.";
                UpdateAvailableButton.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception exception)
        {
            LatestVersionText.Text = "НЕ УДАЛОСЬ ПРОВЕРИТЬ";
            UpdateDescriptionText.Text = exception.Message;
            if (!silent) ShowError(exception);
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private static string TrimReleaseNotes(string notes)
    {
        var normalized = notes.Replace("\r\n", "\n").Trim();
        return normalized.Length <= 900 ? normalized : normalized[..900] + "…";
    }

    private void UpdateAvailableButton_Click(object sender, RoutedEventArgs e)
    {
        UpdatesNav.IsChecked = true;
        ShowPage("Updates");
    }

    private void OpenReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_latestUpdate is null) return;
        Process.Start(new ProcessStartInfo(_latestUpdate.ReleaseUri.AbsoluteUri) { UseShellExecute = true });
    }

    private async void UpdatePreference_Click(object sender, RoutedEventArgs e)
    {
        _settings.CheckForUpdatesOnStartup = CheckUpdatesAtStartupBox.IsChecked == true;
        await _settings.SaveAsync();
        SetStatus("Настройки обновлений сохранены");
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
        DiffPath.Text = "Выберите файл слева";
        DiffObjectTitle.Text = "Сравнение изменений";
        DiffList.ItemsSource = null;
        EmptyDiffPanel.Visibility = Visibility.Visible;
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
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Input.Mouse.OverrideCursor = busy ? System.Windows.Input.Cursors.Wait : null;
        if (status is not null) SetStatus(status);
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void ShowError(Exception exception)
    {
        SetStatus($"Ошибка: {exception.Message}");
        MessageBox.Show(this, exception.Message, "OneC Change Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowFatalError(Exception exception)
    {
        ShowError(exception);
        Close();
    }
}
