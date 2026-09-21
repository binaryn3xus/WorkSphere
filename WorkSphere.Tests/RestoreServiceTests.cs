using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using WorkSphere.Models;
using WorkSphere.Services;
using Xunit;

namespace WorkSphere.Tests;

public class RestoreServiceTests : IDisposable
{
    private readonly string _tempOutputDir;
    private readonly Mock<IWorkLogService> _mockWorkLogService;
    private readonly Mock<ILogger<RestoreService>> _mockLogger;
    private readonly IConfiguration _configuration;
    private readonly RestoreService _service;

    public RestoreServiceTests()
    {
        _tempOutputDir = Path.Combine(Path.GetTempPath(), $"worksphere_restore_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempOutputDir);

        _mockWorkLogService = new Mock<IWorkLogService>();
        _mockLogger = new Mock<ILogger<RestoreService>>();

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Export:OutputPath", _tempOutputDir }
            })
            .Build();

        _service = new RestoreService(
            _mockWorkLogService.Object,
            _configuration,
            _mockLogger.Object);
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
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public async Task RestoreFromJsonAsync_EmptyOrInvalidJson_ReturnsFailedResult()
    {
        var emptyResult = await _service.RestoreFromJsonAsync("");
        Assert.False(emptyResult.Success);
        Assert.Contains("empty", emptyResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        var invalidResult = await _service.RestoreFromJsonAsync("{ invalid json }");
        Assert.False(invalidResult.Success);
        Assert.Contains("failed", invalidResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreFromJsonAsync_ValidSnapshot_CreatesEntitiesAndRemapsIds()
    {
        // Arrange
        var existingEmployees = new List<Employee>
        {
            new() { Id = 1, Name = "Alice Smith", Initials = "AS" }
        };
        var employeesList = new List<Employee>(existingEmployees);

        _mockWorkLogService.Setup(s => s.GetEmployeesAsync())
            .ReturnsAsync(() => employeesList);

        _mockWorkLogService.Setup(s => s.AddEmployeeAsync(It.IsAny<Employee>()))
            .Returns<Employee>(e =>
            {
                var newEmp = new Employee { Id = employeesList.Count + 1, Name = e.Name, Initials = e.Initials };
                employeesList.Add(newEmp);
                return Task.CompletedTask;
            });

        var incidentsList = new List<Incident>();
        _mockWorkLogService.Setup(s => s.GetIncidentsAsync())
            .ReturnsAsync(() => incidentsList);

        _mockWorkLogService.Setup(s => s.AddIncidentAsync(It.IsAny<Incident>()))
            .Returns<Incident>(i =>
            {
                var newInc = new Incident
                {
                    Id = incidentsList.Count + 10,
                    TicketNumber = i.TicketNumber,
                    Title = i.Title,
                    Details = i.Details
                };
                incidentsList.Add(newInc);
                return Task.CompletedTask;
            });

        var existingLogs = new List<WorkLog>();
        var insertedLogs = new List<WorkLog>();
        _mockWorkLogService.Setup(s => s.GetWorkLogsAsync())
            .ReturnsAsync(() => existingLogs);

        _mockWorkLogService.Setup(s => s.AddWorkLogAsync(It.IsAny<WorkLog>()))
            .Returns<WorkLog>(wl =>
            {
                insertedLogs.Add(wl);
                return Task.CompletedTask;
            });

        var snapshot = new DatabaseBackupSnapshot
        {
            Employees =
            [
                new() { Id = 99, Name = "Alice Smith", Initials = "AS" }, // existing
                new() { Id = 100, Name = "Bob Jones", Initials = "BJ" }    // new
            ],
            Incidents =
            [
                new() { Id = 500, TicketNumber = "INC-12345", Title = "Server Outage" }
            ],
            WorkLogs =
            [
                new()
                {
                    EmployeeId = 100, // Bob Jones -> should remap to 2
                    IncidentId = 500, // INC-12345 -> should remap to 10
                    LogDate = new DateOnly(2026, 9, 21),
                    LogTime = new TimeOnly(10, 30),
                    MainCategory = "Infrastructure",
                    SubCategory = "Outage",
                    Details = "Resolved DNS failure",
                    Hours = 1.5m
                },
                new()
                {
                    EmployeeId = 99, // Alice Smith -> should remap to 1
                    LogDate = new DateOnly(2026, 9, 21),
                    LogTime = new TimeOnly(12, 0),
                    MainCategory = "Meeting",
                    SubCategory = "Standup",
                    Details = "Daily team sync",
                    Hours = 0.5m
                }
            ]
        };

        var json = JsonSerializer.Serialize(snapshot);

        // Act
        var result = await _service.RestoreFromJsonAsync(json);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.EmployeesRestored); // Bob created
        Assert.Equal(1, result.IncidentsRestored); // INC-12345 created
        Assert.Equal(2, result.WorkLogsRestored);
        Assert.Equal(0, result.WorkLogsSkipped);

        Assert.Equal(2, insertedLogs.Count);
        // Verify Bob's log remapping
        var bobsLog = insertedLogs.First(l => l.Details == "Resolved DNS failure");
        Assert.Equal(2, bobsLog.EmployeeId);
        Assert.Equal(10, bobsLog.IncidentId);

        // Verify Alice's log remapping
        var alicesLog = insertedLogs.First(l => l.Details == "Daily team sync");
        Assert.Equal(1, alicesLog.EmployeeId);
        Assert.Null(alicesLog.IncidentId);
    }

    [Fact]
    public async Task RestoreFromJsonAsync_DuplicateWorkLogs_SkipsDuplicates()
    {
        // Arrange
        var employees = new List<Employee>
        {
            new() { Id = 1, Name = "Alice Smith", Initials = "AS" }
        };
        _mockWorkLogService.Setup(s => s.GetEmployeesAsync()).ReturnsAsync(employees);
        _mockWorkLogService.Setup(s => s.GetIncidentsAsync()).ReturnsAsync(new List<Incident>());

        // Database already has this exact log
        var existingLogs = new List<WorkLog>
        {
            new()
            {
                EmployeeId = 1,
                LogDate = new DateOnly(2026, 9, 21),
                LogTime = new TimeOnly(9, 0),
                MainCategory = "Work",
                SubCategory = "Dev",
                Details = "Already exists in database"
            }
        };
        _mockWorkLogService.Setup(s => s.GetWorkLogsAsync()).ReturnsAsync(existingLogs);

        var snapshot = new DatabaseBackupSnapshot
        {
            Employees = [new() { Id = 1, Name = "Alice Smith", Initials = "AS" }],
            WorkLogs =
            [
                new()
                {
                    EmployeeId = 1,
                    LogDate = new DateOnly(2026, 9, 21),
                    LogTime = new TimeOnly(9, 0),
                    MainCategory = "Work",
                    SubCategory = "Dev",
                    Details = "Already exists in database"
                },
                new()
                {
                    EmployeeId = 1,
                    LogDate = new DateOnly(2026, 9, 21),
                    LogTime = new TimeOnly(14, 0),
                    MainCategory = "Work",
                    SubCategory = "Dev",
                    Details = "Brand new log"
                }
            ]
        };

        var insertedLogs = new List<WorkLog>();
        _mockWorkLogService.Setup(s => s.AddWorkLogAsync(It.IsAny<WorkLog>()))
            .Returns<WorkLog>(wl =>
            {
                insertedLogs.Add(wl);
                return Task.CompletedTask;
            });

        // Act
        var result = await _service.RestoreFromJsonAsync(JsonSerializer.Serialize(snapshot));

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.WorkLogsRestored);
        Assert.Equal(1, result.WorkLogsSkipped);
        Assert.Single(insertedLogs);
        Assert.Equal("Brand new log", insertedLogs[0].Details);
    }

    [Fact]
    public async Task ImportWorkLogsFromCsvAsync_ValidCsv_ImportsAndCreatesEmployees()
    {
        // Arrange
        var employeesList = new List<Employee>
        {
            new() { Id = 1, Name = "Alice Smith", Initials = "AS" }
        };
        _mockWorkLogService.Setup(s => s.GetEmployeesAsync())
            .ReturnsAsync(() => employeesList);

        _mockWorkLogService.Setup(s => s.AddEmployeeAsync(It.IsAny<Employee>()))
            .Returns<Employee>(e =>
            {
                var newEmp = new Employee { Id = employeesList.Count + 1, Name = e.Name, Initials = e.Initials };
                employeesList.Add(newEmp);
                return Task.CompletedTask;
            });

        _mockWorkLogService.Setup(s => s.GetIncidentsAsync())
            .ReturnsAsync(new List<Incident>
            {
                new() { Id = 5, TicketNumber = "INC-999", Title = "Network Down" }
            });

        _mockWorkLogService.Setup(s => s.GetWorkLogsAsync()).ReturnsAsync(new List<WorkLog>());

        var insertedLogs = new List<WorkLog>();
        _mockWorkLogService.Setup(s => s.AddWorkLogAsync(It.IsAny<WorkLog>()))
            .Returns<WorkLog>(wl =>
            {
                insertedLogs.Add(wl);
                return Task.CompletedTask;
            });

        var csv =
            "Date,Time,EmployeeName,Initials,MainCategory,SubCategory,Details,Hours,EarnsCompTime,UsesCompTime,IncidentTicket\r\n" +
            "2026-09-20,09:30,Alice Smith,AS,Work,Architecture,\"Refactoring authentication modules\",2.0,False,False,\r\n" +
            "2026-09-20,11:00,Bob Jones,BJ,Support,Incident,\"Assisted with network downtime\",1.5,True,False,INC-999\r\n";

        // Act
        var result = await _service.ImportWorkLogsFromCsvAsync(csv);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.WorkLogsRestored);
        Assert.Equal(0, result.WorkLogsSkipped);
        Assert.Equal(1, result.EmployeesRestored); // Bob created

        Assert.Equal(2, insertedLogs.Count);

        var alicesLog = insertedLogs.First(l => (l.Details ?? "").Contains("Refactoring authentication"));
        Assert.Equal(1, alicesLog.EmployeeId);
        Assert.Equal(new DateOnly(2026, 9, 20), alicesLog.LogDate);
        Assert.Equal(new TimeOnly(9, 30), alicesLog.LogTime);
        Assert.False(alicesLog.EarnsCompTime);

        var bobsLog = insertedLogs.First(l => (l.Details ?? "").Contains("Assisted with network"));
        Assert.Equal(2, bobsLog.EmployeeId);
        Assert.Equal(5, bobsLog.IncidentId);
        Assert.True(bobsLog.EarnsCompTime);
        Assert.Equal(1.5m, bobsLog.Hours);
    }

