namespace WorkSphere.Models;

public class IncidentViewModel
{
    public int Id { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public bool IsClosed { get; set; }
    public decimal TotalCompHours { get; set; }
}
