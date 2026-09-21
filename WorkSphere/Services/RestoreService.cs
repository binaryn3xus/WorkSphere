using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkSphere.Models;

namespace WorkSphere.Services;

public class RestoreService : IRestoreService
{
    private readonly IWorkLogService _workLogService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RestoreService> _logger;

    public RestoreService(
        IWorkLogService workLogService,
        IConfiguration configuration,
        ILogger<RestoreService> logger)
    {
        _workLogService = workLogService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<RestoreResult> RestoreFromJsonAsync(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            return new RestoreResult { Success = false, ErrorMessage = "Snapshot JSON content is empty." };
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var snapshot = JsonSerializer.Deserialize<DatabaseBackupSnapshot>(jsonContent, options);

            if (snapshot == null)
            {
                return new RestoreResult { Success = false, ErrorMessage = "Failed to deserialize snapshot JSON." };
            }

            return await RestoreSnapshotCoreAsync(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while restoring JSON snapshot.");
            return new RestoreResult { Success = false, ErrorMessage = $"Restore failed: {ex.Message}" };
        }
    }

    public async Task<RestoreResult> RestoreFromSnapshotDirectoryAsync(string snapshotDirectoryPath)
    {
        if (!Directory.Exists(snapshotDirectoryPath))
        {
            return new RestoreResult { Success = false, ErrorMessage = $"Snapshot directory not found: {snapshotDirectoryPath}" };
        }

        try
        {
            var fullJsonPath = Path.Combine(snapshotDirectoryPath, "backup-full.json");
            if (File.Exists(fullJsonPath))
            {
                var json = await File.ReadAllTextAsync(fullJsonPath);
                return await RestoreFromJsonAsync(json);
            }

            // Fallback: check for stamped json or worklogs.csv
            var jsonFiles = Directory.GetFiles(snapshotDirectoryPath, "backup-full*.json");
            if (jsonFiles.Length > 0)
            {
                var json = await File.ReadAllTextAsync(jsonFiles[0]);
                return await RestoreFromJsonAsync(json);
            }

            var csvPath = Path.Combine(snapshotDirectoryPath, "worklogs.csv");
            if (File.Exists(csvPath))
            {
                var csv = await File.ReadAllTextAsync(csvPath);
                return await ImportWorkLogsFromCsvAsync(csv);
            }

            return new RestoreResult
            {
                Success = false,
                ErrorMessage = $"No compatible backup files (backup-full.json or worklogs.csv) found in {snapshotDirectoryPath}."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore from snapshot directory: {Path}", snapshotDirectoryPath);
            return new RestoreResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<RestoreResult> ImportWorkLogsFromCsvAsync(string csvContent)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            return new RestoreResult { Success = false, ErrorMessage = "CSV content is empty." };
        }

        try
        {
            var lines = SplitCsvLines(csvContent);
            if (lines.Count < 2)
            {
                return new RestoreResult { Success = false, ErrorMessage = "CSV file must contain a header and at least one data row." };
            }

            var header = ParseCsvRow(lines[0]);
            var headerMap = header
                .Select((name, idx) => (Name: name.Trim(), Index: idx))
                .ToDictionary(h => h.Name, h => h.Index, StringComparer.OrdinalIgnoreCase);

            // Verify essential columns
            if (!headerMap.ContainsKey("Date") || !headerMap.ContainsKey("Details"))
            {
                return new RestoreResult { Success = false, ErrorMessage = "CSV missing required 'Date' or 'Details' columns." };
            }

            var employees = (await _workLogService.GetEmployeesAsync()).ToList();
            var incidents = (await _workLogService.GetIncidentsAsync()).ToList();
            var existingLogs = (await _workLogService.GetWorkLogsAsync()).ToList();
            var existingKeys = existingLogs.Select(GetLogKey).ToHashSet();

            int logsRestored = 0;
            int logsSkipped = 0;
            int employeesCreated = 0;

            for (int i = 1; i < lines.Count; i++)
            {
                var row = ParseCsvRow(lines[i]);
                if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace)) continue;

                // Resolve Employee
                var empName = GetCol(row, headerMap, "EmployeeName");
                var empInitials = GetCol(row, headerMap, "Initials");
                var empIdStr = GetCol(row, headerMap, "EmployeeId");

                var employee = employees.FirstOrDefault(e =>
                    (!string.IsNullOrEmpty(empInitials) && e.Initials.Equals(empInitials, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(empName) && e.Name.Equals(empName, StringComparison.OrdinalIgnoreCase)) ||
                    (int.TryParse(empIdStr, out var eid) && e.Id == eid));

                if (employee == null && (!string.IsNullOrEmpty(empName) || !string.IsNullOrEmpty(empInitials)))
                {
                    var newEmpName = !string.IsNullOrEmpty(empName) ? empName : empInitials!;
                    var newEmpInitials = !string.IsNullOrEmpty(empInitials) ? empInitials : (newEmpName.Length > 2 ? newEmpName[..2] : newEmpName);

                    await _workLogService.AddEmployeeAsync(new Employee
                    {
                        Name = newEmpName,
                        Initials = newEmpInitials
                    });
                    employeesCreated++;

                    employees = (await _workLogService.GetEmployeesAsync()).ToList();
                    employee = employees.FirstOrDefault(e => e.Name.Equals(newEmpName, StringComparison.OrdinalIgnoreCase));
                }

                if (employee == null)
                {
                    logsSkipped++;
                    continue;
                }

                // Resolve Incident
                int? incidentId = null;
                var incTicket = GetCol(row, headerMap, "IncidentTicket");
                var incIdStr = GetCol(row, headerMap, "IncidentId");
                if (!string.IsNullOrWhiteSpace(incTicket))
                {
                    var inc = incidents.FirstOrDefault(inObj => inObj.TicketNumber.Equals(incTicket, StringComparison.OrdinalIgnoreCase));
                    if (inc != null) incidentId = inc.Id;
                }
                else if (int.TryParse(incIdStr, out var parsedIncId))
                {
                    var inc = incidents.FirstOrDefault(inObj => inObj.Id == parsedIncId);
                    if (inc != null) incidentId = inc.Id;
                }

                // Parse Date & Time
                var dateStr = GetCol(row, headerMap, "Date");
                DateOnly? logDate = null;
                if (DateOnly.TryParse(dateStr, CultureInfo.InvariantCulture, out var pd)) logDate = pd;
                else if (DateOnly.TryParse(dateStr, out var pdLocal)) logDate = pdLocal;

                var timeStr = GetCol(row, headerMap, "Time");
                TimeOnly? logTime = null;
                if (TimeOnly.TryParse(timeStr, CultureInfo.InvariantCulture, out var pt)) logTime = pt;
                else if (TimeOnly.TryParse(timeStr, out var ptLocal)) logTime = ptLocal;

                var details = GetCol(row, headerMap, "Details");
                var mainCat = GetCol(row, headerMap, "MainCategory");
                var subCat = GetCol(row, headerMap, "SubCategory");

                bool.TryParse(GetCol(row, headerMap, "EarnsCompTime"), out var earnsComp);
                bool.TryParse(GetCol(row, headerMap, "UsesCompTime"), out var usesComp);
                decimal.TryParse(GetCol(row, headerMap, "Hours"), CultureInfo.InvariantCulture, out var hours);

                var newLog = new WorkLog
                {
                    EmployeeId = employee.Id,
                    IncidentId = incidentId,
                    LogDate = logDate,
                    LogTime = logTime,
                    MainCategory = !string.IsNullOrEmpty(mainCat) ? mainCat : "Work",
                    SubCategory = !string.IsNullOrEmpty(subCat) ? subCat : "General",
                    Details = details,
                    OriginalDetails = details,
                    EarnsCompTime = earnsComp,
                    UsesCompTime = usesComp,
                    Hours = hours
                };

                var key = GetLogKey(newLog);
                if (!existingKeys.Contains(key))
                {
                    await _workLogService.AddWorkLogAsync(newLog);
                    existingKeys.Add(key);
                    logsRestored++;
                }
                else
                {
                    logsSkipped++;
                }
            }

            return new RestoreResult
            {
                Success = true,
                EmployeesRestored = employeesCreated,
                WorkLogsRestored = logsRestored,
                WorkLogsSkipped = logsSkipped,
                Messages = [$"Imported {logsRestored} work logs ({logsSkipped} duplicates skipped). Created {employeesCreated} employees."]
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import work logs from CSV.");
            return new RestoreResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public List<ServerSnapshotInfo> GetAvailableServerSnapshots()
    {
        var targetDir = _configuration["Export:OutputPath"]
            ?? Environment.GetEnvironmentVariable("EXPORT_PATH")
            ?? "Exports";

        if (!Directory.Exists(targetDir))
        {
            return [];
        }

        var results = new List<ServerSnapshotInfo>();
        var dirs = Directory.GetDirectories(targetDir, "snapshot-*");

        foreach (var dir in dirs)
        {
            var dirInfo = new DirectoryInfo(dir);
            DateTime? ts = null;
            if (dirInfo.Name.StartsWith("snapshot-") && dirInfo.Name.Length >= 24)
            {
                var tsStr = dirInfo.Name.Substring("snapshot-".Length);
                if (DateTime.TryParseExact(tsStr, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    ts = parsed;
                }
            }

            var hasJson = File.Exists(Path.Combine(dir, "backup-full.json")) || Directory.GetFiles(dir, "backup-full*.json").Length > 0;
            var hasCsv = File.Exists(Path.Combine(dir, "worklogs.csv")) || Directory.GetFiles(dir, "worklogs*.csv").Length > 0;

            var mdDir = Path.Combine(dir, "Markdown");
            var mdCount = Directory.Exists(mdDir) ? Directory.GetFiles(mdDir, "*.md").Length : 0;

            long totalBytes = 0;
            try
            {
                totalBytes = dirInfo.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch
            {
                // ignore permission/locking errors on size calculation
            }

            results.Add(new ServerSnapshotInfo
            {
                DirectoryName = dirInfo.Name,
                FullPath = dirInfo.FullName,
                TimestampUtc = ts,
                HasJsonBackup = hasJson,
                HasCsvBackup = hasCsv,
                MarkdownFileCount = mdCount,
                TotalSizeBytes = totalBytes
            });
        }

        return results.OrderByDescending(s => s.DirectoryName).ToList();
    }

    private async Task<RestoreResult> RestoreSnapshotCoreAsync(DatabaseBackupSnapshot snapshot)
    {
        int employeesRestored = 0;
        int incidentsRestored = 0;
        int workLogsRestored = 0;
        int workLogsSkipped = 0;

        // 1. Employees: match or create and build ID map
        var existingEmployees = (await _workLogService.GetEmployeesAsync()).ToList();
        var oldEmpIdToNewId = new Dictionary<int, int>();

        foreach (var emp in snapshot.Employees)
        {
            var match = existingEmployees.FirstOrDefault(e =>
                e.Name.Equals(emp.Name, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(emp.Initials) && e.Initials.Equals(emp.Initials, StringComparison.OrdinalIgnoreCase)));

            if (match == null)
            {
                await _workLogService.AddEmployeeAsync(new Employee
                {
                    Name = emp.Name,
                    Initials = emp.Initials
                });
                employeesRestored++;
            }
        }

        existingEmployees = (await _workLogService.GetEmployeesAsync()).ToList();
        foreach (var emp in snapshot.Employees)
        {
            var match = existingEmployees.FirstOrDefault(e =>
                e.Name.Equals(emp.Name, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(emp.Initials) && e.Initials.Equals(emp.Initials, StringComparison.OrdinalIgnoreCase)));

            if (match != null)
            {
                oldEmpIdToNewId[emp.Id] = match.Id;
            }
        }

        // 2. Incidents: match or create and build ID map
        var existingIncidents = (await _workLogService.GetIncidentsAsync()).ToList();
        var oldIncidentIdToNewId = new Dictionary<int, int>();

        foreach (var inc in snapshot.Incidents)
        {
            var match = existingIncidents.FirstOrDefault(i =>
                (!string.IsNullOrEmpty(inc.TicketNumber) && i.TicketNumber.Equals(inc.TicketNumber, StringComparison.OrdinalIgnoreCase)) ||
                i.Title.Equals(inc.Title, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                await _workLogService.AddIncidentAsync(new Incident
                {
                    TicketNumber = inc.TicketNumber,
                    Title = inc.Title,
                    Details = inc.Details,
                    StartedAt = inc.StartedAt,
                    EndedAt = inc.EndedAt,
                    IsClosed = inc.IsClosed
                });
                incidentsRestored++;
            }
        }

        existingIncidents = (await _workLogService.GetIncidentsAsync()).ToList();
        foreach (var inc in snapshot.Incidents)
        {
            var match = existingIncidents.FirstOrDefault(i =>
                (!string.IsNullOrEmpty(inc.TicketNumber) && i.TicketNumber.Equals(inc.TicketNumber, StringComparison.OrdinalIgnoreCase)) ||
                i.Title.Equals(inc.Title, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                oldIncidentIdToNewId[inc.Id] = match.Id;
            }
        }

        // 3. WorkLogs: Remap IDs, deduplicate against existing, and insert
        var existingLogs = (await _workLogService.GetWorkLogsAsync()).ToList();
        var existingLogKeys = existingLogs.Select(GetLogKey).ToHashSet();

        foreach (var log in snapshot.WorkLogs)
        {
            if (!oldEmpIdToNewId.TryGetValue(log.EmployeeId, out var newEmployeeId))
            {
                continue;
            }

            int? newIncidentId = null;
            if (log.IncidentId.HasValue && oldIncidentIdToNewId.TryGetValue(log.IncidentId.Value, out var mappedIncId))
            {
                newIncidentId = mappedIncId;
            }

            var candidate = new WorkLog
            {
                EmployeeId = newEmployeeId,
                IncidentId = newIncidentId,
                LogDate = log.LogDate,
                LogTime = log.LogTime,
                MainCategory = log.MainCategory,
                SubCategory = log.SubCategory,
                Details = log.Details,
                OriginalDetails = log.OriginalDetails,
                EarnsCompTime = log.EarnsCompTime,
                UsesCompTime = log.UsesCompTime,
                Hours = log.Hours
            };

            var key = GetLogKey(candidate);
            if (!existingLogKeys.Contains(key))
            {
                await _workLogService.AddWorkLogAsync(candidate);
                existingLogKeys.Add(key);
                workLogsRestored++;
            }
            else
            {
                workLogsSkipped++;
            }
        }

        return new RestoreResult
        {
            Success = true,
            EmployeesRestored = employeesRestored,
            IncidentsRestored = incidentsRestored,
            WorkLogsRestored = workLogsRestored,
            WorkLogsSkipped = workLogsSkipped,
            Messages =
            [
                $"Restored {workLogsRestored} work logs ({workLogsSkipped} duplicates skipped).",
                $"Restored {employeesRestored} employees and {incidentsRestored} incidents."
            ]
        };
    }

    private static string GetLogKey(WorkLog log)
    {
        var dateStr = log.LogDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "";
        var timeStr = log.LogTime?.ToString("HHmm", CultureInfo.InvariantCulture) ?? "";
        var details = (log.Details ?? log.OriginalDetails ?? "").Trim();
        var main = (log.MainCategory ?? "").Trim();
        var sub = (log.SubCategory ?? "").Trim();

        return $"{log.EmployeeId}_{dateStr}_{timeStr}_{details}_{main}_{sub}";
    }

    private static string GetCol(List<string> row, Dictionary<string, int> headerMap, string colName)
    {
        if (headerMap.TryGetValue(colName, out var idx) && idx < row.Count)
        {
            return row[idx].Trim();
        }
        return string.Empty;
    }

    private static List<string> SplitCsvLines(string csvContent)
    {
        var lines = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < csvContent.Length; i++)
        {
            char c = csvContent[i];
            if (c == '\"')
            {
                inQuotes = !inQuotes;
                current.Append(c);
            }
            else if ((c == '\n' || c == '\r') && !inQuotes)
            {
                if (c == '\r' && i + 1 < csvContent.Length && csvContent[i + 1] == '\n')
                {
                    i++;
                }
                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    private static List<string> ParseCsvRow(string row)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < row.Length; i++)
        {
            char c = row[i];
            if (c == '\"')
            {
                if (inQuotes && i + 1 < row.Length && row[i + 1] == '\"')
                {
                    current.Append('\"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