    [Fact]
    public async Task ImportWorkLogsFromCsvAsync_MissingRequiredHeader_ReturnsError()
    {
        var invalidCsv = "Col1,Col2\r\nVal1,Val2";
        var result = await _service.ImportWorkLogsFromCsvAsync(invalidCsv);

        Assert.False(result.Success);
        Assert.Contains("missing required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAvailableServerSnapshots_FindsAndParsesSnapshots()
    {
        // Arrange: create 2 snapshot folders
        var snap1 = Path.Combine(_tempOutputDir, "snapshot-20260920-140000");
        Directory.CreateDirectory(snap1);
        File.WriteAllText(Path.Combine(snap1, "backup-full.json"), "{}");
        File.WriteAllText(Path.Combine(snap1, "worklogs.csv"), "Date,Details\r\n");

        var mdDir = Path.Combine(snap1, "Markdown");
        Directory.CreateDirectory(mdDir);
        File.WriteAllText(Path.Combine(mdDir, "2026-09.md"), "# Log");

        var snap2 = Path.Combine(_tempOutputDir, "snapshot-20260921-080000");
        Directory.CreateDirectory(snap2);
        File.WriteAllText(Path.Combine(snap2, "worklogs.csv"), "Date,Details\r\n");

        // Act
        var list = _service.GetAvailableServerSnapshots();

        // Assert
        Assert.Equal(2, list.Count);
        // Ordered descending
        Assert.Equal("snapshot-20260921-080000", list[0].DirectoryName);
        Assert.Equal("snapshot-20260920-140000", list[1].DirectoryName);

        var snap1Info = list.First(s => s.DirectoryName == "snapshot-20260920-140000");
        Assert.True(snap1Info.HasJsonBackup);
        Assert.True(snap1Info.HasCsvBackup);
        Assert.Equal(1, snap1Info.MarkdownFileCount);
        Assert.NotNull(snap1Info.TimestampUtc);
        Assert.Equal(2026, snap1Info.TimestampUtc.Value.Year);

        var snap2Info = list.First(s => s.DirectoryName == "snapshot-20260921-080000");
        Assert.False(snap2Info.HasJsonBackup);
        Assert.True(snap2Info.HasCsvBackup);
        Assert.Equal(0, snap2Info.MarkdownFileCount);
    }

    [Fact]
    public async Task RestoreFromSnapshotDirectoryAsync_FindsBackupJsonAndRestores()
    {
        // Arrange
        var snapDir = Path.Combine(_tempOutputDir, "snapshot-20260921-120000");
        Directory.CreateDirectory(snapDir);

        var snapshot = new DatabaseBackupSnapshot
        {
            Employees = [new() { Id = 1, Name = "Alice Smith", Initials = "AS" }],
            WorkLogs =
            [
                new()
                {
                    EmployeeId = 1,
                    LogDate = new DateOnly(2026, 9, 21),
                    Details = "Restored from dir"
                }
            ]
        };
        await File.WriteAllTextAsync(Path.Combine(snapDir, "backup-full.json"), JsonSerializer.Serialize(snapshot));

        _mockWorkLogService.Setup(s => s.GetEmployeesAsync())
            .ReturnsAsync(new List<Employee> { new() { Id = 1, Name = "Alice Smith", Initials = "AS" } });
        _mockWorkLogService.Setup(s => s.GetIncidentsAsync()).ReturnsAsync(new List<Incident>());
        _mockWorkLogService.Setup(s => s.GetWorkLogsAsync()).ReturnsAsync(new List<WorkLog>());
        _mockWorkLogService.Setup(s => s.AddWorkLogAsync(It.IsAny<WorkLog>())).Returns(Task.CompletedTask);

        // Act
        var result = await _service.RestoreFromSnapshotDirectoryAsync(snapDir);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.WorkLogsRestored);
    }
}
