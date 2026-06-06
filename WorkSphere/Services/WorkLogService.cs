using Dapper;
using Npgsql;
using WorkSphere.Models;

namespace WorkSphere.Services;

public class WorkLogService
{
    private readonly string _connectionString;
    private readonly ILogger<WorkLogService> _logger;

    public WorkLogService(IConfiguration configuration, ILogger<WorkLogService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    private NpgsqlConnection CreateConnection() => new NpgsqlConnection(_connectionString);

    #region Employees
    public async Task<IEnumerable<Employee>> GetEmployeesAsync()
    {
        const string sql = "SELECT * FROM Employees ORDER BY Name";
        using var connection = CreateConnection();
        return await connection.QueryAsync<Employee>(sql);
    }

    public async Task AddEmployeeAsync(Employee employee)
    {
        _logger.LogInformation("Adding new employee: {Name}", employee.Name);
        const string sql = "INSERT INTO Employees (Name, Initials) VALUES (@Name, @Initials)";
        using var connection = CreateConnection();
        await connection.ExecuteAsync(sql, employee);
    }

    public async Task DeleteEmployeeAsync(int id)
    {
        _logger.LogInformation("Deleting employee ID: {Id}", id);
        const string sql = "DELETE FROM Employees WHERE Id = @Id";
        using var connection = CreateConnection();
        await connection.ExecuteAsync(sql, new { Id = id });
    }
    #endregion

    #region Incidents
    public async Task<IEnumerable<Incident>> GetIncidentsAsync()
    {
        const string sql = "SELECT * FROM Incidents ORDER BY StartedAt DESC";
        using var connection = CreateConnection();
        return await connection.QueryAsync<Incident>(sql);
    }

    public async Task AddIncidentAsync(Incident incident)
    {
        _logger.LogInformation("Adding new incident: {Title} ({TicketNumber})", incident.Title, incident.TicketNumber);
        const string sql = @"
            INSERT INTO Incidents (TicketNumber, Title, Details, StartedAt, EndedAt, IsClosed)
            VALUES (@TicketNumber, @Title, @Details, @StartedAt, @EndedAt, @IsClosed)";
        using var connection = CreateConnection();
        await connection.ExecuteAsync(sql, incident);
    }

    public async Task UpdateIncidentAsync(Incident incident)
    {
        _logger.LogInformation("Updating incident ID: {Id} - {Title}", incident.Id, incident.Title);
        const string sql = @"
            UPDATE Incidents 
            SET TicketNumber = @TicketNumber, Title = @Title, Details = @Details, 
                StartedAt = @StartedAt, EndedAt = @EndedAt, IsClosed = @IsClosed 
            WHERE Id = @Id";
        using var connection = CreateConnection();
        await connection.ExecuteAsync(sql, incident);
    }

    public async Task DeleteIncidentAsync(int id)
    {
        _logger.LogInformation("Deleting incident ID: {Id}", id);
        const string sql = "DELETE FROM Incidents WHERE Id = @Id";
        using var connection = CreateConnection();
        await connection.ExecuteAsync(sql, new { Id = id });
    }
    #endregion

    public async Task<IEnumerable<IncidentViewModel>> GetIncidentStatsAsync()
    {
        const string sql = @"
            SELECT i.Id, i.TicketNumber, i.Title, i.Details, i.StartedAt, i.EndedAt, i.IsClosed,
                   COALESCE(SUM(l.Hours), 0) as TotalCompHours
            FROM Incidents i
            LEFT JOIN WorkLogs l ON i.Id = l.IncidentId AND l.EarnsCompTime = TRUE
            GROUP BY i.Id, i.TicketNumber, i.Title, i.Details, i.StartedAt, i.EndedAt, i.IsClosed
            ORDER BY i.StartedAt DESC";
        using var connection = CreateConnection();
        return await connection.QueryAsync<IncidentViewModel>(sql);
    }

    #region WorkLogs
    public async Task<IEnumerable<WorkLog>> GetWorkLogsAsync()
    {
        const string sql = @"
            SELECT l.*, e.* 
            FROM WorkLogs l 
            JOIN Employees e ON l.EmployeeId = e.Id 
            ORDER BY LogDate DESC, LogTime DESC";
        using var connection = CreateConnection();
        return await connection.QueryAsync<WorkLog, Employee, WorkLog>(sql, (log, employee) =>
        {
            log.Employee = employee;
            return log;
        });
    }

    public async Task AddWorkLogAsync(WorkLog log)
    {
        _logger.LogInformation("Adding work log for employee {EmployeeId}: {MainCategory}/{SubCategory}", log.EmployeeId, log.MainCategory, log.SubCategory);
        const string sql = @"
            INSERT INTO WorkLogs (LogDate, LogTime, EmployeeId, MainCategory, SubCategory, Details, OriginalDetails, IncidentId, EarnsCompTime, UsesCompTime, Hours)
            VALUES (@LogDate, @LogTime, @EmployeeId, @MainCategory, @SubCategory, @Details, @OriginalDetails, @IncidentId, @EarnsCompTime, @UsesCompTime, @Hours)";
        
        using var connection = CreateConnection();
        await connection.ExecuteAsync(sql, log);
    }

    public async Task UpdateWorkLogAsync(WorkLog log)
    {
        _logger.LogInformation("Updating work log ID: {Id}", log.Id);
        const string sql = @"
            UPDATE WorkLogs 
            SET LogDate = @LogDate, LogTime = @LogTime, EmployeeId = @EmployeeId, 
                MainCategory = @MainCategory, SubCategory = @SubCategory, 
                Details = @Details, OriginalDetails = @OriginalDetails,
                IncidentId = @IncidentId, EarnsCompTime = @EarnsCompTime, 
                UsesCompTime = @UsesCompTime, Hours = @Hours
            WHERE Id = @Id";
        
        using var connection = CreateConnection();
        await connection.ExecuteAsync(sql, log);
    }

    public async Task DeleteWorkLogAsync(int id)
    {
        _logger.LogInformation("Deleting work log ID: {Id}", id);
        const string sql = "DELETE FROM WorkLogs WHERE Id = @Id";
        using var connection = CreateConnection();
        await connection.ExecuteAsync(sql, new { Id = id });
    }

    public async Task<IEnumerable<CategoryStatDto>> GetMainCategoryStatsAsync()
    {
        const string sql = @"
            SELECT MainCategory as Name, CAST(COUNT(*) AS INT) as Count 
            FROM WorkLogs 
            GROUP BY MainCategory 
            ORDER BY Count DESC";
        using var connection = CreateConnection();
        return await connection.QueryAsync<CategoryStatDto>(sql);
    }

    public async Task<IEnumerable<CategoryStatDto>> GetSubCategoryStatsAsync()
    {
        const string sql = @"
            SELECT SubCategory as Name, CAST(COUNT(*) AS INT) as Count 
            FROM WorkLogs 
            GROUP BY SubCategory 
            ORDER BY Count DESC";
        using var connection = CreateConnection();
        return await connection.QueryAsync<CategoryStatDto>(sql);
    }

    public async Task<IEnumerable<EmployeeStatDto>> GetEmployeeStatsAsync()
    {
        const string sql = @"
            SELECT e.Name, CAST(COUNT(l.Id) AS INT) as Count 
            FROM Employees e
            LEFT JOIN WorkLogs l ON e.Id = l.EmployeeId
            GROUP BY e.Name
            ORDER BY Count DESC";
        using var connection = CreateConnection();
        return await connection.QueryAsync<EmployeeStatDto>(sql);
    }

    public async Task<IEnumerable<DailyActivityDto>> GetDailyActivityAsync()
    {
        const string sql = @"
            SELECT LogDate as Date, CAST(COUNT(*) AS INT) as Count 
            FROM WorkLogs 
            WHERE LogDate > CURRENT_DATE - INTERVAL '30 days'
            GROUP BY LogDate 
            ORDER BY LogDate";
        using var connection = CreateConnection();
        return await connection.QueryAsync<DailyActivityDto>(sql);
    }

    public async Task<IEnumerable<WorkLog>> GetTodaysStatusAsync()
    {
        const string sql = @"
            SELECT l.*, e.* 
            FROM WorkLogs l 
            JOIN Employees e ON l.EmployeeId = e.Id 
            WHERE l.LogDate = @Today
            ORDER BY l.LogTime DESC";
        
        using var connection = CreateConnection();
        var today = DateOnly.FromDateTime(DateTime.Today);
        return await connection.QueryAsync<WorkLog, Employee, WorkLog>(sql, (log, employee) =>
        {
            log.Employee = employee;
            return log;
        }, new { Today = today });
    }

    public async Task<IEnumerable<WorkLog>> GetThisWeeksActivityAsync()
    {
        const string sql = @"
            SELECT l.*, e.* 
            FROM WorkLogs l 
            JOIN Employees e ON l.EmployeeId = e.Id 
            WHERE l.LogDate >= @StartOfWeek
            ORDER BY l.LogDate DESC, l.LogTime DESC";
        
        using var connection = CreateConnection();
        var startOfWeek = DateOnly.FromDateTime(DateTime.Today.AddDays(-7));
        return await connection.QueryAsync<WorkLog, Employee, WorkLog>(sql, (log, employee) =>
        {
            log.Employee = employee;
            return log;
        }, new { StartOfWeek = startOfWeek });
    }

    public async Task<IEnumerable<WorkLog>> GetRecentLogsAsync(int count = 5)
    {
        const string sql = @"
            SELECT l.*, e.* 
            FROM WorkLogs l 
            JOIN Employees e ON l.EmployeeId = e.Id 
            ORDER BY l.LogDate DESC, l.LogTime DESC
            LIMIT @Count";
        using var connection = CreateConnection();
        return await connection.QueryAsync<WorkLog, Employee, WorkLog>(sql, (log, employee) =>
        {
            log.Employee = employee;
            return log;
        }, new { Count = count });
    }

    public async Task<IEnumerable<CompTimeBalanceDto>> GetCompTimeStatsAsync()
    {
        const string sql = @"
            SELECT 
                e.Id,
                e.Name, 
                SUM(CASE WHEN l.EarnsCompTime = TRUE THEN l.Hours ELSE 0 END) as Earned,
                SUM(CASE WHEN l.UsesCompTime = TRUE THEN l.Hours ELSE 0 END) as Used,
                SUM(CASE WHEN l.EarnsCompTime = TRUE THEN l.Hours ELSE 0 END) - 
                SUM(CASE WHEN l.UsesCompTime = TRUE THEN l.Hours ELSE 0 END) as Balance
            FROM Employees e
            LEFT JOIN WorkLogs l ON e.Id = l.EmployeeId
            GROUP BY e.Id, e.Name
            ORDER BY Balance DESC";
        using var connection = CreateConnection();
        return await connection.QueryAsync<CompTimeBalanceDto>(sql);
    }
    #endregion
}

public record CompTimeBalanceDto(int Id, string Name, decimal Earned, decimal Used, decimal Balance);
public record CategoryStatDto(string Name, int Count);
public record EmployeeStatDto(string Name, int Count);
public record DailyActivityDto(DateOnly Date, int Count);
