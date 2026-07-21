using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Models;
using TaskStatus = Mindflow.Api.Models.Enums.TaskStatus;

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
    public DbSet<NotificationSettings> NotificationSettings { get; set; }
    public DbSet<PushNotificationSubscription> PushNotificationSubscriptions { get; set; }
    public DbSet<PushNotificationDelivery> PushNotificationDeliveries { get; set; }
    public DbSet<GoogleCalendarConnection> GoogleCalendarConnections { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<AiSuggestion> AiSuggestions { get; set; }
    public DbSet<SuggestionAction> SuggestionActions { get; set; }
    public DbSet<AiUsageDaily> AiUsageDaily { get; set; }
    public DbSet<PomodoroSessionState> PomodoroSessions { get; set; }
    public DbSet<BrainMap> BrainMaps { get; set; }
    public DbSet<BrainNode> BrainNodes { get; set; }
    public DbSet<BrainEdge> BrainEdges { get; set; }
    public DbSet<GoalDay> GoalDays { get; set; }

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

            entity.Property(s => s.Status)
                .HasConversion<string>()
                .HasDefaultValue(TaskStatus.NotStarted);

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
            entity.HasIndex(b => new { b.UserId, b.ExternalEventId });
        });

        modelBuilder.Entity<NotificationSettings>(entity =>
        {
            entity.ToTable("notification_settings");
            entity.HasKey(s => s.UserId);
            entity.Property(s => s.MorningBriefTime).HasColumnType("time without time zone");
            entity.Property(s => s.MiddayBriefTime).HasColumnType("time without time zone");
            entity.Property(s => s.EveningSummaryTime).HasColumnType("time without time zone");
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PushNotificationSubscription>(entity =>
        {
            entity.ToTable("push_notification_subscriptions");
            entity.Property(s => s.Endpoint).HasMaxLength(2048);
            entity.Property(s => s.P256dh).HasMaxLength(255);
            entity.Property(s => s.Auth).HasMaxLength(255);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(s => s.UserId);
            entity.HasIndex(s => s.Endpoint).IsUnique();
        });

        modelBuilder.Entity<PushNotificationDelivery>(entity =>
        {
            entity.ToTable("push_notification_deliveries");
            entity.Property(d => d.DeliveryKey).HasMaxLength(255);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(d => new { d.UserId, d.DeliveryKey }).IsUnique();
            entity.HasIndex(d => d.SentAt);
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

        modelBuilder.Entity<GoogleCalendarConnection>(entity =>
        {
            entity.ToTable("google_calendar_connections");

            entity.HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(c => c.UserId).IsUnique();
            entity.HasIndex(c => c.WatchChannelId);
        });

        modelBuilder.Entity<PomodoroSessionState>(entity =>
        {
            entity.ToTable("pomodoro_sessions");

            entity.Property(session => session.Title)
                .HasMaxLength(255);

            entity.Property(session => session.Phase)
                .HasConversion<string>();

            entity.HasOne(session => session.User)
                .WithMany()
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(session => session.UserId)
                .IsUnique();
        });

        modelBuilder.Entity<BrainMap>(entity =>
        {
            entity.ToTable("brain_maps");

            entity.HasOne(map => map.User)
                .WithMany()
                .HasForeignKey(map => map.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(map => new { map.UserId, map.Key })
                .IsUnique();
        });

        modelBuilder.Entity<BrainNode>(entity =>
        {
            entity.ToTable("brain_nodes");

            entity.HasOne(node => node.BrainMap)
                .WithMany(map => map.Nodes)
                .HasForeignKey(node => node.BrainMapId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(node => new { node.BrainMapId, node.Key })
                .IsUnique();
        });

        modelBuilder.Entity<BrainEdge>(entity =>
        {
            entity.ToTable("brain_edges");

            entity.HasOne(edge => edge.BrainMap)
                .WithMany(map => map.Edges)
                .HasForeignKey(edge => edge.BrainMapId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(edge => new { edge.BrainMapId, edge.Key })
                .IsUnique();
            entity.HasIndex(edge => new { edge.BrainMapId, edge.FromNodeKey });
            entity.HasIndex(edge => new { edge.BrainMapId, edge.ToNodeKey });
        });

        modelBuilder.Entity<GoalDay>(entity =>
        {
            entity.ToTable("goal_days");

            entity.Property(day => day.DayShort)
                .HasMaxLength(20);

            entity.Property(day => day.DateLabel)
                .HasMaxLength(20);

            entity.Property(day => day.Title)
                .HasMaxLength(255);

            entity.Property(day => day.SectionsJson)
                .HasColumnName("sections")
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'[]'::jsonb");

            entity.Property(day => day.LinkedTaskIdsJson)
                .HasColumnName("linked_task_ids")
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'[]'::jsonb");

            entity.HasOne(day => day.User)
                .WithMany()
                .HasForeignKey(day => day.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(day => new { day.UserId, day.Date })
                .IsUnique();
        });
    }
}
