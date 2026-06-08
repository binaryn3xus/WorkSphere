using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using WorkSphere.Models;
using WorkSphere.Services;
using Xunit;

namespace WorkSphere.Tests;

public class MigrationServiceTests : IDisposable
{
    private readonly string _tempFilePath;

    public MigrationServiceTests()
    {
        // Create a temporary file path for tests to write to
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.md");
    }

    public void Dispose()
    {
        // Cleanup the temporary file after each test
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }

    private MigrationService CreateService()
    {
        // Mock dependencies. MigrationService needs WorkLogService and IConfiguration.
        // Since we are only testing ParseMarkdownFileAsync, we don't need fully functional dependencies.
        var mockLogger = new Mock<ILogger<WorkLogService>>();
        
        // Build an actual configuration object to avoid mocking extension methods
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:DefaultConnection", "Host=dummy;" }
            })
            .Build();

        var workLogService = new WorkLogService(config, mockLogger.Object);
        return new MigrationService(workLogService, config);
    }

    [Fact]
    public async Task ParseMarkdownFileAsync_ValidRows_ReturnsCorrectWorkLogs()
    {
        // Arrange
        var markdownContent = @"
# May 2026

## Subject J

| Day      | Time  | Subject | Details                                          |
|----------|-------|---------|--------------------------------------------------|
| 05/01/26 | 08:00 | JG      | Arrive in Office                                 |
| 05/04/26 | 00:00 | JG      | Vacation / Comp                                  |
| 05/05/26 |       | JG      | Work From Home (Standard)                        |
| 05/06/26 | 09:00 | JG      | Incident Response                                |
";
        await File.WriteAllTextAsync(_tempFilePath, markdownContent);
        
        var service = CreateService();
        var employeeMap = new Dictionary<string, int> { { "JG", 1 } };

        // Act
        var logs = await service.ParseMarkdownFileAsync(_tempFilePath, employeeMap);

        // Assert
        Assert.Equal(4, logs.Count);

        // Row 1: Office
        var log1 = logs[0];
        Assert.Equal(new DateOnly(2026, 5, 1), log1.LogDate);
        Assert.Equal(new TimeOnly(8, 0), log1.LogTime);
        Assert.Equal(1, log1.EmployeeId);
        Assert.Equal("Work", log1.MainCategory);
        Assert.Equal("In-Office", log1.SubCategory);
        Assert.Equal("Arrive in Office", log1.OriginalDetails);

        // Row 2: PTO/Comp
        var log2 = logs[1];
        Assert.Equal("Leave", log2.MainCategory);
        Assert.Equal("PTO", log2.SubCategory);

        // Row 3: WFH (Missing time)
        var log3 = logs[2];
        Assert.Null(log3.LogTime);
        Assert.Equal("Work", log3.MainCategory);
        Assert.Equal("Work From Home", log3.SubCategory);

        // Row 4: Incident Response
        var log4 = logs[3];
        Assert.Equal("Work", log4.MainCategory);
        Assert.Equal("Incident Response", log4.SubCategory);
    }

    [Fact]
    public async Task ParseMarkdownFileAsync_DetailsContainingDayOrTime_AreNotSkipped()
    {
        // Arrange
        var markdownContent = @"
| Day      | Time  | Subject | Details                                          |
|----------|-------|---------|--------------------------------------------------|
| 05/07/26 | 08:00 | JG      | Sick Day                                         |
| 05/08/26 | 08:00 | JG      | Comp Time Used                                   |
";
        await File.WriteAllTextAsync(_tempFilePath, markdownContent);

        var service = CreateService();
        var employeeMap = new Dictionary<string, int> { { "JG", 1 } };

        // Act
        var logs = await service.ParseMarkdownFileAsync(_tempFilePath, employeeMap);

        // Assert
        Assert.Equal(2, logs.Count);
        Assert.Equal("Leave", logs[0].MainCategory);
        Assert.Equal("Sick Day", logs[0].SubCategory);
        Assert.Equal("Leave", logs[1].MainCategory);
        Assert.Equal("Comp Day", logs[1].SubCategory);
    }

    [Fact]
    public async Task ParseMarkdownFileAsync_UnknownOrMissingEmployee_SkipsRow()
    {
        // Arrange
        var markdownContent = @"
| Day      | Time  | Subject | Details                                          |
|----------|-------|---------|--------------------------------------------------|
| 05/01/26 | 08:00 | XX      | Arrive in Office                                 |
";
        await File.WriteAllTextAsync(_tempFilePath, markdownContent);
        var service = CreateService();
        
        // EmployeeMap does not contain "XX"
        var employeeMap = new Dictionary<string, int> { { "JG", 1 } };

        // Act
        var logs = await service.ParseMarkdownFileAsync(_tempFilePath, employeeMap);

        // Assert
        Assert.Empty(logs);
    }

    [Fact]
    public async Task ParseMarkdownFileAsync_UnknownDetails_SkipsRow()
    {
        // Arrange
        var markdownContent = @"
| Day      | Time  | Subject | Details                                          |
|----------|-------|---------|--------------------------------------------------|
| 05/01/26 | 08:00 | JG      | Unknown                                          |
| 05/02/26 | 08:00 | JG      |                                                  |
";
        await File.WriteAllTextAsync(_tempFilePath, markdownContent);
        var service = CreateService();
        var employeeMap = new Dictionary<string, int> { { "JG", 1 } };

        // Act
        var logs = await service.ParseMarkdownFileAsync(_tempFilePath, employeeMap);

        // Assert
        Assert.Empty(logs);
    }
}
