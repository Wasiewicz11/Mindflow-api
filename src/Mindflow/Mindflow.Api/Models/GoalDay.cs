using System.ComponentModel.DataAnnotations;

namespace Mindflow.Api.Models;

public class GoalDay
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    [MaxLength(20)]
    public required string DayShort { get; set; }
    [MaxLength(20)]
    public required string DateLabel { get; set; }
    [MaxLength(255)]
    public required string Title { get; set; }
    public int MarkerLevel { get; set; }
    public required string SectionsJson { get; set; }
    public required string LinkedTaskIdsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public User? User { get; set; }
}
