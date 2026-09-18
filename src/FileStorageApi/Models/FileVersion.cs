namespace FileStorageApi.Models;

/// <summary>
/// One immutable version of a <see cref="FileEntry"/>. The actual bytes live in whatever
/// <see cref="Storage.IFileStorage"/> implementation is configured, addressed by
/// <see cref="StorageKey"/>; this row only holds metadata.
/// </summary>
public class FileVersion
{
    public Guid Id { get; set; }

    public Guid FileEntryId { get; set; }

    public FileEntry? FileEntry { get; set; }

    /// <summary>1-based, increasing per <see cref="FileEntryId"/>.</summary>
    public int VersionNumber { get; set; }

    /// <summary>Original file name as uploaded by the client (for Content-Disposition on download).</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    /// <summary>Key passed to <see cref="Storage.IFileStorage"/> to read/write this version's bytes.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Optional identity of who uploaded this version, if supplied by the caller.</summary>
    public string? UploadedBy { get; set; }
}
