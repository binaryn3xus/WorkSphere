using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
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
    public void CalendarEntryContextMenu_RightClickRendersDeleteActionAfterCopyWithDangerStyling()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, initialLogs);

        var cut = harness.RenderCalendar();

        OpenCalendarContextMenu(cut, logId: 1);

        cut.WaitForAssertion(() =>
        {
            var menuItems = cut.FindAll("[data-testid='calendar-context-menu'] button");
            Assert.Collection(menuItems,
                item => Assert.Equal("Copy Entry", item.TextContent.Trim()),
                item =>
                {
                    Assert.Equal("Delete Entry", item.TextContent.Trim());
                    Assert.Contains("calendar-context-menu__item--danger", item.ClassList);
                });
        });
    }

    [Fact]
    public void CalendarEntryContextMenu_DoesNotRenderDeleteActionForIneligibleEntries()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        initialLogs[0].Id = 0;
        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, initialLogs);

        var cut = harness.RenderCalendar();

        OpenCalendarContextMenu(cut, logId: 0);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Copy Entry", cut.Markup);
            Assert.DoesNotContain("Delete Entry", cut.Markup);
            Assert.Empty(cut.FindAll("[data-testid='calendar-delete-action']"));
        });
    }

    [Fact]
    public void CalendarEntryContextMenu_CopyCapturesIndependentEntrySnapshot()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, initialLogs);

        var cut = harness.RenderCalendar();

        OpenCalendarContextMenu(cut, logId: 1);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='calendar-copy-action']")));
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
    public void CalendarDayContextMenu_WithCopiedEntryOnDifferentDay_RendersPasteAction()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, initialLogs);

        var cut = harness.RenderCalendar();

        OpenCalendarContextMenu(cut, logId: 1);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='calendar-copy-action']")));
        cut.Find("[data-testid='calendar-copy-action']").Click();
        OpenCalendarDayContextMenu(cut, new DateOnly(2026, 8, 2));

        cut.WaitForAssertion(() => Assert.Contains("Paste Entry", cut.Markup));
    }

    [Fact]
    public void CalendarDayContextMenu_PasteCreatesEntryOnTargetDayAndRefreshesCalendar()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        var refreshedLogs = CreateLogs(1, 2, 3);
        refreshedLogs.Add(new WorkLog
        {
            Id = 99,
            EmployeeId = 101,
            Employee = new Employee { Id = 101, Name = "Employee 1", Initials = "E1" },
            LogDate = new DateOnly(2026, 8, 2),
            LogTime = new TimeOnly(9, 0),
            MainCategory = "Work",
            SubCategory = "In-Office",
            Details = "Details 1",
            OriginalDetails = "Details 1"
        });

        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, refreshedLogs);
        harness.WorkLogServiceMock
            .Setup(service => service.AddWorkLogAsync(It.IsAny<WorkLog>()))
            .Returns(Task.CompletedTask);

        var cut = harness.RenderCalendar();

        OpenCalendarContextMenu(cut, logId: 1);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='calendar-copy-action']")));
        cut.Find("[data-testid='calendar-copy-action']").Click();
        OpenCalendarDayContextMenu(cut, new DateOnly(2026, 8, 2));
        cut.Find("[data-testid='calendar-paste-action']").Click();

        cut.WaitForAssertion(() =>
        {
            harness.WorkLogServiceMock.Verify(service => service.AddWorkLogAsync(It.Is<WorkLog>(log =>
                log.Id == 0 &&
                log.LogDate == new DateOnly(2026, 8, 2) &&
                log.LogTime == new TimeOnly(9, 0) &&
                log.EmployeeId == 101 &&
                log.MainCategory == "Work" &&
                log.SubCategory == "In-Office" &&
                log.Details == "Details 1" &&
                log.OriginalDetails == "Details 1")), Times.Once);

            harness.SnackbarMock.Verify(snackbar => snackbar.Add("Pasted entry for Employee 1 to 08/02/2026", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
            Assert.NotNull(cut.Find("[data-log-id='1']"));
            Assert.NotNull(cut.Find("[data-log-id='99']"));
            Assert.Empty(cut.FindAll("[data-testid='calendar-context-menu']"));
        });
    }

    [Fact]
    public void MobileAgenda_RendersDayCardsWithEntrySummariesAndActions()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, initialLogs);

        var cut = harness.RenderCalendar();

        var mobileAgenda = cut.Find("[data-testid='mobile-calendar-agenda']");
        var dayCard = cut.Find("[data-mobile-calendar-day='2026-08-01']");
        var emptyDayCard = cut.Find("[data-mobile-calendar-day='2026-08-04']");

        Assert.Contains("Saturday, August 1", dayCard.TextContent);
        Assert.Contains("1 entry scheduled", dayCard.TextContent);
        Assert.NotNull(mobileAgenda);
        Assert.NotNull(cut.Find("[data-mobile-log-id='1']"));
        Assert.NotNull(cut.Find("button[aria-label='Edit mobile log 1']"));
        Assert.NotNull(cut.Find("button[aria-label='Copy mobile log 1']"));
        Assert.NotNull(cut.Find("button[aria-label='Delete mobile log 1']"));
        Assert.Contains("No schedule entries for this day.", emptyDayCard.TextContent);
    }

    [Fact]
    public void MobileAgenda_CopyThenPaste_CreatesEntryForTargetDay()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        var refreshedLogs = CreateLogs(1, 2, 3);
        refreshedLogs.Add(new WorkLog
        {
            Id = 99,
            EmployeeId = 101,
            Employee = new Employee { Id = 101, Name = "Employee 1", Initials = "E1" },
            LogDate = new DateOnly(2026, 8, 2),
            LogTime = new TimeOnly(9, 0),
            MainCategory = "Work",
            SubCategory = "In-Office",
            Details = "Details 1",
            OriginalDetails = "Details 1"
        });

        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, refreshedLogs);
        harness.WorkLogServiceMock
            .Setup(service => service.AddWorkLogAsync(It.IsAny<WorkLog>()))
            .Returns(Task.CompletedTask);

        var cut = harness.RenderCalendar();

        cut.Find("button[aria-label='Copy mobile log 1']").Click();

        cut.WaitForAssertion(() =>
        {
            var targetDayButtons = cut.Find("[data-mobile-calendar-day='2026-08-02']").QuerySelectorAll("button");
            Assert.Contains(targetDayButtons, button => button.TextContent.Contains("Paste", StringComparison.Ordinal));
        });

        FindButtonByText(cut.Find("[data-mobile-calendar-day='2026-08-02']"), "Paste").Click();

        cut.WaitForAssertion(() =>
        {
            harness.WorkLogServiceMock.Verify(service => service.AddWorkLogAsync(It.Is<WorkLog>(log =>
                log.Id == 0 &&
                log.LogDate == new DateOnly(2026, 8, 2) &&
                log.LogTime == new TimeOnly(9, 0) &&
                log.EmployeeId == 101 &&
                log.MainCategory == "Work" &&
                log.SubCategory == "In-Office" &&
                log.Details == "Details 1" &&
                log.OriginalDetails == "Details 1")), Times.Once);

            harness.SnackbarMock.Verify(snackbar => snackbar.Add("Pasted entry for Employee 1 to 08/02/2026", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
            Assert.NotNull(cut.Find("[data-mobile-log-id='99']"));
        });
    }

    [Fact]
    public void ScheduleListView_RendersMobileFeedCardsAndDesktopTableShell()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, initialLogs);

        var cut = harness.Render();

        Assert.NotEmpty(cut.FindAll(".mobile-feed-card"));
        Assert.NotNull(cut.Find(".schedule-table-shell"));
        Assert.Contains("Search records", cut.Markup);
        Assert.DoesNotContain("schedule-list-search", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ScheduleAuditView_RendersResponsiveToolbarAndMobileCards()
    {
        Directory.CreateDirectory(Path.Combine(_contentRoot, "Import"));
        File.WriteAllText(
            Path.Combine(_contentRoot, "Import", "2026-08.md"),
            "| Date | Time | Initials | Details |\n| --- | --- | --- | --- |\n| 08/01/26 | 9:00 AM | E1 | Arrive in Office |\n");

        var initialLogs = CreateLogs(1, 2, 3);
        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, initialLogs);

        var cut = harness.RenderAudit();

        Assert.NotNull(cut.Find(".schedule-audit-toolbar"));
        Assert.NotEmpty(cut.FindAll(".schedule-audit-select"));
        Assert.NotNull(cut.Find(".schedule-audit-refresh"));
        Assert.NotNull(cut.Find(".mobile-audit-card"));
        Assert.NotNull(cut.Find(".schedule-table-shell"));
        Assert.DoesNotContain("ml-4", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void CalendarEntryContextMenu_DeleteAction_DeletesEntryAndClosesMenu()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        var remainingLogs = CreateLogs(2, 3);
        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, remainingLogs, confirmDelete: true);
        harness.WorkLogServiceMock
            .Setup(service => service.DeleteWorkLogAsync(1))
            .Returns(Task.CompletedTask);

        var cut = harness.RenderCalendar();

        OpenCalendarContextMenu(cut, logId: 1);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='calendar-delete-action']")));
        cut.Find("[data-testid='calendar-delete-action']").Click();

        cut.WaitForAssertion(() =>
        {
            harness.DialogServiceMock.Verify(service => service.ShowMessageBoxAsync(
                "Delete Calendar Entry",
                "Are you sure you want to delete the calendar entry for Employee 1 on 08/01/2026?",
                "Delete",
                null,
                "Cancel",
                It.IsAny<DialogOptions?>()), Times.Once);
            harness.WorkLogServiceMock.Verify(service => service.DeleteWorkLogAsync(1), Times.Once);
            harness.SnackbarMock.Verify(snackbar => snackbar.Add("Log deleted", Severity.Success, It.IsAny<Action<SnackbarOptions>>(), It.IsAny<string?>()), Times.Once);
            Assert.Empty(cut.FindAll("[data-testid='calendar-context-menu']"));
            Assert.Null(cut.FindAll("[data-log-id='1']").FirstOrDefault());
        });
    }

    [Fact]
    public void CalendarEntryContextMenu_DeleteAction_Cancelled_DoesNotDeleteEntry()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        using var harness = new ScheduleTestHarness(_contentRoot, initialLogs, initialLogs, confirmDelete: null);

        var cut = harness.RenderCalendar();

        OpenCalendarContextMenu(cut, logId: 1);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='calendar-delete-action']")));
        cut.Find("[data-testid='calendar-delete-action']").Click();

        cut.WaitForAssertion(() =>
        {
            harness.DialogServiceMock.Verify(service => service.ShowMessageBoxAsync(
                "Delete Calendar Entry",
                "Are you sure you want to delete the calendar entry for Employee 1 on 08/01/2026?",
                "Delete",
                null,
                "Cancel",
                It.IsAny<DialogOptions?>()), Times.Once);
            harness.WorkLogServiceMock.Verify(service => service.DeleteWorkLogAsync(It.IsAny<int>()), Times.Never);
            Assert.NotNull(cut.Find("[data-log-id='1']"));
            Assert.Empty(cut.FindAll("[data-testid='calendar-context-menu']"));
        });
    }

    [Fact]
    public void CalendarEntryContextMenu_DeleteAction_Failure_ShowsErrorAndKeepsCalendarView()
    {
        var initialLogs = CreateLogs(1, 2, 3);
        using var harness = new ScheduleTestHarness(
            _contentRoot,
            initialLogs,
            initialLogs,
            confirmDelete: true,
            deleteSingleException: new InvalidOperationException("boom"));

        var cut = harness.RenderCalendar();

        OpenCalendarContextMenu(cut, logId: 1);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='calendar-delete-action']")));
        cut.Find("[data-testid='calendar-delete-action']").Click();

        cut.WaitForAssertion(() =>
        {
            harness.WorkLogServiceMock.Verify(service => service.DeleteWorkLogAsync(1), Times.Once);
            harness.SnackbarMock.Verify(snackbar => snackbar.Add(
                It.Is<string>(message => message.Contains("Could not delete log: boom", StringComparison.Ordinal)),
                Severity.Error,
                It.IsAny<Action<SnackbarOptions>>(),
                It.IsAny<string?>()), Times.Once);
            Assert.NotNull(cut.Find("[data-log-id='1']"));
            Assert.Empty(cut.FindAll("[data-testid='calendar-context-menu']"));
        });
    }

    [Theory]
    [InlineData(120, 180, 180, 88, 1280, 720, 120, 180)]
    [InlineData(1270, 180, 180, 88, 1280, 720, 1092, 180)]
    [InlineData(120, 715, 180, 88, 1280, 720, 120, 624)]
    [InlineData(-12, -20, 180, 88, 1280, 720, 8, 8)]
    [InlineData(1270, -20, 180, 88, 1280, 720, 1092, 8)]
    [InlineData(-12, 715, 180, 88, 1280, 720, 8, 624)]
    [InlineData(1270, 715, 180, 88, 1280, 720, 1092, 624)]
    [InlineData(640, 360, 1800, 900, 1280, 720, 8, 8)]
    public void CalculateCalendarContextMenuPosition_ClampsMenuWithinViewport(
        double requestedX,
        double requestedY,
        double menuWidth,
        double menuHeight,
        double viewportWidth,
        double viewportHeight,
        double expectedX,
        double expectedY)
    {
        var method = typeof(Schedule).GetMethod("CalculateCalendarContextMenuPosition", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = method!.Invoke(null, [requestedX, requestedY, menuWidth, menuHeight, viewportWidth, viewportHeight, 8d]);
        Assert.NotNull(result);

        Assert.Equal(expectedX, GetProperty<double>(result!, "X"));
        Assert.Equal(expectedY, GetProperty<double>(result!, "Y"));
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
            Assert.Single(FindRowSelectionInputs(cut));
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

    private static void OpenCalendarDayContextMenu(IRenderedComponent<Schedule> cut, DateOnly date)
    {
        cut.Find($"[data-calendar-day='{date:yyyy-MM-dd}']").TriggerEvent("oncontextmenu", new MouseEventArgs
        {
            Button = 2,
            ClientX = 220,
            ClientY = 260
        });
    }

    private static IElement FindButtonByText(IElement container, string text) =>
        container.QuerySelectorAll("button")
            .Single(button => button.TextContent.Contains(text, StringComparison.Ordinal));

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
        private readonly DateOnly? _calendarFocusDate;

        public ScheduleTestHarness(
            string contentRoot,
            IReadOnlyCollection<WorkLog> initialLogs,
            IReadOnlyCollection<WorkLog> refreshedLogs,
            bool? confirmDelete = true,
            int deletedCount = 0,
            Exception? deleteException = null,
            Exception? deleteSingleException = null)
        {
            Directory.CreateDirectory(Path.Combine(contentRoot, "Import"));
            _calendarFocusDate = initialLogs.FirstOrDefault(log => log.LogDate.HasValue)?.LogDate;

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

            if (deleteSingleException is not null)
            {
                WorkLogServiceMock.Setup(service => service.DeleteWorkLogAsync(It.IsAny<int>()))
                    .ThrowsAsync(deleteSingleException);
            }
            else
            {
                WorkLogServiceMock.Setup(service => service.DeleteWorkLogAsync(It.IsAny<int>()))
                    .Returns(Task.CompletedTask);
            }

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

        public Mock<IDialogService> DialogServiceMock => _dialogServiceMock;

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

            if (_calendarFocusDate.HasValue)
            {
                SetCalendarMonth(component, _calendarFocusDate.Value);
            }

            component.WaitForAssertion(() => Assert.Contains("calendar-grid", component.Markup));
            return component;
        }

        public IRenderedComponent<Schedule> RenderAudit()
        {
            RenderComponent<MudThemeProvider>();
            RenderComponent<MudPopoverProvider>();
            RenderComponent<MudDialogProvider>();
            var component = RenderComponent<Schedule>();

            if (_calendarFocusDate.HasValue)
            {
                var auditYearField = typeof(Schedule).GetField("_auditYear", BindingFlags.Instance | BindingFlags.NonPublic);
                var auditMonthField = typeof(Schedule).GetField("_auditMonth", BindingFlags.Instance | BindingFlags.NonPublic);
                var loadAuditDataMethod = typeof(Schedule).GetMethod("LoadAuditData", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(auditYearField);
                Assert.NotNull(auditMonthField);
                Assert.NotNull(loadAuditDataMethod);

                component.InvokeAsync(async () =>
                {
                    auditYearField!.SetValue(component.Instance, _calendarFocusDate.Value.Year);
                    auditMonthField!.SetValue(component.Instance, _calendarFocusDate.Value.Month);
                    await (Task)loadAuditDataMethod!.Invoke(component.Instance, null)!;
                    typeof(ComponentBase)
                        .GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(component.Instance, null);
                }).GetAwaiter().GetResult();
            }

            component.FindAll("[role='tab']")
                .Single(tab => tab.TextContent.Contains("Audit", StringComparison.Ordinal))
                .Click();
            component.WaitForAssertion(() => Assert.Contains("Refresh Audit", component.Markup));
            return component;
        }

        private static void SetCalendarMonth(IRenderedComponent<Schedule> component, DateOnly targetDate)
        {
            var currentDateField = typeof(Schedule).GetField("_currentDate", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(currentDateField);

            component.InvokeAsync(() =>
            {
                currentDateField!.SetValue(component.Instance, new DateTime(targetDate.Year, targetDate.Month, 1));
                typeof(ComponentBase)
                    .GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(component.Instance, null);
            }).GetAwaiter().GetResult();

            component.WaitForAssertion(() => Assert.Contains($"data-calendar-day=\"{targetDate:yyyy-MM}-01\"", component.Markup));
        }
    }
}
