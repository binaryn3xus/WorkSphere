using WorkSphere.Models;

namespace WorkSphere.Services;

public interface IWorkLogService
{
    #region Employees
    Task<IEnumerable<Employee>> GetEmployeesAsync();
    Task AddEmployeeAsync(Employee employee);
    Task UpdateEmployeeAsync(Employee employee);
    Task DeleteEmployeeAsync(int id);
    #endregion

    #region Incidents
    Task<IEnumerable<Incident>> GetIncidentsAsync();
    Task AddIncidentAsync(Incident incident);
    Task UpdateIncidentAsync(Incident incident);
    Task DeleteIncidentAsync(int id);
    Task<IEnumerable<IncidentViewModel>> GetIncidentStatsAsync();
    Task<IEnumerable<WorkLog>> GetIncidentWorkLogsAsync();
    #endregion

    #region WorkLogs
    Task<IEnumerable<WorkLog>> GetWorkLogsAsync();
    Task<IEnumerable<WorkLog>> GetWorkLogsByDateRangeAsync(DateOnly startDate, DateOnly endDate);
    Task<IEnumerable<WorkLog>> GetWorkLogsByMonthAsync(int year, int month);
    Task<int> GetWorkLogsCountByMonthAsync(int year, int month);
    Task AddWorkLogAsync(WorkLog log);
    Task<HolidayCreateResult> CreateCompanyHolidayAsync(HolidayCreateRequest request);
    Task<WorkLogRangeResult> CreateWorkLogRangeAsync(WorkLogRangeRequest request);
    Task UpdateWorkLogAsync(WorkLog log);
    Task DeleteWorkLogAsync(int id);
    Task<int> DeleteWorkLogsAsync(IEnumerable<int> ids);
    #endregion

    #region Stats & Reporting
    Task<IEnumerable<CategoryStatDto>> GetMainCategoryStatsAsync();
    Task<IEnumerable<CategoryStatDto>> GetSubCategoryStatsAsync();
    Task<IEnumerable<EmployeeStatDto>> GetEmployeeStatsAsync();
    Task<IEnumerable<DailyActivityDto>> GetDailyActivityAsync();
    Task<IEnumerable<WorkLog>> GetTodaysStatusAsync(DateOnly? targetDate = null);
    Task<IEnumerable<WorkLog>> GetThisWeeksActivityAsync();
    Task<IEnumerable<WorkLog>> GetRecentLogsAsync(int count = 5);
    Task<IEnumerable<CompTimeBalanceDto>> GetCompTimeStatsAsync();
    #endregion
}
