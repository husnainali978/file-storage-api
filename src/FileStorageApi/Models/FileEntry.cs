namespace FileStorageApi.Models;

/// <summary>
/// A logical file identified by a unique <see cref="Name"/>. Uploading a new file under the
/// same name does not overwrite anything — it adds a new <see cref="FileVersion"/> to
/// <see cref="Versions"/>, so the full history stays retrievable.
/// </summary>
public class FileEntry
{
    public Guid Id { get; set; }

    /// <summary>Logical, human-chosen name that groups versions together (case-insensitive, unique).</summary>
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public List<FileVersion> Versions { get; set; } = new();
}
