using OneCChangeMonitor.Application;
using OneCChangeMonitor.Domain;
using OneCChangeMonitor.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Services.AddProblemDetails();

var settings = builder.Configuration.GetSection("Projects").Get<List<ProjectSettings>>() ?? [];
var projects = settings.Select(item => new RepositoryProject(
    item.Id, item.Name, Path.GetFullPath(item.LocalPath), item.RemoteUrl, item.DefaultBranch,
    item.SourceRoots.Length == 0 ? ["src/cf", "src/cfe", "src/epf", "src/erf"] : item.SourceRoots)).ToArray();

builder.Services.AddSingleton<IProjectCatalog>(new InMemoryProjectCatalog(projects));
builder.Services.AddSingleton<IOneCPathClassifier, OneCPathClassifier>();
builder.Services.AddSingleton<IGitRepositoryReader, GitCliRepositoryReader>();
builder.Services.AddSingleton<ChangeMonitorService>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new { status = "ok", version = "0.3.1" }));
api.MapGet("/projects", (ChangeMonitorService service) => service.GetProjects().Select(project => new
{
    project.Id,
    project.Name,
    project.RemoteUrl,
    project.DefaultBranch,
    project.SourceRoots
}));
api.MapGet("/projects/{projectId}/branches", async (string projectId, ChangeMonitorService service, CancellationToken token) =>
    Results.Ok(await service.GetBranchesAsync(projectId, token)));
api.MapGet("/projects/{projectId}/branches/{branch}/status", async (string projectId, string branch, ChangeMonitorService service, CancellationToken token) =>
    Results.Ok(await service.GetBranchStatusAsync(projectId, branch, token)));
api.MapGet("/projects/{projectId}/commits", async (string projectId, string? branch, int? limit, ChangeMonitorService service, CancellationToken token) =>
    Results.Ok(await service.GetCommitsAsync(projectId, branch, limit ?? 40, token)));
api.MapGet("/projects/{projectId}/commits/{sha}", async (string projectId, string sha, ChangeMonitorService service, CancellationToken token) =>
{
    var result = await service.GetCommitAsync(projectId, sha, token);
    return result is null ? Results.NotFound() : Results.Ok(result);
});
api.MapGet("/projects/{projectId}/commits/{sha}/diff", async (string projectId, string sha, string path, ChangeMonitorService service, CancellationToken token) =>
    Results.Ok(await service.GetDiffAsync(projectId, sha, path, token)));
api.MapPost("/projects/{projectId}/sync", async (string projectId, ChangeMonitorService service, CancellationToken token) =>
{
    await service.FetchAsync(projectId, token);
    return Results.Ok(new { synchronizedAt = DateTimeOffset.UtcNow });
});

app.MapFallbackToFile("index.html");
app.Run();

public sealed class ProjectSettings
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string LocalPath { get; init; } = string.Empty;
    public string RemoteUrl { get; init; } = string.Empty;
    public string DefaultBranch { get; init; } = "master";
    public string[] SourceRoots { get; init; } = [];
}
