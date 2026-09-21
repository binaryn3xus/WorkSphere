namespace WorkSphere.Models;

public record RestoreResult
{
    public bool Success { get; init; }
    public int EmployeesRestored { get; init; }
    public int IncidentsRestored { get; init; }
    public int WorkLogsRestored { get; init; }
    public int WorkLogsSkipped { get; init; }
    public List<string> Messages { get; init; } = [];
    public string? ErrorMessage { get; init; }

    public static RestoreResult Failed(string error) => new() { Success = false, ErrorMessage = error };
    public static RestoreResult Succeeded(int logsRestored, int logsSkipped, int employees = 0, int incidents = 0, List<string>? messages = null) =>
        new()
        {
            Success = true,
            WorkLogsRestored = logsRestored,
            WorkLogsSkipped = logsSkipped,
            EmployeesRestored = employees,
            IncidentsRestored = incidents,
            Messages = messages ?? []
        };
}

public record ServerSnapshotInfo
{
    public string DirectoryName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public DateTime? TimestampUtc { get; init; }
    public bool HasJsonBackup { get; init; }
    public bool HasCsvBackup { get; init; }
    public int MarkdownFileCount { get; init; }
    public long TotalSizeBytes { get; init; }
}
