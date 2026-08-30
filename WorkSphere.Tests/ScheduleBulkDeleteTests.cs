using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MudBlazor;
using MudBlazor.Services;
using WorkSphere.Components.Pages;
using WorkSphere.Models;
using WorkSphere.Services;
using Xunit;

namespace WorkSphere.Tests;

public sealed class ScheduleBulkDeleteTests : IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"worksphere-schedule-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }

    [Fact]
    public void BulkDeleteAction_IsOnlyRenderedWhenSelectionExists()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, initialLogs);

        var cut = harness.Render();

        Assert.DoesNotContain("Delete Selected", cut.Markup);

        ToggleRowSelection(cut, logId: 1, isSelected: true);

        cut.WaitForAssertion(() => Assert.Contains("Delete Selected (1)", cut.Markup));
    }

    [Fact]
    public void BulkDelete_Confirmed_DeletesSelectedLogsRefreshesDataAndClearsSelection()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        var remainingLogs = CreateLogs(3);
        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, remainingLogs, confirmDelete: true, deletedCount: 2);

        var cut = harness.Render();

        ToggleRowSelection(cut, logId: 1, isSelected: true);
        ToggleRowSelection(cut, logId: 2, isSelected: true);
        cut.WaitForAssertion(() => Assert.Contains("Delete Selected (2)", cut.Markup));

        cut.FindAll("button").Single(button => button.TextContent.Contains("Delete Selected", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            harness.WorkLogServiceMock.Verify(service => service.DeleteWorkLogsAsync(It.Is<IEnumerable<int>>(ids => ids.OrderBy(id => id).SequenceEqual(new[] { 1, 2 }))), Times.Once);
            harness.SnackbarMock.Verify(snackbar => snackbar.Add("Deleted 2 log entries", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
            Assert.DoesNotContain("Delete Selected", cut.Markup);
            Assert.Equal(1, FindRowSelectionInputs(cut).Count);
        });
    }

    [Fact]
    public void BulkDelete_Cancelled_DoesNotDeleteSelectedLogs()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, initialLogs, confirmDelete: null);

        var cut = harness.Render();

        ToggleRowSelection(cut, logId: 1, isSelected: true);
        ToggleRowSelection(cut, logId: 2, isSelected: true);
        cut.WaitForAssertion(() => Assert.Contains("Delete Selected (2)", cut.Markup));

        cut.FindAll("button").Single(button => button.TextContent.Contains("Delete Selected", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            harness.WorkLogServiceMock.Verify(service => service.DeleteWorkLogsAsync(It.IsAny<IEnumerable<int>>()), Times.Never);
            Assert.Contains("Delete Selected (2)", cut.Markup);
        });
    }

    [Fact]
    public void BulkDelete_Failure_ShowsErrorAndKeepsSelection()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        using var harness = new ScheduleTestHarness(
            _contentRoot,
            initialLogs,
            initialLogs,
            confirmDelete: true,
            deleteException: new InvalidOperationException("boom"));

        var cut = harness.Render();

        ToggleRowSelection(cut, logId: 1, isSelected: true);
        ToggleRowSelection(cut, logId: 2, isSelected: true);
        cut.WaitForAssertion(() => Assert.Contains("Delete Selected (2)", cut.Markup));

        cut.FindAll("button").Single(button => button.TextContent.Contains("Delete Selected", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            harness.WorkLogServiceMock.Verify(service => service.DeleteWorkLogsAsync(It.Is<IEnumerable<int>>(ids => ids.OrderBy(id => id).SequenceEqual(new[] { 1, 2 }))), Times.Once);
            harness.SnackbarMock.Verify(snackbar => snackbar.Add(It.Is<string>(message => message.Contains("Could not delete selected logs: boom", StringComparison.Ordinal)), Severity.Error, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
            Assert.Contains("Delete Selected (2)", cut.Markup);
        });
    }

    private static List<WorkLog> CreateLogs(params int[] ids) =>
        ids.Select(id => new WorkLog
        {
            Id = id,
            EmployeeId = 100 + id,
            Employee = new Employee { Id = 100 + id, Name = $"Employee {id}", Initials = $"E{id}" },
            LogDate = new DateOnly(2026, 8, id),
            LogTime = new TimeOnly(9, 0),
            MainCategory = "Work",
            SubCategory = "In-Office",
            Details = $"Details {id}",
            OriginalDetails = $"Details {id}"
        }).ToList();

    private static void ToggleRowSelection(IRenderedComponent<Schedule> cut, int logId, bool isSelected)
    {
        var input = cut.FindAll("input[aria-label='Select log " + logId + "']").Single();
        input.Change(isSelected);
    }

    private static IReadOnlyList<IElement> FindRowSelectionInputs(IRenderedComponent<Schedule> cut) =>
        cut.FindAll("input[aria-label^='Select log']");

    private sealed class ScheduleTestHarness : TestContext
    {
        private readonly Mock<IDialogService> _dialogServiceMock = new();
        private readonly Mock<ISnackbar> _snackbarMock = new();

        public ScheduleTestHarness(
            string contentRoot,
            IReadOnlyCollection<WorkLog> initialLogs,
            IReadOnlyCollection<WorkLog> refreshedLogs,
            bool? confirmDelete = true,
            int deletedCount = 0,
            Exception? deleteException = null)
        {
            Directory.CreateDirectory(Path.Combine(contentRoot, "Import"));

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "Host=dummy;Database=worksphere;Username=test;Password=test",
                    ["Migration:LogsPath"] = "Import"
                })
                .Build();

            var environmentMock = new Mock<IWebHostEnvironment>();
            environmentMock.SetupGet(environment => environment.ContentRootPath).Returns(contentRoot);

            WorkLogServiceMock = new Mock<WorkLogService>(configuration, Mock.Of<ILogger<WorkLogService>>()) { CallBase = true };
            WorkLogServiceMock.SetupSequence(service => service.GetWorkLogsAsync())
                .ReturnsAsync(initialLogs)
                .ReturnsAsync(initialLogs)
                .ReturnsAsync(refreshedLogs)
                .ReturnsAsync(refreshedLogs);
            WorkLogServiceMock.Setup(service => service.GetEmployeesAsync())
                .ReturnsAsync(initialLogs.Select(log => log.Employee!).DistinctBy(employee => employee.Id).ToList());

            if (deleteException is not null)
            {
                WorkLogServiceMock.Setup(service => service.DeleteWorkLogsAsync(It.IsAny<IEnumerable<int>>()))
                    .ThrowsAsync(deleteException);
            }
            else
            {
                WorkLogServiceMock.Setup(service => service.DeleteWorkLogsAsync(It.IsAny<IEnumerable<int>>()))
                    .ReturnsAsync(deletedCount);
            }

            _dialogServiceMock
                .Setup(service => service.ShowMessageBoxAsync(
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<DialogOptions?>()))
                .ReturnsAsync(confirmDelete);

            var migrationService = new MigrationService(WorkLogServiceMock.Object, configuration);

            Services.AddMudServices();
            Services.AddSingleton<IConfiguration>(configuration);
            Services.AddSingleton(environmentMock.Object);
            Services.AddSingleton(WorkLogServiceMock.Object);
            Services.AddSingleton(migrationService);
            Services.AddSingleton(_dialogServiceMock.Object);
            Services.AddSingleton(_snackbarMock.Object);

            JSInterop.Mode = JSRuntimeMode.Loose;
        }

        public Mock<WorkLogService> WorkLogServiceMock { get; }

        public Mock<ISnackbar> SnackbarMock => _snackbarMock;

        public IRenderedComponent<Schedule> Render()
        {
            RenderComponent<MudThemeProvider>();
            RenderComponent<MudPopoverProvider>();
            RenderComponent<MudDialogProvider>();
            var component = RenderComponent<Schedule>();
            component.FindAll("[role='tab']")
                .Single(tab => tab.TextContent.Contains("List View", StringComparison.Ordinal))
                .Click();
            component.WaitForAssertion(() => Assert.Contains("Daily Records", component.Markup));
            return component;
        }
    }
}
