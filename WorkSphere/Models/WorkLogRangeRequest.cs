using System;

namespace WorkSphere.Models;

public class WorkLogRangeRequest
{
    public int EmployeeId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public TimeOnly? LogTime { get; set; }
    public string MainCategory { get; set; } = "Work";
    public string SubCategory { get; set; } = "Office";
    public string? Details { get; set; }
    public bool EarnsCompTime { get; set; }
    public bool UsesCompTime { get; set; }
    public decimal Hours { get; set; }
    public bool SkipWeekends { get; set; } = true;
    public bool SkipHolidays { get; set; } = true;
}
