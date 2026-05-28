namespace BoxWise.Server.Models;

public class Item
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? PhotoPath { get; set; }
    public string? ThumbPath { get; set; }
    public string? MediumPath { get; set; }
    public int? LocationId { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid Version { get; set; }

    public Location? Location { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public AppUser? UpdatedByUser { get; set; }
    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
}
