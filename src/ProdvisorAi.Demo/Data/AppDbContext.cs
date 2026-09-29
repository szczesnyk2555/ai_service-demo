using Microsoft.EntityFrameworkCore;

namespace ProdvisorAi.Demo.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<PromptTemplate> Prompts => Set<PromptTemplate>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<GenerationJob> Jobs => Set<GenerationJob>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Machine>().HasKey(m => m.Id);
        b.Entity<PromptTemplate>().HasKey(p => p.Id);
        b.Entity<AppSetting>().HasKey(s => s.Key);
        b.Entity<GenerationJob>(e =>
        {
            e.HasKey(j => j.Id);
            e.HasIndex(j => j.CreatedAt);
        });
    }

    // SQLite has no date type; all timestamps are written as UTC, so mark them as UTC when read back
    // (otherwise they serialize without the "Z" suffix and the browser treats them as local time).
    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    sealed class UtcDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        v => v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
