using OneCChangeMonitor.Application;
using OneCChangeMonitor.Domain;

namespace OneCChangeMonitor.Infrastructure;

public sealed class OneCPathClassifier : IOneCPathClassifier
{
    private static readonly IReadOnlyDictionary<string, string> ObjectTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Catalogs"] = "Справочник",
        ["Documents"] = "Документ",
        ["DataProcessors"] = "Обработка",
        ["Reports"] = "Отчёт",
        ["InformationRegisters"] = "Регистр сведений",
        ["AccumulationRegisters"] = "Регистр накопления",
        ["AccountingRegisters"] = "Регистр бухгалтерии",
        ["CalculationRegisters"] = "Регистр расчёта",
        ["CommonModules"] = "Общий модуль",
        ["CommonForms"] = "Общая форма",
        ["CommonCommands"] = "Общая команда",
        ["Roles"] = "Роль",
        ["Subsystems"] = "Подсистема",
        ["Enumerations"] = "Перечисление",
        ["Constants"] = "Константа",
        ["ExchangePlans"] = "План обмена",
        ["ChartsOfAccounts"] = "План счетов",
        ["ChartsOfCharacteristicTypes"] = "План видов характеристик",
        ["ChartsOfCalculationTypes"] = "План видов расчёта",
        ["BusinessProcesses"] = "Бизнес-процесс",
        ["Tasks"] = "Задача",
        ["WebServices"] = "Web-сервис",
        ["HTTPServices"] = "HTTP-сервис",
        ["EventSubscriptions"] = "Подписка на событие",
        ["ScheduledJobs"] = "Регламентное задание",
        ["SessionParameters"] = "Параметр сеанса"
    };

    public OneCObjectReference? Classify(RepositoryProject project, string path)
    {
        var normalized = path.Replace('\\', '/');
        var root = project.SourceRoots.Select(item => item.Trim('/').Replace('\\', '/')).OrderByDescending(item => item.Length)
            .FirstOrDefault(item => normalized.StartsWith(item + "/", StringComparison.OrdinalIgnoreCase));
        if (root is null) return null;

        var parts = normalized[(root.Length + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        var sourceKind = root.EndsWith("/cf", StringComparison.OrdinalIgnoreCase) ? "Конфигурация"
            : root.EndsWith("/cfe", StringComparison.OrdinalIgnoreCase) ? "Расширение"
            : root.EndsWith("/epf", StringComparison.OrdinalIgnoreCase) ? "Внешняя обработка"
            : root.EndsWith("/erf", StringComparison.OrdinalIgnoreCase) ? "Внешний отчёт" : "Источник 1С";
        var objectType = ObjectTypes.TryGetValue(parts[0], out var translated) ? translated : parts[0];
        var objectName = RemoveExtension(parts.Length > 1 ? parts[1] : parts[0]);
        var formIndex = Array.FindIndex(parts, item => item.Equals("Forms", StringComparison.OrdinalIgnoreCase));
        var component = formIndex >= 0 && formIndex + 1 < parts.Length ? $"Форма: {RemoveExtension(parts[formIndex + 1])}"
            : parts.Any(item => item.Equals("Ext", StringComparison.OrdinalIgnoreCase)) ? "Модуль или свойства объекта" : null;
        var isCode = normalized.EndsWith(".bsl", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith(".bsl.orig", StringComparison.OrdinalIgnoreCase);
        var suspicious = normalized.EndsWith(".orig", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith(".bin", StringComparison.OrdinalIgnoreCase);
        return new OneCObjectReference(sourceKind, objectType, objectName, component, isCode, suspicious);
    }

    private static string RemoveExtension(string value) => value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value;
}
