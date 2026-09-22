using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using MudBlazor;
using WorkSphere.Models;
using WorkSphere.Services;

namespace WorkSphere.Components.Pages;

public partial class Schedule
{
    private static readonly ScheduleTabOption[] ScheduleTabOptions =
    [
        new("Calendar", Icons.Material.Filled.CalendarMonth),
        new("List View", Icons.Material.Filled.List),
        new("Audit", Icons.Material.Filled.FactCheck)
    ];

    private DateOnly Today => UserTimeService.GetToday();
    private DateTime _currentDate = DateTime.Today;
    private List<WorkLog> _logs = new();
    private string[] _daysOfWeek = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
    private string _searchString = "";
    private bool _needsReviewOnly = false;
    private int _activeScheduleTabIndex;
    private readonly HashSet<int> _selectedLogIds = [];
    private bool _isDeletingLogs;
    private CalendarContextMenuState _calendarContextMenu = new();
    private ElementReference _calendarContextMenuElement;
    private bool _repositionCalendarContextMenuOnRender;
    private CopiedCalendarEntry? _copiedEntry;

    private int _daysInMonth => DateTime.DaysInMonth(_currentDate.Year, _currentDate.Month);
    private int _offsetDays => (int)new DateTime(_currentDate.Year, _currentDate.Month, 1).DayOfWeek;
    private IEnumerable<DateOnly> DaysInCurrentMonth => Enumerable.Range(1, _daysInMonth).Select(day => new DateOnly(_currentDate.Year, _currentDate.Month, day));
    private IEnumerable<WorkLog> FilteredLogs => _logs.Where(FilterFunc);
    private int VisibleSelectedLogCount => FilteredLogs.Count(log => _selectedLogIds.Contains(log.Id));
    private bool AreAllVisibleLogsSelected => FilteredLogs.Any() && FilteredLogs.All(log => _selectedLogIds.Contains(log.Id));
    private bool HasPartialVisibleSelection => FilteredLogs.Any(log => _selectedLogIds.Contains(log.Id)) && !AreAllVisibleLogsSelected;
    private bool? SelectAllCheckboxState =>
        !FilteredLogs.Any() ? false :
        AreAllVisibleLogsSelected ? true :
        HasPartialVisibleSelection ? null : false;
    private bool HasCopiedEntry => _copiedEntry is not null;
    private bool CanDeleteCalendarContextMenuLog =>
        _calendarContextMenu.Log is { Id: > 0 } log &&
        !_isDeletingLogs &&
        _logs.Any(existingLog => existingLog.Id == log.Id);
    private List<AuditRow> _auditRows = new();
    private string _auditFilter = "All";
    private int _auditYear = DateTime.Today.Year;
    private int _auditMonth = DateTime.Today.Month;
    private string? _missingFileWarning;

    private IEnumerable<AuditRow> FilteredAuditRows => _auditFilter switch
    {
        "Missing" => _auditRows.Where(r => r.InMarkdown && r.DbLog == null),
        "Extra" => _auditRows.Where(r => !r.InMarkdown),
        "Mismatch" => _auditRows.Where(r => r.InMarkdown && r.DbLog != null && 
                                           (Normalize(r.MarkdownDetails) != Normalize(r.DbLog.OriginalDetails) || 
                                            r.MarkdownTime != r.DbLog.LogTime)),
        _ => _auditRows
    };

    protected override async Task OnInitializedAsync()
    {
        _currentDate = Today.ToDateTime(TimeOnly.MinValue);
        _auditYear = Today.Year;
        _auditMonth = Today.Month;
        await LoadData();
        await LoadAuditData();
    }

    private Task OnActiveScheduleTabChanged(int tabIndex)
    {
        if (tabIndex < 0 || tabIndex >= ScheduleTabOptions.Length)
        {
            return Task.CompletedTask;
        }

        _activeScheduleTabIndex = tabIndex;
        return Task.CompletedTask;
    }

