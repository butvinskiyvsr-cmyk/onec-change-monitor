using OneCChangeMonitor.Application;
using OneCChangeMonitor.Domain;

namespace OneCChangeMonitor.Infrastructure;

public sealed class InMemoryProjectCatalog(IEnumerable<RepositoryProject> projects) : IProjectCatalog
{
    private readonly IReadOnlyList<RepositoryProject> _projects = projects.ToArray();
    public IReadOnlyList<RepositoryProject> GetAll() => _projects;
    public RepositoryProject? Find(string id) => _projects.FirstOrDefault(project => string.Equals(project.Id, id, StringComparison.OrdinalIgnoreCase));
}
