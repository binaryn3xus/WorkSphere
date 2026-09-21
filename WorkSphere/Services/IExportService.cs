using WorkSphere.Models;

namespace WorkSphere.Services;

public interface IExportService
{
    Task<string> ExportWorkLogsCsvAsync(WorkLogExportFilter? filter = null);
    Task<string> ExportCompTimeSummaryCsvAsync();
    Task<string> ExportIncidentsCsvAsync();
    Task<string> ExportMarkdownAsync(int year, int month, string format = "Original", int? employeeId = null);
    Task<string> ExportFullBackupJsonAsync();
    Task<ExportResult> RunBackupAsync(string? targetDirectory = null);
    Task<int> RunCliExportAsync(string[]? args = null, string? customOutputPath = null);
    int PruneOldSnapshots(string targetDirectory, int maxHistoryCount);
    string GetDefaultExportDirectory();
}
