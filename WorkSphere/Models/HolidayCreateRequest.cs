namespace WorkSphere.Models;

public class HolidayCreateRequest
{
    public DateOnly HolidayDate { get; set; }
    public string HolidayName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<int> EmployeeIds { get; set; } = new();
}
