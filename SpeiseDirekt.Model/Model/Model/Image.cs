namespace SpeiseDirekt.Model
{
    public class Image
    {
        public Guid Id { get; set; }
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public string MimeType { get; set; } = string.Empty;
    }
}
