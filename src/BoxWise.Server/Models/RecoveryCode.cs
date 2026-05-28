namespace BoxWise.Server.Models;

public class RecoveryCode
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string CodeHash { get; set; } = ""; // SHA-256 hash
    public AppUser User { get; set; } = null!;
}
