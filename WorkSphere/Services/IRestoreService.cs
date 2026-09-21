using WorkSphere.Models;

namespace WorkSphere.Services;

public interface IRestoreService
{
    Task<RestoreResult> RestoreFromJsonAsync(string jsonContent);
    Task<RestoreResult> RestoreFromSnapshotDirectoryAsync(string snapshotDirectoryPath);
    Task<RestoreResult> ImportWorkLogsFromCsvAsync(string csvContent);
    List<ServerSnapshotInfo> GetAvailableServerSnapshots();
}
