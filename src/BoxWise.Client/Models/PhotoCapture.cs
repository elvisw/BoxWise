namespace BoxWise.Client.Models;

public record PhotoCapture(string FileName, string ContentType, byte[] Bytes)
{
    public Stream OpenReadStream() => new MemoryStream(Bytes);
}
