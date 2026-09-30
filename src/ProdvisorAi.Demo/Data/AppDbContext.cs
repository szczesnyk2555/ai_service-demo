using Microsoft.EntityFrameworkCore;

namespace ProdvisorAi.Demo.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Service
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<ProdvisorMaterial> Materials => Set<ProdvisorMaterial>();
    public DbSet<SystemPrompt> SystemPrompts => Set<SystemPrompt>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<GenerationJob> Jobs => Set<GenerationJob>();

    // Simulated PRODVISOR side (elements with drawings, saved technologies)
    public DbSet<ProdvisorElement> Elements => Set<ProdvisorElement>();
    public DbSet<SavedTechnology> SavedTechnologies => Set<SavedTechnology>();
    public DbSet<TechnologyPrompt> TechnologyPrompts => Set<TechnologyPrompt>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Machine>().HasKey(m => m.Id);
        b.Entity<ProdvisorMaterial>(e => { e.HasKey(m => m.Id); e.Property(m => m.Id).ValueGeneratedNever(); });
        b.Entity<SystemPrompt>().HasKey(p => p.Id);
        b.Entity<AppSetting>().HasKey(s => s.Key);
        b.Entity<GenerationJob>(e => { e.HasKey(j => j.Id); e.HasIndex(j => j.CreatedAt); });
        b.Entity<ProdvisorElement>().HasKey(e => e.Id);
        b.Entity<SavedTechnology>().HasKey(t => t.ElementId);
        b.Entity<TechnologyPrompt>().HasKey(p => p.Id);
    }

    // SQLite has no date type; timestamps are written as UTC, so mark them as UTC when read back.
    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    sealed class UtcDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        v => v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
