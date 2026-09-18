namespace FileStorageApi.DTOs;

/// <summary>Summary of a logical file, used in list responses.</summary>
public record FileSummaryDto(
    Guid Id,
    string Name,
    int LatestVersion,
    int VersionCount,
    long LatestSizeBytes,
    string LatestContentType,
    DateTimeOffset CreatedAt,
    DateTimeOffset LatestUploadedAt);

/// <summary>Metadata for a single version, returned after upload and in version history listings.</summary>
public record FileVersionDto(
    Guid FileId,
    string Name,
    int VersionNumber,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    string? UploadedBy);
