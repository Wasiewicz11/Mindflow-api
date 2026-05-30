using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Models;

namespace Mindflow.Api.Data;

public class MindflowDbContext(DbContextOptions<MindflowDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<UserIdentity> UserIdentities { get; set; }
    public DbSet<Space> Spaces { get; set; }
    public DbSet<SpaceMember> SpaceMembers { get; set; }
    public DbSet<SpaceInvitation> SpaceInvitations { get; set; }
    public DbSet<Project> Projects { get; set; }
    public DbSet<TaskItem> Tasks { get; set; }
    public DbSet<TaskActivityEvent> TaskActivityEvents { get; set; }
    public DbSet<CalendarBlock> CalendarBlocks { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserIdentity>()
            .HasOne(ui => ui.User)
            .WithMany()
            .HasForeignKey(ui => ui.UserId);

        modelBuilder.Entity<UserIdentity>()
            .HasIndex(ui => new { ui.Provider, ui.ProviderUserId })
            .IsUnique();
        
        modelBuilder.Entity<UserIdentity>()
            .Property(ui => ui.Provider)
            .HasConversion<string>();

        modelBuilder.Entity<SpaceMember>()
            .Property(m => m.Role)
            .HasConversion<string>();
        
        modelBuilder.Entity<RefreshToken>()
            .HasKey(rt => rt.Token);

        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.ToTable("tasks");

            entity.Property(t => t.Priority)
                .HasConversion<string>();

            entity.HasIndex(t => t.UserId);
            entity.HasIndex(t => t.ProjectId);
        });

        modelBuilder.Entity<CalendarBlock>(entity =>
        {
            entity.ToTable("calendar_blocks");

            entity.Property(b => b.Provider)
                .HasConversion<string>();

            entity.Property(b => b.SyncStatus)
                .HasConversion<string>();

            entity.HasIndex(b => b.UserId);
            entity.HasIndex(b => b.TaskId);
            entity.HasIndex(b => new { b.UserId, b.StartAt });
        });

        modelBuilder.Entity<TaskActivityEvent>(entity =>
        {
            entity.ToTable("task_activity_events");

            entity.Property(e => e.EventType)
                .HasConversion<string>();

            entity.Property(e => e.Source)
                .HasConversion<string>();

            entity.Property(e => e.ActorType)
                .HasConversion<string>();

            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb");

            entity.HasIndex(e => new { e.UserId, e.OccurredAt });
            entity.HasIndex(e => new { e.UserId, e.EventType, e.OccurredAt });
            entity.HasIndex(e => new { e.TaskId, e.OccurredAt });
            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.SpaceId);
        });
    }
}
