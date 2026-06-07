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
    public DbSet<ProjectTag> ProjectTags { get; set; }
    public DbSet<TaskItem> Tasks { get; set; }
    public DbSet<TaskSubtask> TaskSubtasks { get; set; }
    public DbSet<TaskActivityEvent> TaskActivityEvents { get; set; }
    public DbSet<CalendarBlock> CalendarBlocks { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<AiSuggestion> AiSuggestions { get; set; }
    public DbSet<SuggestionAction> SuggestionActions { get; set; }
    public DbSet<AiUsageDaily> AiUsageDaily { get; set; }

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

        modelBuilder.Entity<ProjectTag>(entity =>
        {
            entity.ToTable("project_tags");

            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(t => new { t.ProjectId, t.Name })
                .IsUnique();
        });

        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.ToTable("tasks");

            entity.Property(t => t.Priority)
                .HasConversion<string>();

            entity.Property(t => t.EstimatedHours)
                .HasColumnType("numeric(6,2)");

            entity.Property(t => t.Tags)
                .HasColumnType("text[]")
                .HasDefaultValueSql("'{}'::text[]")
                .IsRequired();

            entity.HasIndex(t => t.UserId);
            entity.HasIndex(t => t.ProjectId);
        });

        modelBuilder.Entity<TaskSubtask>(entity =>
        {
            entity.ToTable("task_subtasks");

            entity.HasOne<TaskItem>()
                .WithMany(t => t.Subtasks)
                .HasForeignKey(s => s.TaskItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(s => s.TaskItemId);
            entity.HasIndex(s => new { s.TaskItemId, s.SortOrder });
            entity.HasIndex(s => s.DueDate);
        });

        modelBuilder.Entity<CalendarBlock>(entity =>
        {
            entity.ToTable("calendar_blocks");

            entity.Property(b => b.Title)
                .HasMaxLength(255);

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

        modelBuilder.Entity<AiSuggestion>(entity =>
        {
            entity.ToTable("ai_suggestions");

            entity.Property(s => s.Status)
                .HasConversion<string>();

            entity.HasIndex(s => new { s.UserId, s.Status });
            entity.HasIndex(s => new { s.UserId, s.GeneratedForDate });
        });

        modelBuilder.Entity<SuggestionAction>(entity =>
        {
            entity.ToTable("suggestion_actions");

            entity.Property(a => a.ActionType)
                .HasConversion<string>();

            entity.Property(a => a.Payload)
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb");

            entity.HasOne<AiSuggestion>()
                .WithMany(s => s.Actions)
                .HasForeignKey(a => a.SuggestionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<TaskItem>()
                .WithMany()
                .HasForeignKey(a => a.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(a => a.SuggestionId);
        });

        modelBuilder.Entity<AiUsageDaily>(entity =>
        {
            entity.ToTable("ai_usage_daily");
            entity.HasKey(u => new { u.UserId, u.Date });
        });
    }
}
