namespace Lighthouse.Core.Entities;

public class Subtask : BaseEntity
{
    public Guid TaskItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public int SortOrder { get; set; }
}
