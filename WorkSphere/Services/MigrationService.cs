using System.Globalization;
using System.Text.RegularExpressions;
using WorkSphere.Models;

namespace WorkSphere.Services;

public class MigrationService
{
    private readonly WorkLogService _workLogService;
    private readonly string _logsPath;

    public MigrationService(WorkLogService workLogService, IConfiguration configuration)
    {
        _workLogService = workLogService;
        _logsPath = configuration["Migration:LogsPath"] ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Import");
    }

    public async Task MigrateAsync()
    {
        if (!Directory.Exists(_logsPath))
        {
            Console.WriteLine($"Directory not found: {_logsPath}");
            return;
        }

        var employees = await _workLogService.GetEmployeesAsync();
        var employeeMap = employees.ToDictionary(e => e.Initials, e => e.Id);

        var existingLogs = await _workLogService.GetWorkLogsAsync();
        var logKeys = existingLogs
            .Select(l => GetLogKey(l))
            .ToHashSet();

        var files = Directory.GetFiles(_logsPath, "*.md");
        foreach (var file in files)
        {
            var parsedLogs = await ParseMarkdownFileAsync(file, employeeMap);
            foreach (var log in parsedLogs)
            {
                var key = GetLogKey(log);
                if (!logKeys.Contains(key))
                {
                    await _workLogService.AddWorkLogAsync(log);
                    logKeys.Add(key);
                }
            }
        }
    }

    public async Task<List<WorkLog>> ParseMarkdownFileAsync(string filePath, Dictionary<string, int> employeeMap)
    {
        var logs = new List<WorkLog>();
        if (!File.Exists(filePath)) return logs;

        var content = await File.ReadAllTextAsync(filePath);
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (!IsMarkdownDataRow(line))
            {
                continue;
            }

            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length >= 5)
            {
                var dateStr = parts[1];
                var timeStr = parts[2];
                var initials = parts[3];
                var detailsStr = parts[4];

                if (string.IsNullOrWhiteSpace(detailsStr) || detailsStr.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (DateOnly.TryParseExact(dateStr, "MM/dd/yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    TimeOnly? time = null;
                    if (TimeOnly.TryParse(timeStr, out var parsedTime))
                    {
                        time = parsedTime;
                    }

                    var (main, sub, details) = Categorize(detailsStr);

                    if (employeeMap.TryGetValue(initials, out var employeeId))
                    {
                        logs.Add(new WorkLog
                        {
                            LogDate = date,
                            LogTime = time,
                            EmployeeId = employeeId,
                            MainCategory = main,
                            SubCategory = sub,
                            Details = details,
                            OriginalDetails = detailsStr
                        });
                    }
                }
            }
        }
        return logs;
    }

    private static bool IsMarkdownDataRow(string line)
    {
        if (!line.StartsWith("|", StringComparison.Ordinal) || line.Contains("---", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = line.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 5)
        {
            return false;
        }

        return !(string.Equals(parts[1], "Day", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[2], "Time", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[3], "Subject", StringComparison.OrdinalIgnoreCase));
    }

    private string GetLogKey(WorkLog l) => $"{l.LogDate:yyyyMMdd}_{l.EmployeeId}_{l.OriginalDetails}";

    private (string Main, string Sub, string Details) Categorize(string rawDetails)
    {
        if (string.IsNullOrWhiteSpace(rawDetails)) return ("Other", "Other", "");

        var details = rawDetails.Trim();
        
        // Scrub common noise but keep it in the details
        // Example: "Sick Day (2 of 5 used)" -> Sub: "Sick Day", Details: "Sick Day (2 of 5 used)"
        
        // 1. Leave / Time Off
        if (details.Contains("FMLA", StringComparison.OrdinalIgnoreCase))
        {
            return ("Leave", "FMLA Day", details);
        }
        if (details.Contains("Sick", StringComparison.OrdinalIgnoreCase) || details.Contains("Doctor", StringComparison.OrdinalIgnoreCase))
        {
            return ("Leave", "Sick Day", details);
        }
        if (details.Contains("Vacation", StringComparison.OrdinalIgnoreCase) || details.Contains("Holiday", StringComparison.OrdinalIgnoreCase) || details.Contains("PTO", StringComparison.OrdinalIgnoreCase))
        {
            return ("Leave", "PTO", details);
        }
        if (details.Contains("Comp Time", StringComparison.OrdinalIgnoreCase) || details.Contains("Comp Day", StringComparison.OrdinalIgnoreCase))
        {
            return ("Leave", "Comp Day", details);
        }

        // 2. Work / Active
        if (details.Contains("Work From Home", StringComparison.OrdinalIgnoreCase) || details.Contains("WFH", StringComparison.OrdinalIgnoreCase))
        {
            return ("Work", "Work From Home", details);
        }
        if (details.Contains("Plant", StringComparison.OrdinalIgnoreCase) || details.Contains("Corp", StringComparison.OrdinalIgnoreCase) || details.Contains("Remote", StringComparison.OrdinalIgnoreCase))
        {
            return ("Work", "Remote Location", details);
        }
        if (details.Contains("Arrive in Office", StringComparison.OrdinalIgnoreCase) || details.Contains("In The Office", StringComparison.OrdinalIgnoreCase) || details.Contains("At Office", StringComparison.OrdinalIgnoreCase))
        {
            return ("Work", "In-Office", details);
        }
        if (details.Contains("Incident", StringComparison.OrdinalIgnoreCase) || details.Contains("Response", StringComparison.OrdinalIgnoreCase))
        {
            return ("Work", "Incident Response", details);
        }

        return ("Other", "Other", details);
    }
}
