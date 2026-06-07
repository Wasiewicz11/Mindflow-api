namespace Mindflow.Api.Models;

public class AiUsageDaily
{
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    public int AiCalls { get; set; }
}
