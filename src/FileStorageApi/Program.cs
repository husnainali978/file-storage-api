using FileStorageApi.Data;
using FileStorageApi.Services;
using FileStorageApi.Storage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// EF Core + SQLite for metadata (file names, versions, sizes, timestamps).
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// Storage backend for the actual bytes. Everything else depends on IFileStorage only, so
// swapping LocalDiskFileStorage for e.g. an Azure Blob / S3 implementation later is a one-line
// change here — no controller or service code needs to move.
builder.Services.Configure<LocalDiskStorageOptions>(
    builder.Configuration.GetSection(LocalDiskStorageOptions.SectionName));
builder.Services.AddSingleton<IFileStorage, LocalDiskFileStorage>();

builder.Services.AddScoped<FileVersioningService>();

// Multipart form uploads default to a 128MB limit in Kestrel's form reader; raise it so large
// file uploads aren't rejected before they even reach the controller. The upload path itself
// streams straight to disk (see LocalDiskFileStorage.SaveAsync), so memory use stays flat
// regardless of file size.
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = long.MaxValue;
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = null; // unlimited; rely on streaming + disk space instead
});

var app = builder.Build();

// Apply any pending EF Core migrations and ensure the database file exists at startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory-based integration testing.
public partial class Program;
