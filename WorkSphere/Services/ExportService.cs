using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkSphere.Models;

namespace WorkSphere.Services;

public class ExportService : IExportService
{
    private readonly IWorkLogService _workLogService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ExportService> _logger;

    public ExportService(
        IWorkLogService workLogService,
        IConfiguration configuration,
        ILogger<ExportService> logger)
    {
        _workLogService = workLogService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> ExportWorkLogsCsvAsync(WorkLogExportFilter? filter = null)
    {
        var logs = (await _workLogService.GetWorkLogsAsync()).AsEnumerable();

        if (filter != null)
        {
            if (filter.EmployeeId.HasValue)
                logs = logs.Where(l => l.EmployeeId == filter.EmployeeId.Value);

            if (filter.StartDate.HasValue)
                logs = logs.Where(l => l.LogDate >= filter.StartDate.Value);

            if (filter.EndDate.HasValue)
                logs = logs.Where(l => l.LogDate <= filter.EndDate.Value);

            if (!string.IsNullOrWhiteSpace(filter.MainCategory))
                logs = logs.Where(l => string.Equals(l.MainCategory, filter.MainCategory, StringComparison.OrdinalIgnoreCase));

            if (filter.CompTimeOnly == true)
                logs = logs.Where(l => l.EarnsCompTime || l.UsesCompTime);
        }

        var sortedLogs = logs
            .OrderBy(l => l.LogDate)
            .ThenBy(l => l.LogTime)
            .ThenBy(l => l.Employee?.Name)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("Id,Date,Time,EmployeeId,EmployeeName,Initials,MainCategory,SubCategory,EarnsCompTime,UsesCompTime,Hours,IncidentId,IncidentTicket,Details");

        foreach (var l in sortedLogs)
        {
            var dateStr = l.LogDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
            var timeStr = l.LogTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
            var empName = l.Employee?.Name ?? "";
            var empInitials = l.Employee?.Initials ?? "";
            var incidentTicket = l.Incident?.TicketNumber ?? (l.IncidentId.HasValue ? $"#{l.IncidentId}" : "");
            var details = l.Details ?? l.OriginalDetails ?? "";

            sb.AppendLine(string.Join(",",
                EscapeCsv(l.Id),
                EscapeCsv(dateStr),
                EscapeCsv(timeStr),
                EscapeCsv(l.EmployeeId),
                EscapeCsv(empName),
                EscapeCsv(empInitials),
                EscapeCsv(l.MainCategory),
                EscapeCsv(l.SubCategory),
                EscapeCsv(l.EarnsCompTime),
                EscapeCsv(l.UsesCompTime),
                EscapeCsv(l.Hours),
                EscapeCsv(l.IncidentId),
                EscapeCsv(incidentTicket),
                EscapeCsv(details)));
        }

        return sb.ToString();
    }

    public async Task<string> ExportCompTimeSummaryCsvAsync()
    {
        var stats = await _workLogService.GetCompTimeStatsAsync();
        var employees = (await _workLogService.GetEmployeesAsync()).ToDictionary(e => e.Id, e => e.Initials);

        var sb = new StringBuilder();
        sb.AppendLine("EmployeeId,EmployeeName,Initials,EarnedHours,UsedHours,BalanceHours");

        foreach (var s in stats)
        {
            var initials = employees.TryGetValue(s.Id, out var ini) ? ini : "";
            sb.AppendLine(string.Join(",",
                EscapeCsv(s.Id),
                EscapeCsv(s.Name),
                EscapeCsv(initials),
                EscapeCsv(s.Earned),
                EscapeCsv(s.Used),
                EscapeCsv(s.Balance)));
        }

        return sb.ToString();
    }

    public async Task<string> ExportIncidentsCsvAsync()
    {
        var incidents = await _workLogService.GetIncidentStatsAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Id,TicketNumber,Title,Status,StartedAt,EndedAt,TotalCompHours,Details");

        foreach (var inc in incidents)
        {
            var startedStr = inc.StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var endedStr = inc.EndedAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
            var status = inc.IsClosed ? "Closed" : "Open";

            sb.AppendLine(string.Join(",",
                EscapeCsv(inc.Id),
                EscapeCsv(inc.TicketNumber),
                EscapeCsv(inc.Title),
                EscapeCsv(status),
                EscapeCsv(startedStr),
                EscapeCsv(endedStr),
                EscapeCsv(inc.TotalCompHours),
                EscapeCsv(inc.Details)));
        }

        return sb.ToString();
    }

    public async Task<string> ExportMarkdownAsync(int year, int month, string format = "Original", int? employeeId = null)
    {
        var monthLogs = (await _workLogService.GetWorkLogsByMonthAsync(year, month)).ToList();

        if (employeeId.HasValue)
        {
            monthLogs = monthLogs.Where(l => l.EmployeeId == employeeId.Value).ToList();
        }

        if (monthLogs.Count == 0)
        {
            return string.Empty;
        }

        var exportDate = new DateTime(year, month, 1);
        var sb = new StringBuilder();
        sb.AppendLine($"# {exportDate.ToString("MMMM yyyy", CultureInfo.InvariantCulture)} ({format} Format)");
        sb.AppendLine();

        var groupedLogs = monthLogs.GroupBy(l => l.EmployeeId);

        foreach (var group in groupedLogs)
        {
            var firstLog = group.First();
            var empName = firstLog.Employee?.Name ?? "Unknown";
            var initials = firstLog.Employee?.Initials ?? "??";

            sb.AppendLine($"## Subject {empName}");
            sb.AppendLine();

            if (string.Equals(format, "Extended", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("| Day      | Time  | Subject | Category            | Comp   | Incident | Details                                          |");
                sb.AppendLine("|----------|-------|---------|---------------------|--------|----------|--------------------------------------------------|");
            }
            else
            {
                sb.AppendLine("| Day      | Time  | Subject | Details                                          |");
                sb.AppendLine("|----------|-------|---------|--------------------------------------------------|");
            }

            foreach (var log in group)
            {
                var dateStr = log.LogDate?.ToString("MM/dd/yy", CultureInfo.InvariantCulture) ?? "";
                var timeStr = log.LogTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
                var details = log.Details ?? log.OriginalDetails ?? "";

                if (string.Equals(format, "Extended", StringComparison.OrdinalIgnoreCase))
                {
                    var catStr = $"{log.MainCategory}/{log.SubCategory}";
                    var compStr = log.EarnsCompTime ? $"+{log.Hours:0.0}" : log.UsesCompTime ? $"-{log.Hours:0.0}" : "";
                    var incStr = log.IncidentId.HasValue ? $"#{log.IncidentId}" : "";

                    sb.AppendLine($"| {dateStr,-8} | {timeStr,-5} | {initials,-7} | {catStr,-19} | {compStr,-6} | {incStr,-8} | {details,-48} |");
                }
                else
                {
                    sb.AppendLine($"| {dateStr,-8} | {timeStr,-5} | {initials,-7} | {details,-48} |");
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task<string> ExportFullBackupJsonAsync()
    {
        var employees = (await _workLogService.GetEmployeesAsync()).ToList();
        var incidents = (await _workLogService.GetIncidentsAsync()).ToList();
        var workLogs = (await _workLogService.GetWorkLogsAsync()).ToList();

        var snapshot = new DatabaseBackupSnapshot
        {
            ExportedAtUtc = DateTime.UtcNow,
            Version = "1.0",
            Employees = employees,
            Incidents = incidents,
            WorkLogs = workLogs
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        return JsonSerializer.Serialize(snapshot, options);
    }

    public string GetDefaultExportDirectory()
    {
        return _configuration["Export:OutputPath"]
            ?? Environment.GetEnvironmentVariable("EXPORT_PATH")
            ?? "Exports";
    }

    public async Task<int> RunCliExportAsync(string[]? args = null, string? customOutputPath = null)
    {
        string? cliPath = null;
        if (args != null)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--output" || args[i] == "-o" || args[i] == "--path") && i + 1 < args.Length)
                {
                    cliPath = args[i + 1];
                    break;
                }
                if (args[i].StartsWith("--output=", StringComparison.OrdinalIgnoreCase) ||
                    args[i].StartsWith("--path=", StringComparison.OrdinalIgnoreCase))
                {
                    cliPath = args[i].Split('=', 2)[1];
                    break;
                }
            }
        }

        var targetDir = customOutputPath ?? cliPath ?? GetDefaultExportDirectory();
        var result = await RunBackupAsync(targetDir);
        return result.Success ? 0 : 1;
    }

    public async Task<ExportResult> RunBackupAsync(string? targetDirectory = null)
    {
        var targetDir = !string.IsNullOrWhiteSpace(targetDirectory) ? targetDirectory : GetDefaultExportDirectory();

        _logger.LogInformation("Initiating automatic WorkSphere backup export to: {TargetDirectory}", targetDir);
        Directory.CreateDirectory(targetDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var snapshotDirName = $"snapshot-{timestamp}";
        var snapshotDirPath = Path.Combine(targetDir, snapshotDirName);

        Directory.CreateDirectory(snapshotDirPath);

        var writtenFiles = new List<string>();

        try
        {
            // 1. Full Database JSON Backup
            var fullJson = await ExportFullBackupJsonAsync();
            const string jsonName = "backup-full.json";
            await File.WriteAllTextAsync(Path.Combine(snapshotDirPath, jsonName), fullJson, Encoding.UTF8);
            writtenFiles.Add($"{snapshotDirName}/{jsonName}");
            _logger.LogInformation("Wrote JSON backup to {SnapshotDir}", snapshotDirName);

            // 2. WorkLogs CSV
            var logsCsv = await ExportWorkLogsCsvAsync();
            const string logsName = "worklogs.csv";
            await File.WriteAllTextAsync(Path.Combine(snapshotDirPath, logsName), logsCsv, Encoding.UTF8);
            writtenFiles.Add($"{snapshotDirName}/{logsName}");
            _logger.LogInformation("Wrote WorkLogs CSV to {SnapshotDir}", snapshotDirName);

            // 3. Comp Time Summary CSV
            var compCsv = await ExportCompTimeSummaryCsvAsync();
            const string compName = "comptime-summary.csv";
            await File.WriteAllTextAsync(Path.Combine(snapshotDirPath, compName), compCsv, Encoding.UTF8);
            writtenFiles.Add($"{snapshotDirName}/{compName}");
            _logger.LogInformation("Wrote Comp Time CSV to {SnapshotDir}", snapshotDirName);

            // 4. Incidents CSV
            var incidentsCsv = await ExportIncidentsCsvAsync();
            const string incidentsName = "incidents.csv";
            await File.WriteAllTextAsync(Path.Combine(snapshotDirPath, incidentsName), incidentsCsv, Encoding.UTF8);
            writtenFiles.Add($"{snapshotDirName}/{incidentsName}");
            _logger.LogInformation("Wrote Incidents CSV to {SnapshotDir}", snapshotDirName);

            // 5. Markdown Archives (in 'Markdown' folder)
            var allLogs = await _workLogService.GetWorkLogsAsync();
            var months = allLogs
                .Where(l => l.LogDate.HasValue)
                .Select(l => (Year: l.LogDate!.Value.Year, Month: l.LogDate!.Value.Month))
                .Distinct()
                .OrderBy(m => m.Year)
                .ThenBy(m => m.Month)
                .ToList();

            var snapshotMdDir = Path.Combine(snapshotDirPath, "Markdown");
            Directory.CreateDirectory(snapshotMdDir);

            foreach (var (year, month) in months)
            {
                var md = await ExportMarkdownAsync(year, month, "Extended");
                if (!string.IsNullOrWhiteSpace(md))
                {
                    var mdFileName = $"{year:D4}-{month:D2}.md";
                    await File.WriteAllTextAsync(Path.Combine(snapshotMdDir, mdFileName), md, Encoding.UTF8);
                    writtenFiles.Add($"{snapshotDirName}/Markdown/{mdFileName}");
                }
            }
            _logger.LogInformation("Exported {Count} markdown monthly files to {Dir}", months.Count, snapshotMdDir);

            _logger.LogInformation("Automated backup export completed successfully into {SnapshotDir}.", snapshotDirPath);
            return new ExportResult
            {
                Success = true,
                OutputDirectory = Path.GetFullPath(snapshotDirPath),
                FilesWritten = writtenFiles.Count,
                FileNames = writtenFiles
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete automated backup export to: {TargetDirectory}", targetDir);
            return new ExportResult
            {
                Success = false,
                OutputDirectory = Path.GetFullPath(snapshotDirPath),
                FilesWritten = writtenFiles.Count,
                FileNames = writtenFiles,
                ErrorMessage = ex.Message
            };
        }
    }

    public int PruneOldSnapshots(string targetDirectory, int maxHistoryCount)
    {
        if (maxHistoryCount <= 0 || !Directory.Exists(targetDirectory))
        {
            return 0;
        }

        try
        {
            var snapshotDirs = Directory.GetDirectories(targetDirectory, "snapshot-*")
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.Name)
                .ToList();

            if (snapshotDirs.Count <= maxHistoryCount)
            {
                return 0;
            }

            var toDelete = snapshotDirs.Skip(maxHistoryCount).ToList();
            var deletedCount = 0;

            foreach (var dir in toDelete)
            {
                try
                {
                    _logger.LogInformation("Pruning old backup snapshot exceeding limit of {Max}: {Directory}", maxHistoryCount, dir.FullName);
                    dir.Delete(recursive: true);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete old backup snapshot: {Directory}", dir.FullName);
                }
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while pruning old backup snapshots in: {TargetDirectory}", targetDirectory);
            return 0;
        }
    }

    private static string EscapeCsv(object? value)
    {
        if (value is null) return string.Empty;
        var str = value.ToString() ?? string.Empty;
        if (str.Contains(',') || str.Contains('"') || str.Contains('\n') || str.Contains('\r'))
        {
            return $"\"{str.Replace("\"", "\"\"")}\"";
        }
        return str;
    }
}
