namespace WorkSphere.Models;

public class WorkLog
{
    public int Id { get; set; }
    public DateOnly? LogDate { get; set; }
    public TimeOnly? LogTime { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public string MainCategory { get; set; } = "Work";
    public string SubCategory { get; set; } = "Office";
    public string? Details { get; set; }
    public string? OriginalDetails { get; set; }
    public int? IncidentId { get; set; }
    public Incident? Incident { get; set; }
    public bool EarnsCompTime { get; set; }
    public bool UsesCompTime { get; set; }
    public decimal Hours { get; set; }
}