    private string GetScheduleTabIcon(int tabIndex)
    {
        if (tabIndex < 0 || tabIndex >= ScheduleTabOptions.Length)
        {
            return Icons.Material.Filled.CalendarMonth;
        }

        return ScheduleTabOptions[tabIndex].Icon;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_calendarContextMenu.IsVisible || !_repositionCalendarContextMenuOnRender)
        {
            return;
        }

        _repositionCalendarContextMenuOnRender = false;

        var measurements = await JSRuntime.InvokeAsync<ContextMenuViewportMeasurements?>(
            "measureCalendarContextMenuPosition",
            _calendarContextMenuElement);

        if (measurements is null)
        {
            return;
        }

        var positionedMenu = CalculateCalendarContextMenuPosition(
            _calendarContextMenu.X,
            _calendarContextMenu.Y,
            measurements.MenuWidth,
            measurements.MenuHeight,
            measurements.ViewportWidth,
            measurements.ViewportHeight,
            ContextMenuViewportPadding);

        if (positionedMenu.X.Equals(_calendarContextMenu.X) && positionedMenu.Y.Equals(_calendarContextMenu.Y))
        {
            return;
        }

        _calendarContextMenu = _calendarContextMenu with
        {
            X = positionedMenu.X,
            Y = positionedMenu.Y
        };

