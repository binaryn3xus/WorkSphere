namespace WorkSphere.Models;

public record WorkLogExportFilter
{
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public int? EmployeeId { get; init; }
    public string? MainCategory { get; init; }
    public bool? CompTimeOnly { get; init; }
}

public record DatabaseBackupSnapshot
{
    public DateTime ExportedAtUtc { get; init; } = DateTime.UtcNow;
    public string Version { get; init; } = "1.0";
    public List<Employee> Employees { get; init; } = [];
    public List<Incident> Incidents { get; init; } = [];
    public List<WorkLog> WorkLogs { get; init; } = [];
}

public record ExportResult
{
    public bool Success { get; init; }
    public string OutputDirectory { get; init; } = string.Empty;
    public int FilesWritten { get; init; }
    public List<string> FileNames { get; init; } = [];
    public string? ErrorMessage { get; init; }
}

public record ScheduledBackupOptions
{
    public bool Enabled { get; init; } = true;
    public string CronExpression { get; init; } = "0 2 * * *";
    public int? MaxBackupHistory { get; init; } = 60;
}

public class BackupSchedulerState
{
    public bool IsEnabled { get; set; } = true;
    public string CronExpression { get; set; } = "0 2 * * *";
    public DateTime? LastRunUtc { get; set; }
    public bool? LastRunSuccess { get; set; }
    public string? LastRunMessage { get; set; }
    public DateTime? NextRunUtc { get; set; }
    public bool IsRunningNow { get; set; }
}
