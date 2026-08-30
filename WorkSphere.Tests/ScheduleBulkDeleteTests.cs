using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
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
    public void CalendarEntryContextMenu_RightClickRendersCopyAction()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, initialLogs);

        var cut = harness.RenderCalendar();

        OpenCalendarContextMenu(cut, logId: 1);

        cut.WaitForAssertion(() => Assert.Contains("Copy Entry", cut.Markup));
    }

    [Fact]
    public void CalendarEntryContextMenu_CopyCapturesIndependentEntrySnapshot()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, initialLogs);

        var cut = harness.RenderCalendar();

        OpenCalendarContextMenu(cut, logId: 1);
        cut.Find("[data-testid='calendar-copy-action']").Click();

        cut.WaitForAssertion(() =>
        {
            harness.SnackbarMock.Verify(snackbar => snackbar.Add("Copied entry for Employee 1", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);

            var copiedEntry = GetCopiedEntry(cut);
            Assert.NotNull(copiedEntry);
            Assert.Equal(1, GetProperty<int>(copiedEntry!, "SourceLogId"));
            Assert.Equal(new DateOnly(2026, 8, 1), GetProperty<DateOnly?>(copiedEntry!, "SourceDate"));
            Assert.Equal(new TimeOnly(9, 0), GetProperty<TimeOnly?>(copiedEntry!, "LogTime"));
            Assert.Equal(101, GetProperty<int>(copiedEntry!, "EmployeeId"));
            Assert.Equal("Employee 1", GetProperty<string>(copiedEntry!, "EmployeeName"));
            Assert.Equal("Work", GetProperty<string>(copiedEntry!, "MainCategory"));
            Assert.Equal("In-Office", GetProperty<string>(copiedEntry!, "SubCategory"));
            Assert.Equal("Details 1", GetProperty<string>(copiedEntry!, "Details"));
            Assert.Equal("Details 1", GetProperty<string>(copiedEntry!, "OriginalDetails"));
        });

        initialLogs[0].Details = "Mutated after copy";
        initialLogs[0].OriginalDetails = "Mutated after copy";

        var copiedAfterMutation = GetCopiedEntry(cut);
        Assert.NotNull(copiedAfterMutation);
        Assert.Equal("Details 1", GetProperty<string>(copiedAfterMutation!, "Details"));
        Assert.Equal("Details 1", GetProperty<string>(copiedAfterMutation!, "OriginalDetails"));
        Assert.Empty(cut.FindAll("[data-testid='calendar-context-menu']"));
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

    private static void OpenCalendarContextMenu(IRenderedComponent<Schedule> cut, int logId)
    {
        cut.Find($"[data-log-id='{logId}']").TriggerEvent("oncontextmenu", new MouseEventArgs
        {
            Button = 2,
            ClientX = 120,
            ClientY = 180
        });
    }

    private static object? GetCopiedEntry(IRenderedComponent<Schedule> cut) =>
        typeof(Schedule)
            .GetField("_copiedEntry", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(cut.Instance);

    private static T GetProperty<T>(object instance, string propertyName) =>
        (T)instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!.GetValue(instance)!;

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

        public IRenderedComponent<Schedule> RenderCalendar()
        {
            RenderComponent<MudThemeProvider>();
            RenderComponent<MudPopoverProvider>();
            RenderComponent<MudDialogProvider>();
            var component = RenderComponent<Schedule>();
            component.WaitForAssertion(() => Assert.Contains("calendar-grid", component.Markup));
            return component;
        }
    }
}
