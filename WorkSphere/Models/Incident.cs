namespace WorkSphere.Models;

public class Incident
{
    public int Id { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public bool IsClosed { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(TicketNumber) ? Title : $"{TicketNumber} - {Title}";
}
