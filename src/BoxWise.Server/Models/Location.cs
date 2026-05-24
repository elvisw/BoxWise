namespace BoxWise.Server.Models;

public class Location
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public int SortOrder { get; set; }

    public Location? Parent { get; set; }
    public ICollection<Location> Children { get; set; } = new List<Location>();
}
