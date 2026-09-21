using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using WorkSphere.Models;
using WorkSphere.Services;
using Xunit;

namespace WorkSphere.Tests;

public class ExportServiceTests : IDisposable
{
    private readonly string _tempOutputDir;
    private readonly Mock<IWorkLogService> _mockWorkLogService;
    private readonly Mock<ILogger<ExportService>> _mockLogger;
    private readonly IConfiguration _configuration;

    public ExportServiceTests()
    {
        _tempOutputDir = Path.Combine(Path.GetTempPath(), $"worksphere_export_test_{Guid.NewGuid()}");
        _mockWorkLogService = new Mock<IWorkLogService>();
        _mockLogger = new Mock<ILogger<ExportService>>();

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Export:OutputPath", _tempOutputDir }
            })
            .Build();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempOutputDir))
        {
            try
            {
                Directory.Delete(_tempOutputDir, true);
            }
            catch
            {
                // Ignore cleanup errors in temp
            }
        }
    }

    private ExportService CreateService()
    {
        return new ExportService(_mockWorkLogService.Object, _configuration, _mockLogger.Object);
    }

    [Fact]
    public async Task ExportWorkLogsCsvAsync_EscapesCommasAndQuotesProperly()
    {
        // Arrange
        var employee = new Employee { Id = 1, Name = "Alice \"Special\" Smith", Initials = "AS" };
        var logs = new List<WorkLog>
        {
            new()
            {
                Id = 101,
                EmployeeId = 1,
                Employee = employee,
                LogDate = new DateOnly(2026, 5, 10),
                LogTime = new TimeOnly(9, 30),
                MainCategory = "Development",
                SubCategory = "Frontend",
                EarnsCompTime = true,
                UsesCompTime = false,
                Hours = 2.5m,
                IncidentId = 42,
                Incident = new Incident { Id = 42, TicketNumber = "INC-1001", Title = "Outage" },
                Details = "Investigated issue, resolved quickly, and deployed \"patch\"."
            }
        };

        _mockWorkLogService.Setup(s => s.GetWorkLogsAsync()).ReturnsAsync(logs);
        var service = CreateService();

        // Act
        var csv = await service.ExportWorkLogsCsvAsync();

        // Assert
        Assert.NotNull(csv);
        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 2);
        Assert.StartsWith("Id,Date,Time,EmployeeId,EmployeeName,Initials", lines[0]);

        var dataLine = lines[1];
        Assert.Contains("\"Alice \"\"Special\"\" Smith\"", dataLine);
        Assert.Contains("\"Investigated issue, resolved quickly, and deployed \"\"patch\"\".\"", dataLine);
        Assert.Contains("INC-1001", dataLine);
    }

    [Fact]
    public async Task ExportWorkLogsCsvAsync_AppliesFilterCriteriaCorrectly()
    {
        // Arrange
        var emp1 = new Employee { Id = 1, Name = "Alice", Initials = "A" };
        var emp2 = new Employee { Id = 2, Name = "Bob", Initials = "B" };

        var logs = new List<WorkLog>
        {
            new() { Id = 1, EmployeeId = 1, Employee = emp1, LogDate = new DateOnly(2026, 1, 15), EarnsCompTime = true, Hours = 1 },
            new() { Id = 2, EmployeeId = 2, Employee = emp2, LogDate = new DateOnly(2026, 2, 10), EarnsCompTime = false, Hours = 0 },
            new() { Id = 3, EmployeeId = 1, Employee = emp1, LogDate = new DateOnly(2026, 3, 5), EarnsCompTime = false, Hours = 0 }
        };

        _mockWorkLogService.Setup(s => s.GetWorkLogsAsync()).ReturnsAsync(logs);
        var service = CreateService();

        // Filter: Employee 1 and CompTime only
        var filter = new WorkLogExportFilter
        {
            EmployeeId = 1,
            CompTimeOnly = true
        };

        // Act
        var csv = await service.ExportWorkLogsCsvAsync(filter);
        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        // Assert
        // Header + 1 record
        Assert.Equal(2, lines.Length);
        Assert.Contains("Alice", lines[1]);
        Assert.DoesNotContain("Bob", csv);
    }

    [Fact]
    public async Task ExportCompTimeSummaryCsvAsync_GeneratesCorrectColumns()
    {
        // Arrange
        var stats = new List<CompTimeBalanceDto>
        {
            new(1, "Alice", 10.0m, 4.0m, 6.0m),
            new(2, "Bob", 2.0m, 2.0m, 0.0m)
        };
        var employees = new List<Employee>
        {
            new() { Id = 1, Name = "Alice", Initials = "AS" },
            new() { Id = 2, Name = "Bob", Initials = "BB" }
        };

        _mockWorkLogService.Setup(s => s.GetCompTimeStatsAsync()).ReturnsAsync(stats);
        _mockWorkLogService.Setup(s => s.GetEmployeesAsync()).ReturnsAsync(employees);
        var service = CreateService();

        // Act
        var csv = await service.ExportCompTimeSummaryCsvAsync();

        // Assert
        Assert.Contains("EmployeeId,EmployeeName,Initials,EarnedHours,UsedHours,BalanceHours", csv);
        Assert.Contains("1,Alice,AS,10.0,4.0,6.0", csv);
        Assert.Contains("2,Bob,BB,2.0,2.0,0.0", csv);
    }

    [Fact]
    public async Task ExportMarkdownAsync_GeneratesValidObsidianTable()
    {
        // Arrange
        var employee = new Employee { Id = 1, Name = "Charlie", Initials = "CH" };
        var logs = new List<WorkLog>
        {
            new()
            {
                Id = 1,
                EmployeeId = 1,
                Employee = employee,
                LogDate = new DateOnly(2026, 4, 15),
                LogTime = new TimeOnly(8, 0),
                Details = "Morning standup meeting"
            }
        };

        _mockWorkLogService.Setup(s => s.GetWorkLogsByMonthAsync(2026, 4)).ReturnsAsync(logs);
        var service = CreateService();

        // Act
        var originalMd = await service.ExportMarkdownAsync(2026, 4, "Original");
        var extendedMd = await service.ExportMarkdownAsync(2026, 4, "Extended");

        // Assert
        Assert.Contains("# April 2026 (Original Format)", originalMd);
        Assert.Contains("## Subject Charlie", originalMd);
        Assert.Contains("| Day      | Time  | Subject | Details", originalMd);
        Assert.Contains("Morning standup meeting", originalMd);

        Assert.Contains("# April 2026 (Extended Format)", extendedMd);
        Assert.Contains("| Day      | Time  | Subject | Category            | Comp   | Incident | Details", extendedMd);
    }

    [Fact]
    public async Task ExportFullBackupJsonAsync_SerializesAndIsDeserializable()
    {
        // Arrange
        var employees = new List<Employee> { new() { Id = 1, Name = "Alice", Initials = "A" } };
        var incidents = new List<Incident> { new() { Id = 10, TicketNumber = "INC-1", Title = "DB Down" } };
        var logs = new List<WorkLog> { new() { Id = 100, EmployeeId = 1, LogDate = new DateOnly(2026, 6, 1) } };

        _mockWorkLogService.Setup(s => s.GetEmployeesAsync()).ReturnsAsync(employees);
        _mockWorkLogService.Setup(s => s.GetIncidentsAsync()).ReturnsAsync(incidents);
        _mockWorkLogService.Setup(s => s.GetWorkLogsAsync()).ReturnsAsync(logs);

        var service = CreateService();

        // Act
        var json = await service.ExportFullBackupJsonAsync();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(json));
        var snapshot = JsonSerializer.Deserialize<DatabaseBackupSnapshot>(json);
        Assert.NotNull(snapshot);
        Assert.Equal("1.0", snapshot.Version);
        Assert.Single(snapshot.Employees);
        Assert.Single(snapshot.Incidents);
        Assert.Single(snapshot.WorkLogs);
        Assert.Equal("Alice", snapshot.Employees[0].Name);
    }

    [Fact]
    public async Task RunBackupAsync_CreatesExpectedFilesAndDirectories()
    {
        // Arrange
        var employee = new Employee { Id = 1, Name = "Alice", Initials = "A" };
        var logs = new List<WorkLog>
        {
            new() { Id = 1, EmployeeId = 1, Employee = employee, LogDate = new DateOnly(2026, 5, 1) }
        };

        _mockWorkLogService.Setup(s => s.GetEmployeesAsync()).ReturnsAsync(new List<Employee> { employee });
        _mockWorkLogService.Setup(s => s.GetIncidentsAsync()).ReturnsAsync(new List<Incident>());
        _mockWorkLogService.Setup(s => s.GetWorkLogsAsync()).ReturnsAsync(logs);
        _mockWorkLogService.Setup(s => s.GetCompTimeStatsAsync()).ReturnsAsync(new List<CompTimeBalanceDto>());
        _mockWorkLogService.Setup(s => s.GetIncidentStatsAsync()).ReturnsAsync(new List<IncidentViewModel>());
        _mockWorkLogService.Setup(s => s.GetWorkLogsByMonthAsync(2026, 5)).ReturnsAsync(logs);

        var service = CreateService();

        // Act
        var result = await service.RunBackupAsync(_tempOutputDir);

        // Assert
        Assert.True(result.Success);
        Assert.True(Directory.Exists(result.OutputDirectory));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "backup-full.json")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "worklogs.csv")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "comptime-summary.csv")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "incidents.csv")));
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "Markdown", "2026-05.md")));

        // Verify latest directory is NOT created
        var latestDir = Path.Combine(_tempOutputDir, "latest");
        Assert.False(Directory.Exists(latestDir));
    }

    [Fact]
    public void PruneOldSnapshots_DeletesExcessSnapshotsKeepingConfiguredCount()
    {
        // Arrange
        var service = CreateService();
        var testDir = Path.Combine(_tempOutputDir, "prune_test");
        Directory.CreateDirectory(testDir);

        // Create 5 fake snapshot directories with timestamps
        for (int i = 1; i <= 5; i++)
        {
            var dirName = $"snapshot-202609{i:D2}-040000";
            Directory.CreateDirectory(Path.Combine(testDir, dirName));
        }

        // Act: Keep only newest 3
        var pruned = service.PruneOldSnapshots(testDir, 3);

        // Assert
        Assert.Equal(2, pruned);
        var remaining = Directory.GetDirectories(testDir, "snapshot-*")
            .Select(Path.GetFileName)
            .OrderByDescending(n => n)
            .ToList();

        Assert.Equal(3, remaining.Count);
        Assert.Equal("snapshot-20260905-040000", remaining[0]);
        Assert.Equal("snapshot-20260904-040000", remaining[1]);
        Assert.Equal("snapshot-20260903-040000", remaining[2]);

        // When maxHistoryCount is 0, nothing is deleted (unlimited)
        var noPrune = service.PruneOldSnapshots(testDir, 0);
        Assert.Equal(0, noPrune);
        Assert.Equal(3, Directory.GetDirectories(testDir, "snapshot-*").Length);
    }

    [Fact]
    public void CalculateNextRunUtc_CalculatesCorrectCronSchedule()
    {
        // Case 1: Standard daily 02:00 UTC ("0 2 * * *") before 2 AM
        var fixedTimeBefore = new DateTime(2026, 5, 10, 1, 15, 0, DateTimeKind.Utc);
        var nextRunSameDay = ExportBackgroundService.CalculateNextRunUtc("0 2 * * *", fixedTimeBefore);
        Assert.Equal(new DateTime(2026, 5, 10, 2, 0, 0, DateTimeKind.Utc), nextRunSameDay);

        // Case 2: Standard daily 02:00 UTC ("0 2 * * *") after 2 AM
        var fixedTimeAfter = new DateTime(2026, 5, 10, 3, 30, 0, DateTimeKind.Utc);
        var nextRunNextDay = ExportBackgroundService.CalculateNextRunUtc("0 2 * * *", fixedTimeAfter);
        Assert.Equal(new DateTime(2026, 5, 11, 2, 0, 0, DateTimeKind.Utc), nextRunNextDay);

        // Case 3: Every 15 minutes ("*/15 * * * *")
        var fixedTimeMinute = new DateTime(2026, 5, 10, 10, 7, 0, DateTimeKind.Utc);
        var nextRun15Min = ExportBackgroundService.CalculateNextRunUtc("*/15 * * * *", fixedTimeMinute);
        Assert.Equal(new DateTime(2026, 5, 10, 10, 15, 0, DateTimeKind.Utc), nextRun15Min);

        // Case 4: Invalid cron expression gracefully falls back to default 2 AM
        var fallbackRun = ExportBackgroundService.CalculateNextRunUtc("invalid-cron-string", fixedTimeBefore);
        Assert.Equal(new DateTime(2026, 5, 10, 2, 0, 0, DateTimeKind.Utc), fallbackRun);

        // Case 5: 6-part cron expression with seconds ("0 0 2 * * *")
        var nextRun6Part = ExportBackgroundService.CalculateNextRunUtc("0 0 2 * * *", fixedTimeBefore);
        Assert.Equal(new DateTime(2026, 5, 10, 2, 0, 0, DateTimeKind.Utc), nextRun6Part);
    }

    [Fact]
    public async Task ExportBackgroundService_ExecuteScheduledBackupAsync_UpdatesStateCorrectly()
    {
        // Arrange
        var schedulerState = new BackupSchedulerState();
        var mockExportService = new Mock<IExportService>();
        mockExportService.Setup(e => e.RunBackupAsync(It.IsAny<string?>()))
            .ReturnsAsync(new ExportResult
            {
                Success = true,
                FilesWritten = 5,
                OutputDirectory = "/dummy/exports"
            });

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped<IExportService>(_ => mockExportService.Object);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var bgLogger = new Mock<ILogger<ExportBackgroundService>>();
        var bgConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Export:ScheduledBackup:Enabled", "true" },
                { "Export:ScheduledBackup:CronExpression", "0 2 * * *" }
            })
            .Build();

        var bgService = new ExportBackgroundService(scopeFactory, schedulerState, bgConfig, bgLogger.Object);

        // Act
        await bgService.ExecuteScheduledBackupAsync();

        // Assert
        Assert.True(schedulerState.LastRunSuccess);
        Assert.NotNull(schedulerState.LastRunUtc);
        Assert.Contains("5 files written to /dummy/exports", schedulerState.LastRunMessage);
    }
}
