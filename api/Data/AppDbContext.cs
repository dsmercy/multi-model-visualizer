using Microsoft.EntityFrameworkCore;
using MultiModelVisualizer.Api.Models;

namespace MultiModelVisualizer.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<LearningSession> LearningSessions => Set<LearningSession>();
    public DbSet<LearningSessionEvent> LearningSessionEvents => Set<LearningSessionEvent>();
    public DbSet<GenerationJob> GenerationJobs => Set<GenerationJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<LearningSession>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.Property(e => e.SessionId).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.UserId).HasDefaultValue(new Guid("00000000-0000-0000-0000-000000000001"));
            entity.Property(e => e.CurrentState).HasDefaultValue("Created");
            entity.Property(e => e.CreatedDate).HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedDate).HasDefaultValueSql("NOW()");

            // Store JSONB column as text
            entity.Property(e => e.VisualizationPlan).HasColumnType("jsonb");
        });

        modelBuilder.Entity<GenerationJob>(entity =>
        {
            entity.HasKey(e => e.JobId);
            entity.Property(e => e.JobId).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Status).HasDefaultValue("Queued");
            entity.Property(e => e.FallbackAttempt).HasDefaultValue(0);
            entity.Property(e => e.Progress).HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("NOW()");

            entity.HasOne(e => e.Session)
                  .WithMany(s => s.Jobs)
                  .HasForeignKey(e => e.SessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LearningSessionEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.EventId).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");

            entity.Property(e => e.EventPayload).HasColumnType("jsonb");

            entity.HasOne(e => e.Session)
                  .WithMany(s => s.Events)
                  .HasForeignKey(e => e.SessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