        StateHasChanged();
    }

    private async Task LoadData()
    {
        _logs = (await WorkLogService.GetWorkLogsAsync()).ToList();
        _selectedLogIds.RemoveWhere(id => _logs.All(log => log.Id != id));
    }

    private async Task LoadAuditData()
    {
        _missingFileWarning = null;
        var logsPath = Configuration["Migration:LogsPath"] ?? "Import";
        
        var employees = await WorkLogService.GetEmployeesAsync();
        var employeeMap = employees.ToDictionary(e => e.Initials, e => e.Id);
        var idToInitials = employees.ToDictionary(e => e.Id, e => e.Initials);

        var markdownLogs = new List<WorkLog>();
        var missingFiles = new List<string>();

        if (_auditMonth == 0)
        {
            for (int m = 1; m <= 12; m++)
            {
                var fileName = $"{_auditYear}-{m:D2}.md";
                var filePath = Path.Combine(Env.ContentRootPath, logsPath, fileName);
                if (File.Exists(filePath))
                {
                    markdownLogs.AddRange(await MigrationService.ParseMarkdownFileAsync(filePath, employeeMap));
                }
                else if (_auditYear <= DateTime.Today.Year && m <= DateTime.Today.Month)
                {
                    missingFiles.Add(fileName);
                }
            }
            if (missingFiles.Any())
            {
                _missingFileWarning = $"Missing {missingFiles.Count} markdown files for {_auditYear}.";
            }
        }
        else
        {
            var fileName = $"{_auditYear}-{_auditMonth:D2}.md";
            var filePath = Path.Combine(Env.ContentRootPath, logsPath, fileName);
            if (File.Exists(filePath))
            {
                markdownLogs.AddRange(await MigrationService.ParseMarkdownFileAsync(filePath, employeeMap));
            }
            else
            {
                _missingFileWarning = $"Markdown file not found: {fileName}";
            }
        }
        
        // RE-FETCH ALL LOGS
        var allLogs = await WorkLogService.GetWorkLogsAsync();
        var dbLogs = allLogs.Where(l => l.LogDate?.Year == _auditYear && (_auditMonth == 0 || l.LogDate?.Month == _auditMonth)).ToList();

        _auditRows.Clear();

        // 1. Map Markdown entries to rows
        foreach (var m in markdownLogs)
        {
            // Match with high confidence (Date, Emp, Details, AND Time)
            var match = dbLogs.FirstOrDefault(d => 
                d.LogDate == m.LogDate && 
                d.EmployeeId == m.EmployeeId && 
                d.LogTime == m.LogTime &&
                Normalize(d.OriginalDetails) == Normalize(m.OriginalDetails));

            // Fallback: Match on Date, Emp, and Details (Flag Time Diff)
            if (match == null)
            {
                match = dbLogs.FirstOrDefault(d => 
                    d.LogDate == m.LogDate && 
                    d.EmployeeId == m.EmployeeId && 
                    Normalize(d.OriginalDetails) == Normalize(m.OriginalDetails));
            }

            _auditRows.Add(new AuditRow
            {
                Date = m.LogDate ?? default,
                EmployeeId = m.EmployeeId,
                Initials = idToInitials.GetValueOrDefault(m.EmployeeId, "?"),
                MarkdownDetails = m.OriginalDetails,
                MarkdownTime = m.LogTime,
                DbLog = match,
                InMarkdown = true
            });

            if (match != null) dbLogs.Remove(match);
        }

        // 2. Add remaining DB logs (Extra entries)
        foreach (var d in dbLogs)
        {
            _auditRows.Add(new AuditRow
            {
                Date = d.LogDate ?? default,
                EmployeeId = d.EmployeeId,
                Initials = idToInitials.GetValueOrDefault(d.EmployeeId, "?"),
                MarkdownDetails = null,
                MarkdownTime = null,
                DbLog = d,
                InMarkdown = false
            });
        }

        _auditRows = _auditRows.OrderBy(r => r.Date).ThenBy(r => r.Initials).ToList();
        StateHasChanged();
    }

    private string Normalize(string? val) => val?.Trim().ToLower() ?? "";

    private string GetAuditRowStyle(AuditRow row, int index)
    {
        if (row.DbLog == null) return "background-color: rgba(244, 67, 54, 0.05);"; // Error/Missing
        if (!row.InMarkdown) return "background-color: rgba(33, 150, 243, 0.05);"; // Info/Extra
        if (row.MarkdownTime != row.DbLog?.LogTime) return "background-color: rgba(255, 152, 0, 0.03);"; // Minor warning
        return "";
    }

    private async Task AddMissingFromMarkdown(AuditRow row)
    {
        var employees = await WorkLogService.GetEmployeesAsync();
        var emp = employees.FirstOrDefault(e => e.Id == row.EmployeeId);
        
        var newLog = new WorkLog
        {
            LogDate = row.Date,
            LogTime = row.MarkdownTime,
            EmployeeId = row.EmployeeId,
            Employee = emp,
            MainCategory = "Work",
            SubCategory = "In-Office",
            OriginalDetails = row.MarkdownDetails,
            Details = row.MarkdownDetails
        };

        var parameters = new DialogParameters<AddWorkLogDialog> { { x => x.Log, newLog } };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        var dialog = await DialogService.ShowAsync<AddWorkLogDialog>("Add Missing Entry", parameters, options);
        var result = await dialog.Result;
        
        if (result != null && !result.Canceled)
        {
            await WorkLogService.AddWorkLogAsync((WorkLog)result.Data!);
            await LoadData();
            await LoadAuditData();
            Snackbar.Add("Entry added from Markdown", Severity.Success);
        }
    }

    private async Task OnYearChanged(int year)
    {
        _currentDate = new DateTime(year, _currentDate.Month, 1);
        await LoadAuditData();
    }

    private async Task OnMonthChanged(int month)
    {
        _currentDate = new DateTime(_currentDate.Year, month, 1);
        await LoadAuditData();
    }

    private async Task OnAuditYearChanged(int year)
    {
        _auditYear = year;
        await LoadAuditData();
    }

    private async Task OnAuditMonthChanged(int month)
    {
        _auditMonth = month;
        await LoadAuditData();
    }

    public class AuditRow
    {
        public DateOnly Date { get; set; }
        public int EmployeeId { get; set; }
        public string Initials { get; set; } = "";
        public string? MarkdownDetails { get; set; }
        public TimeOnly? MarkdownTime { get; set; }
        public WorkLog? DbLog { get; set; }
        public bool InMarkdown { get; set; }
    }

    private string GetLogCssClass(WorkLog log) => log.SubCategory switch
    {
        "In-Office" => "cat-office",
        "Work From Home" => "cat-wfh",
        "Remote Location" => "cat-remote",
        "PTO" => "cat-pto",
        "Sick Day" => "cat-sick",
        "FMLA Day" => "cat-fmla",
        "Comp Day" => "cat-pto",
        "Holiday" => "cat-holiday",
        _ => "cat-other"
    };

    private Color GetLogColor(WorkLog log) => log.SubCategory switch
    {
        "In-Office" => Color.Info,
        "Work From Home" => Color.Tertiary,
        "Remote Location" => Color.Secondary,
        "PTO" => Color.Success,
        "Sick Day" => Color.Error,
        "FMLA Day" => Color.Error,
        "Comp Day" => Color.Warning,
        "Holiday" => Color.Warning,
        _ => log.MainCategory switch
        {
            "Work" => Color.Primary,
            "Leave" => Color.Error,
            _ => Color.Default
        }
    };

    private string GetLogIcon(WorkLog log) => log.SubCategory switch
    {
        "In-Office" => Icons.Material.Filled.Business,
        "Work From Home" => Icons.Material.Filled.Home,
        "Remote Location" => Icons.Material.Filled.Factory,
        "PTO" => Icons.Material.Filled.BeachAccess,
        "Sick Day" => Icons.Material.Filled.LocalHospital,
        "FMLA Day" => Icons.Material.Filled.FamilyRestroom,
        "Comp Day" => Icons.Material.Filled.History,
        "Holiday" => Icons.Material.Filled.Celebration,
        _ => Icons.Material.Filled.Label
    };

    private string GetRowStyle(WorkLog log, int index)
    {
        if (log.MainCategory == "Other" || log.SubCategory == "Other")
            return "background-color: rgba(255, 152, 0, 0.15);";
        return "";
    }

    private string GetFormattedLogDate(DateOnly? date) => date?.ToString("dddd, MMM dd, yyyy") ?? "No date";

    private string GetFormattedLogTime(TimeOnly? time) =>
        time.HasValue && time.Value != TimeOnly.MinValue
            ? time.Value.ToString("h:mm tt")
            : "No time";

    private static bool AuditRowHasMismatch(AuditRow row) =>
        row.InMarkdown && row.DbLog is not null &&
        !string.Equals(row.MarkdownDetails?.Trim(), row.DbLog.OriginalDetails?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool AuditRowHasTimeDifference(AuditRow row) =>
        row.DbLog is not null && row.MarkdownTime != row.DbLog.LogTime;

    private static Color GetAuditStatusColor(AuditRow row) =>
        !row.InMarkdown ? Color.Info :
        row.DbLog is null ? Color.Error :
        AuditRowHasMismatch(row) ? Color.Warning :
        AuditRowHasTimeDifference(row) ? Color.Warning :
        Color.Success;

    private static Variant GetAuditStatusVariant(AuditRow row) =>
        !row.InMarkdown || row.DbLog is null || AuditRowHasMismatch(row)
            ? Variant.Filled
            : AuditRowHasTimeDifference(row)
                ? Variant.Outlined
                : Variant.Text;

    private static string GetAuditStatusText(AuditRow row) =>
        !row.InMarkdown ? "DB Only" :
        row.DbLog is null ? "Missing" :
        AuditRowHasMismatch(row) ? "Mismatch" :
        AuditRowHasTimeDifference(row) ? "Time Diff" :
        "Matched";

    private bool FilterFunc(WorkLog log)
    {
        if (_needsReviewOnly && log.MainCategory != "Other" && log.SubCategory != "Other")
            return false;
        if (string.IsNullOrWhiteSpace(_searchString)) return true;
        var search = _searchString.ToLower();
        return (log.Employee?.Name?.ToLower().Contains(search) ?? false) ||
               (log.MainCategory?.ToLower().Contains(search) ?? false) ||
               (log.SubCategory?.ToLower().Contains(search) ?? false) ||
               (log.Details?.ToLower().Contains(search) ?? false) ||
               (log.OriginalDetails?.ToLower().Contains(search) ?? false);
    }

    private bool IsLogSelected(int id) => _selectedLogIds.Contains(id);

    private void OpenLogContextMenu(MouseEventArgs args, WorkLog log)
    {
        _calendarContextMenu = new CalendarContextMenuState
        {
            IsVisible = true,
            X = args.ClientX,
            Y = args.ClientY,
            Log = log
        };

        _repositionCalendarContextMenuOnRender = true;
    }

    private void OpenDayContextMenu(MouseEventArgs args, DateOnly targetDate)
    {
        if (!CanPasteCopiedEntryTo(targetDate))
        {
            HideCalendarContextMenu();
            return;
        }

        _calendarContextMenu = new CalendarContextMenuState
        {
            IsVisible = true,
            X = args.ClientX,
            Y = args.ClientY,
            TargetDate = targetDate
        };

        _repositionCalendarContextMenuOnRender = true;
    }

    private void HideCalendarContextMenu()
    {
        if (_calendarContextMenu.IsVisible)
        {
            _calendarContextMenu = new();
            _repositionCalendarContextMenuOnRender = false;
        }
    }

    private Task CopyContextMenuLog()
    {
        if (_calendarContextMenu.Log is null)
        {
            return Task.CompletedTask;
        }

        return CopyLog(_calendarContextMenu.Log);
    }

    private async Task DeleteContextMenuLogAsync()
    {
        if (!CanDeleteCalendarContextMenuLog || _calendarContextMenu.Log is not { Id: > 0 } log)
        {
            HideCalendarContextMenu();
            return;
        }

        HideCalendarContextMenu();
        await DeleteLog(log, isCalendarEntry: true);
    }

    private IEnumerable<WorkLog> GetLogsForDate(DateOnly date) =>
        _logs.Where(l => l.LogDate == date)
            .OrderBy(l => l.LogTime ?? TimeOnly.MinValue)
            .ThenBy(l => l.Employee?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    private static string GetMobileDaySummary(int count) => count switch
    {
        0 => "No entries scheduled",
        1 => "1 entry scheduled",
        _ => $"{count} entries scheduled"
    };

    private static string GetLogTimeLabel(WorkLog log) =>
        log.LogTime.HasValue && log.LogTime != TimeOnly.MinValue
            ? log.LogTime.Value.ToString("h:mm tt")
            : "Time not set";

    private static string GetMobileLogMeta(WorkLog log) =>
        string.IsNullOrWhiteSpace(log.Employee?.Initials)
            ? GetLogTimeLabel(log)
            : $"{GetLogTimeLabel(log)} • {log.Employee.Initials}";

    private Task CopyLog(WorkLog log)
    {
        _copiedEntry = CopiedCalendarEntry.From(log);
        HideCalendarContextMenu();
        Snackbar.Add($"Copied entry for {_copiedEntry.EmployeeName}", Severity.Success);
        return Task.CompletedTask;
    }

    private bool CanPasteCopiedEntryTo(DateOnly targetDate) =>
        _copiedEntry is not null && _copiedEntry.SourceDate != targetDate;

    private Task PasteCopiedEntryToDateAsync(DateOnly targetDate)
    {
        _calendarContextMenu = new CalendarContextMenuState
        {
            TargetDate = targetDate
        };

        return PasteContextMenuEntryAsync();
    }

    private async Task PasteContextMenuEntryAsync()
    {
        if (_calendarContextMenu.TargetDate is not DateOnly targetDate)
        {
            HideCalendarContextMenu();
            Snackbar.Add("Select a day to paste into.", Severity.Warning);
            return;
        }

        if (_copiedEntry is null)
        {
            HideCalendarContextMenu();
            Snackbar.Add("Copy an entry before pasting.", Severity.Warning);
            return;
        }

        if (_copiedEntry.SourceDate == targetDate)
        {
            HideCalendarContextMenu();
            Snackbar.Add("Choose a different day to paste this entry.", Severity.Warning);
            return;
        }

        var pastedLog = new WorkLog
        {
            LogDate = targetDate,
            LogTime = _copiedEntry.LogTime,
            EmployeeId = _copiedEntry.EmployeeId,
            MainCategory = _copiedEntry.MainCategory,
            SubCategory = _copiedEntry.SubCategory,
            Details = _copiedEntry.Details,
            OriginalDetails = _copiedEntry.OriginalDetails,
            IncidentId = _copiedEntry.IncidentId,
            EarnsCompTime = _copiedEntry.EarnsCompTime,
            UsesCompTime = _copiedEntry.UsesCompTime,
            Hours = _copiedEntry.Hours
        };

        try
        {
            await WorkLogService.AddWorkLogAsync(pastedLog);
            await LoadData();
            await LoadAuditData();
            HideCalendarContextMenu();
            Snackbar.Add($"Pasted entry for {_copiedEntry.EmployeeName} to {targetDate:MM/dd/yyyy}", Severity.Success);
        }
        catch (Exception ex)
        {
            HideCalendarContextMenu();
            Snackbar.Add($"Could not paste entry: {ex.Message}", Severity.Error);
        }
    }

    private Task ToggleLogSelection(int id, bool isSelected)
    {
        if (isSelected)
        {
            _selectedLogIds.Add(id);
        }
        else
        {
            _selectedLogIds.Remove(id);
        }

        return Task.CompletedTask;
    }

    private Task ToggleSelectAllVisibleLogs(bool? selectAll)
    {
        bool shouldSelect = !AreAllVisibleLogsSelected;
        foreach (var logId in FilteredLogs.Select(log => log.Id))
        {
            if (shouldSelect)
            {
                _selectedLogIds.Add(logId);
            }
            else
            {
                _selectedLogIds.Remove(logId);
            }
        }

        return Task.CompletedTask;
    }

    private void ClearSelectedLogs() => _selectedLogIds.Clear();

    private async Task OpenAddDialog(DateOnly? presetDate)
    {
        var options = new DialogOptions { CloseOnEscapeKey = true };
        DialogResult? result = null;
        
        if (presetDate.HasValue)
        {
            var newLog = new WorkLog
            {
                LogDate = presetDate.Value,
                LogTime = null, // Will be set to client local time in AddWorkLogDialog.razor
                MainCategory = "Work",
                SubCategory = "In-Office"
            };
            var parameters = new DialogParameters<AddWorkLogDialog> { { x => x.Log, newLog } };
            var dialog = await DialogService.ShowAsync<AddWorkLogDialog>("Add Log", parameters, options);
            result = await dialog.Result;
        }
        else
        {
            var dialog = await DialogService.ShowAsync<AddWorkLogDialog>("Add Log", options);
            result = await dialog.Result;
        }

        if (result != null && !result.Canceled)
        {
            await WorkLogService.AddWorkLogAsync((WorkLog)result.Data!);
            await LoadData();
            Snackbar.Add("Entry added", Severity.Success);
        }
    }

    private async Task OpenRangeDialog()
    {
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        var dialog = await DialogService.ShowAsync<AddRangeWorkLogDialog>("Add Date Range", options);
        var result = await dialog.Result;
        if (result != null && !result.Canceled)
        {
            var request = (WorkLogRangeRequest)result.Data!;
            var createResult = await WorkLogService.CreateWorkLogRangeAsync(request);
            await LoadData();
            await LoadAuditData();

            var severity = createResult.CreatedCount > 0 ? Severity.Success : Severity.Warning;
            var message = $"Created {createResult.CreatedCount} range entry/entries";
            if (createResult.SkippedCount > 0)
            {
                message += $" ({createResult.SkippedCount} skipped as duplicates/holidays/weekends)";
            }
            Snackbar.Add(message, severity);
        }
    }

    private async Task OpenCompanyHolidayDialog()
    {
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        var dialog = await DialogService.ShowAsync<CompanyHolidayDialog>("Company Holiday", options);
        var result = await dialog.Result;

        if (result != null && !result.Canceled)
        {
            var createRequest = (HolidayCreateRequest)result.Data!;
            var createResult = await WorkLogService.CreateCompanyHolidayAsync(createRequest);
            await LoadData();
            await LoadAuditData();

            var severity = createResult.CreatedCount > 0 ? Severity.Success : Severity.Warning;
            var message = $"Created {createResult.CreatedCount} holiday entr{(createResult.CreatedCount == 1 ? "y" : "ies")}";
            if (createResult.SkippedCount > 0)
            {
                message += $" ({createResult.SkippedCount} skipped as duplicates)";
            }

            Snackbar.Add(message, severity);
        }
    }

    private async Task OpenEditDialog(WorkLog log)
    {
        var parameters = new DialogParameters<AddWorkLogDialog> { { x => x.Log, log } };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        var dialog = await DialogService.ShowAsync<AddWorkLogDialog>("Edit Log", parameters, options);
        var result = await dialog.Result;
        
        if (result != null && !result.Canceled)
        {
            await WorkLogService.UpdateWorkLogAsync((WorkLog)result.Data!);
            await LoadData();
            await LoadAuditData(); // Important: also refresh audit
            Snackbar.Add("Entry updated", Severity.Success);
        }
        else if (result != null && result.Canceled)
        {
            await LoadData();
            await LoadAuditData();
        }
    }

    private Task DeleteLog(int id) => DeleteLog(_logs.FirstOrDefault(log => log.Id == id));

    private async Task DeleteLog(WorkLog? log, bool isCalendarEntry = false)
    {
        if (log is not { Id: > 0 })
        {
            return;
        }

        bool? result = await DialogService.ShowMessageBoxAsync(
            isCalendarEntry ? "Delete Calendar Entry" : "Delete Entry",
            GetDeleteLogConfirmationMessage(log, isCalendarEntry),
            yesText: "Delete",
            cancelText: "Cancel");

        if (result == true)
        {
            try
            {
                _isDeletingLogs = true;
                await WorkLogService.DeleteWorkLogAsync(log.Id);
                _selectedLogIds.Remove(log.Id);
                await LoadData();
                await LoadAuditData();
                Snackbar.Add("Log deleted", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Could not delete log: {ex.Message}", Severity.Error);
            }
            finally
            {
                _isDeletingLogs = false;
            }
        }
    }

    private static string GetDeleteLogConfirmationMessage(WorkLog log, bool isCalendarEntry)
    {
        if (!isCalendarEntry)
        {
            return "Are you sure you want to delete this work log?";
        }

        var employeeName = string.IsNullOrWhiteSpace(log.Employee?.Name)
            ? "this employee"
            : log.Employee.Name;

        var dateText = log.LogDate?.ToString("MM/dd/yyyy") ?? "the selected day";

        return $"Are you sure you want to delete the calendar entry for {employeeName} on {dateText}?";
    }

    private async Task DeleteSelectedLogs()
    {
        var selectedIds = FilteredLogs
            .Where(log => _selectedLogIds.Contains(log.Id))
            .Select(log => log.Id)
            .Distinct()
            .ToArray();

        if (selectedIds.Length == 0)
        {
            Snackbar.Add("Select at least one visible log to delete.", Severity.Warning);
            return;
        }

        bool? result = await DialogService.ShowMessageBoxAsync(
            "Delete Selected Entries",
            $"Are you sure you want to delete {selectedIds.Length} selected entr{(selectedIds.Length == 1 ? "y" : "ies")}?",
            yesText: "Delete",
            cancelText: "Cancel");

        if (result == true)
        {
            try
            {
                _isDeletingLogs = true;
                var deletedCount = await WorkLogService.DeleteWorkLogsAsync(selectedIds);
                await LoadData();
                await LoadAuditData();
                ClearSelectedLogs();
                Snackbar.Add($"Deleted {deletedCount} log entr{(deletedCount == 1 ? "y" : "ies")}", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Could not delete selected logs: {ex.Message}", Severity.Error);
            }
            finally
            {
                _isDeletingLogs = false;
            }
        }
    }

    private async Task PreviousMonth()
    {
        _currentDate = _currentDate.AddMonths(-1);
        await LoadAuditData();
    }
    private async Task NextMonth()
    {
        _currentDate = _currentDate.AddMonths(1);
        await LoadAuditData();
    }

    private async Task ExportMonthToMarkdown()
    {
        var monthLogs = _logs.Where(l => l.LogDate?.Month == _currentDate.Month && l.LogDate?.Year == _currentDate.Year)
                             .OrderBy(l => l.Employee?.Name)
                             .ThenBy(l => l.LogDate)
                             .ThenBy(l => l.LogTime)
                             .ToList();

        if (!monthLogs.Any())
        {
            Snackbar.Add("No logs to export for this month.", Severity.Warning);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# {_currentDate.ToString("MMMM yyyy")}");
        sb.AppendLine();

        var groupedLogs = monthLogs.GroupBy(l => l.Employee);

        foreach (var group in groupedLogs)
        {
            var empName = group.Key?.Name ?? "Unknown";
            var initials = group.Key?.Initials ?? "??";
            
            sb.AppendLine($"## Subject {empName}");
            sb.AppendLine();
            sb.AppendLine("| Day      | Time  | Subject | Details                                          |");
            sb.AppendLine("|----------|-------|---------|--------------------------------------------------|");

            foreach (var log in group)
            {
                var dateStr = log.LogDate?.ToString("MM/dd/yy") ?? "";
                var timeStr = log.LogTime?.ToString("HH:mm") ?? "";
                var details = log.OriginalDetails ?? log.Details ?? "";
                
                // Keep the fixed width columns matching the original format
                sb.AppendLine($"| {dateStr,-8} | {timeStr,-5} | {initials,-7} | {details,-48} |");
            }
            sb.AppendLine();
        }

        var fileName = $"{_currentDate:yyyy-MM}.md";
        await JSRuntime.InvokeVoidAsync("downloadFileFromText", fileName, sb.ToString());
        Snackbar.Add($"Exported {fileName}", Severity.Success);
    }

    private const double ContextMenuViewportPadding = 8;

    private static ContextMenuViewportPosition CalculateCalendarContextMenuPosition(
        double requestedX,
        double requestedY,
        double menuWidth,
        double menuHeight,
        double viewportWidth,
        double viewportHeight,
        double padding)
    {
        var maxX = Math.Max(padding, viewportWidth - menuWidth - padding);
        var maxY = Math.Max(padding, viewportHeight - menuHeight - padding);

        return new ContextMenuViewportPosition
        {
            X = Math.Min(Math.Max(requestedX, padding), maxX),
            Y = Math.Min(Math.Max(requestedY, padding), maxY)
        };
    }

    private sealed record CalendarContextMenuState
    {
        public bool IsVisible { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public WorkLog? Log { get; init; }
        public DateOnly? TargetDate { get; init; }
    }

    private sealed class ContextMenuViewportPosition
    {
        public double X { get; init; }
        public double Y { get; init; }
    }

    private sealed class ContextMenuViewportMeasurements
    {
        public double MenuWidth { get; init; }
        public double MenuHeight { get; init; }
        public double ViewportWidth { get; init; }
        public double ViewportHeight { get; init; }
    }

    private sealed class CopiedCalendarEntry
    {
        public required int SourceLogId { get; init; }
        public DateOnly? SourceDate { get; init; }
        public TimeOnly? LogTime { get; init; }
        public required int EmployeeId { get; init; }
        public required string EmployeeName { get; init; }
        public required string MainCategory { get; init; }
        public required string SubCategory { get; init; }
        public string? Details { get; init; }
        public string? OriginalDetails { get; init; }
        public int? IncidentId { get; init; }
        public bool EarnsCompTime { get; init; }
        public bool UsesCompTime { get; init; }
        public decimal Hours { get; init; }

        public static CopiedCalendarEntry From(WorkLog log) => new()
        {
            SourceLogId = log.Id,
            SourceDate = log.LogDate,
            LogTime = log.LogTime,
            EmployeeId = log.EmployeeId,
            EmployeeName = log.Employee?.Name ?? $"Employee {log.EmployeeId}",
            MainCategory = log.MainCategory,
            SubCategory = log.SubCategory,
            Details = log.Details,
            OriginalDetails = log.OriginalDetails,
            IncidentId = log.IncidentId,
            EarnsCompTime = log.EarnsCompTime,
            UsesCompTime = log.UsesCompTime,
            Hours = log.Hours
        };
    }

    private sealed record ScheduleTabOption(string Label, string Icon);
}
