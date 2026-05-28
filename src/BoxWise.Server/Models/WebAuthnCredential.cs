namespace BoxWise.Server.Models;

public class WebAuthnCredential
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string CredentialId { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public int SignCount { get; set; }
    public string DeviceName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public AppUser User { get; set; } = null!;
}
