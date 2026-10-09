namespace StoatTote;

/// <summary>An image sent alongside a prompt to a vision-capable model.</summary>
internal record ImageAttachment(string Name, string MimeType, string Base64)
{
    // Large images cost a lot of tokens and most servers reject huge uploads
    public const long MaxBytes = 20 * 1024 * 1024;

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
    };

    public string DataUrl => $"data:{MimeType};base64,{Base64}";

    public static bool IsImageFile(string path) => MimeTypes.ContainsKey(Path.GetExtension(path));

    /// <summary>Loads an image file, throwing with a readable message if it can't be used.</summary>
    public static ImageAttachment Load(string path, string name)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}");
        if (!MimeTypes.TryGetValue(Path.GetExtension(path), out var mime))
            throw new InvalidOperationException($"Unsupported image type (use {string.Join(", ", MimeTypes.Keys)})");
        if (new FileInfo(path).Length > MaxBytes)
            throw new InvalidOperationException($"Image is larger than {MaxBytes / 1024 / 1024} MB");

        return new ImageAttachment(name, mime, Convert.ToBase64String(File.ReadAllBytes(path)));
    }
}
