using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkSphere.Models;
using WorkSphere.Services;
using WorkSphere.Data;

namespace WorkSphere.Tools;

public class LogAuditTool
{
    private readonly WorkLogService _workLogService;
    private readonly string _logsPath;

    public LogAuditTool(WorkLogService workLogService, IConfiguration configuration)
    {
        _workLogService = workLogService;
        _logsPath = configuration["Migration:LogsPath"] ?? "WorkSphere/Import";
    }

    public async Task RunAuditAsync()
    {
        Console.WriteLine("--- Starting Log Audit ---");
        
        var employees = await _workLogService.GetEmployeesAsync();
        var employeeMap = employees.ToDictionary(e => e.Initials, e => e.Id);
        
        var databaseLogs = await _workLogService.GetWorkLogsAsync();
        // Key: Date_EmpId_OriginalDetails
        var dbLookup = databaseLogs.ToDictionary(
            l => $"{l.LogDate:yyyy-MM-dd}_{l.EmployeeId}_{l.OriginalDetails}",
            l => l
        );

        var markdownEntries = new List<AuditEntry>();
        var files = Directory.GetFiles(_logsPath, "*.md");
        
        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file);
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.StartsWith("|") && !line.Contains("---") && !line.ToLower().Contains("day") && !line.ToLower().Contains("time"))
                {
                    var parts = line.Split('|', StringSplitOptions.TrimEntries);
                    if (parts.Length >= 5)
                    {
                        var dateStr = parts[1];
                        var initials = parts[3];
                        var detailsStr = parts[4];

                        if (string.IsNullOrWhiteSpace(detailsStr) || detailsStr.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (DateOnly.TryParseExact(dateStr, "MM/dd/yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        {
                            if (employeeMap.TryGetValue(initials, out var employeeId))
                            {
                                markdownEntries.Add(new AuditEntry 
                                { 
                                    Date = date, 
                                    EmployeeId = employeeId, 
                                    Initials = initials,
                                    OriginalDetails = detailsStr,
                                    SourceFile = Path.GetFileName(file)
                                });
                            }
                        }
                    }
                }
            }
        }

        Console.WriteLine($"Total Markdown Entries: {markdownEntries.Count}");
        Console.WriteLine($"Total Database Entries: {databaseLogs.Count()}");

        var missingInDb = new List<AuditEntry>();
        var matchedKeys = new HashSet<string>();

        foreach (var entry in markdownEntries)
        {
            var key = $"{entry.Date:yyyy-MM-dd}_{entry.EmployeeId}_{entry.OriginalDetails}";
            if (!dbLookup.ContainsKey(key))
            {
                missingInDb.Add(entry);
            }
            else
            {
                matchedKeys.Add(key);
            }
        }

        var extraInDb = databaseLogs
            .Where(l => !string.IsNullOrEmpty(l.OriginalDetails))
            .Where(l => !matchedKeys.Contains($"{l.LogDate:yyyy-MM-dd}_{l.EmployeeId}_{l.OriginalDetails}"))
            .ToList();

        Console.WriteLine("\n--- Audit Results ---");
        
        if (missingInDb.Any())
        {
            Console.WriteLine($"\n[MISSING IN DATABASE] ({missingInDb.Count} items):");
            foreach (var m in missingInDb.Take(20))
                Console.WriteLine($"  - {m.Date:yyyy-MM-dd} | {m.Initials} | {m.OriginalDetails} (from {m.SourceFile})");
            if (missingInDb.Count > 20) Console.WriteLine("  ... and more");
        }
        else
        {
            Console.WriteLine("\n[OK] All markdown entries found in database.");
        }

        if (extraInDb.Any())
        {
            Console.WriteLine($"\n[EXTRA IN DATABASE / NOT IN MARKDOWN] ({extraInDb.Count} items):");
            foreach (var e in extraInDb.Take(20))
                Console.WriteLine($"  - {e.LogDate:yyyy-MM-dd} | {e.Employee?.Name ?? e.EmployeeId.ToString()} | {e.OriginalDetails}");
            if (extraInDb.Count > 20) Console.WriteLine("  ... and more");
        }
    }

    private class AuditEntry
    {
        public DateOnly Date { get; set; }
        public int EmployeeId { get; set; }
        public string Initials { get; set; } = "";
        public string OriginalDetails { get; set; } = "";
        public string SourceFile { get; set; } = "";
    }
}
