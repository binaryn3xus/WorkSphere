using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using WorkSphere.Models;
using WorkSphere.Services;
using Xunit;

namespace WorkSphere.Tests;

public class WorkLogServiceTests
{
    private static Microsoft.Extensions.Configuration.IConfiguration CreateDummyConfig() =>
        new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:DefaultConnection", "Host=dummy;" }
            })
            .Build();

    [Fact]
    public async Task GetWorkLogsByMonthAsync_CallsGetWorkLogsByDateRangeAsyncWithCorrectMonthBoundaries()
    {
        var mockService = new Mock<WorkLogService>(
            CreateDummyConfig(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<WorkLogService>>())
        {
            CallBase = true
        };

        var expectedLogs = new List<WorkLog>
        {
            new() { Id = 1, LogDate = new DateOnly(2026, 2, 14), MainCategory = "Work", SubCategory = "In-Office" }
        };

        var expectedStart = new DateOnly(2026, 2, 1);
        var expectedEnd = new DateOnly(2026, 2, 28); // 2026 is not a leap year

        mockService
            .Setup(s => s.GetWorkLogsByDateRangeAsync(expectedStart, expectedEnd))
            .ReturnsAsync(expectedLogs);

        var result = await mockService.Object.GetWorkLogsByMonthAsync(2026, 2);

        Assert.NotNull(result);
        Assert.Single(result);
        mockService.Verify(s => s.GetWorkLogsByDateRangeAsync(expectedStart, expectedEnd), Times.Once);
    }

    [Fact]
    public async Task GetWorkLogsByMonthAsync_HandlesLeapYearsCorrectly()
    {
        var mockService = new Mock<WorkLogService>(
            CreateDummyConfig(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<WorkLogService>>())
        {
            CallBase = true
        };

        var expectedStart = new DateOnly(2028, 2, 1);
        var expectedEnd = new DateOnly(2028, 2, 29); // 2028 is a leap year

        mockService
            .Setup(s => s.GetWorkLogsByDateRangeAsync(expectedStart, expectedEnd))
            .ReturnsAsync(new List<WorkLog>());

        var result = await mockService.Object.GetWorkLogsByMonthAsync(2028, 2);

        Assert.NotNull(result);
        mockService.Verify(s => s.GetWorkLogsByDateRangeAsync(expectedStart, expectedEnd), Times.Once);
    }

    [Fact]
    public async Task IWorkLogService_MockingContract_WorksSeamlessly()
    {
        var mock = new Mock<IWorkLogService>();
        mock.Setup(s => s.GetWorkLogsCountByMonthAsync(2026, 9)).ReturnsAsync(42);

        var count = await mock.Object.GetWorkLogsCountByMonthAsync(2026, 9);

        Assert.Equal(42, count);
        mock.Verify(s => s.GetWorkLogsCountByMonthAsync(2026, 9), Times.Once);
    }

    [Fact]
    public async Task IWorkLogService_UpdateEmployeeAsync_CanBeInvoked()
    {
        var mock = new Mock<IWorkLogService>();
        var emp = new Employee { Id = 1, Name = "Alice Smith", Initials = "AS" };

        mock.Setup(s => s.UpdateEmployeeAsync(It.IsAny<Employee>())).Returns(Task.CompletedTask);

        await mock.Object.UpdateEmployeeAsync(emp);

        mock.Verify(s => s.UpdateEmployeeAsync(It.Is<Employee>(e => e.Id == 1 && e.Name == "Alice Smith")), Times.Once);
    }
}
