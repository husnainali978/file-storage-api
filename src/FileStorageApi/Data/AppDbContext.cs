using FileStorageApi.Models;
using Microsoft.EntityFrameworkCore;

namespace FileStorageApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<FileEntry> FileEntries => Set<FileEntry>();

    public DbSet<FileVersion> FileVersions => Set<FileVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileEntry>(entity =>
        {
            entity.HasIndex(f => f.Name).IsUnique();
            entity.Property(f => f.Name).IsRequired().HasMaxLength(512);

            entity.HasMany(f => f.Versions)
                .WithOne(v => v.FileEntry)
                .HasForeignKey(v => v.FileEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FileVersion>(entity =>
        {
            entity.HasIndex(v => new { v.FileEntryId, v.VersionNumber }).IsUnique();
            entity.Property(v => v.OriginalFileName).IsRequired().HasMaxLength(1024);
            entity.Property(v => v.ContentType).IsRequired().HasMaxLength(255);
            entity.Property(v => v.StorageKey).IsRequired().HasMaxLength(1024);
        });
    }
}
