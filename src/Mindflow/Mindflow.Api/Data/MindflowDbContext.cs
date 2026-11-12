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
    public DbSet<TaskRecurrence> TaskRecurrences { get; set; }
    public DbSet<TaskSubtask> TaskSubtasks { get; set; }
    public DbSet<TaskTimeEntry> TaskTimeEntries { get; set; }
    public DbSet<TaskActivityEvent> TaskActivityEvents { get; set; }
    public DbSet<CalendarBlock> CalendarBlocks { get; set; }
    public DbSet<NotificationSettings> NotificationSettings { get; set; }
    public DbSet<PushNotificationSubscription> PushNotificationSubscriptions { get; set; }
    public DbSet<PushNotificationDelivery> PushNotificationDeliveries { get; set; }
    public DbSet<NotificationInboxItem> NotificationInboxItems { get; set; }
    public DbSet<GoogleCalendarConnection> GoogleCalendarConnections { get; set; }
    public DbSet<TelegramConnection> TelegramConnections { get; set; }
    public DbSet<TelegramTaskDraft> TelegramTaskDrafts { get; set; }
    public DbSet<Note> Notes { get; set; }
    public DbSet<NoteDictationJob> NoteDictationJobs { get; set; }
    public DbSet<NoteTaskSuggestion> NoteTaskSuggestions { get; set; }
    public DbSet<IntegrationToken> IntegrationTokens { get; set; }
    public DbSet<IntegrationTokenPermission> IntegrationTokenPermissions { get; set; }
    public DbSet<IntegrationAuthorizationCode> IntegrationAuthorizationCodes { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<AiSuggestion> AiSuggestions { get; set; }
    public DbSet<SuggestionAction> SuggestionActions { get; set; }
    public DbSet<AiUsageDaily> AiUsageDaily { get; set; }
    public DbSet<UserAiSettings> UserAiSettings { get; set; }
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

            entity.Property(t => t.DueTime)
                .HasColumnType("time without time zone");

            entity.Property(t => t.Tags)
                .HasColumnType("text[]")
                .HasDefaultValueSql("'{}'::text[]")
                .IsRequired();

            entity.HasIndex(t => t.UserId);
            entity.HasIndex(t => t.ProjectId);
            entity.HasIndex(t => new { t.RecurringTaskId, t.RecurrenceOccurrenceDate })
                .IsUnique()
                .HasFilter("recurring_task_id IS NOT NULL AND recurrence_occurrence_date IS NOT NULL");
            entity.HasIndex(t => new { t.UserId, t.ExternalSource, t.ExternalId })
                .IsUnique()
                .HasFilter("external_source IS NOT NULL");

            entity.HasOne(t => t.RecurringTask)
                .WithMany(t => t.RecurringInstances)
                .HasForeignKey(t => t.RecurringTaskId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TaskRecurrence>(entity =>
        {
            entity.ToTable("task_recurrences");
            entity.HasKey(recurrence => recurrence.TaskId);
            entity.Property(recurrence => recurrence.Frequency).HasConversion<string>();
            entity.Property(recurrence => recurrence.DueTime).HasColumnType("time without time zone");
            entity.Property(recurrence => recurrence.NotificationWindowStart).HasColumnType("time without time zone");
            entity.Property(recurrence => recurrence.NotificationWindowEnd).HasColumnType("time without time zone");
            entity.Property(recurrence => recurrence.DaysOfWeek)
                .HasColumnType("integer[]")
                .HasDefaultValueSql("'{}'::integer[]")
                .IsRequired();
            entity.HasOne(recurrence => recurrence.Task)
                .WithOne(task => task.Recurrence)
                .HasForeignKey<TaskRecurrence>(recurrence => recurrence.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(recurrence => new { recurrence.IsEnabled, recurrence.StartDate });
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

            entity.Property(s => s.EstimatedHours)
                .HasColumnType("numeric(6,2)");

            entity.HasIndex(s => s.TaskItemId);
            entity.HasIndex(s => new { s.TaskItemId, s.SortOrder });
            entity.HasIndex(s => s.DueDate);
        });

        modelBuilder.Entity<TaskTimeEntry>(entity =>
        {
            entity.ToTable("task_time_entries");

            entity.Property(entry => entry.TaskContent)
                .HasMaxLength(1000);

            entity.Property(entry => entry.TaskPriority)
                .HasConversion<string>();

            entity.Property(entry => entry.TaskStatus)
                .HasConversion<string>();

            entity.Property(entry => entry.Tags)
                .HasColumnType("text[]")
                .HasDefaultValueSql("'{}'::text[]")
                .IsRequired();

            entity.Property(entry => entry.EstimatedHours)
                .HasColumnType("numeric(6,2)");

            entity.Property(entry => entry.Notes)
                .HasMaxLength(2000);

            entity.HasOne<TaskItem>()
                .WithMany()
                .HasForeignKey(entry => entry.TaskId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<TaskSubtask>()
                .WithMany()
                .HasForeignKey(entry => entry.SubtaskId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(entry => entry.SubtaskId);
            entity.HasIndex(entry => entry.UserId);
            entity.HasIndex(entry => entry.TaskId);
            entity.HasIndex(entry => entry.ProjectId);
            entity.HasIndex(entry => new { entry.UserId, entry.WorkDate });
            entity.HasIndex(entry => new { entry.UserId, entry.TaskId });
            entity.HasIndex(entry => new { entry.UserId, entry.ProjectId });
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
            entity.Property(s => s.DeviceName).HasMaxLength(120);
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

        modelBuilder.Entity<NotificationInboxItem>(entity =>
        {
            entity.ToTable("notification_inbox_items");
            entity.Property(item => item.Kind).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.Title).HasMaxLength(160);
            entity.Property(item => item.Body).HasMaxLength(2000);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.UserId, item.CreatedAt });
            entity.HasIndex(item => new { item.UserId, item.ReadAt });
        });

        modelBuilder.Entity<IntegrationToken>(entity =>
        {
            entity.ToTable("integration_tokens");

            entity.Property(token => token.Name)
                .HasMaxLength(100);

            entity.Property(token => token.TokenHash)
                .HasMaxLength(64);

            entity.Property(token => token.TokenPrefix)
                .HasMaxLength(20);

            entity.HasOne(token => token.User)
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Space>()
                .WithMany()
                .HasForeignKey(token => token.SpaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(token => token.UserId);
            entity.HasIndex(token => new { token.UserId, token.ClientId });
            entity.HasIndex(token => token.TokenHash)
                .IsUnique();
        });

        modelBuilder.Entity<IntegrationTokenPermission>(entity =>
        {
            entity.ToTable("integration_token_permissions");

            entity.Property(permission => permission.Scope)
                .HasConversion<string>()
                .HasMaxLength(64);

            entity.HasOne(permission => permission.IntegrationToken)
                .WithMany(token => token.Permissions)
                .HasForeignKey(permission => permission.IntegrationTokenId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(permission => new { permission.IntegrationTokenId, permission.Scope })
                .IsUnique();
        });

        modelBuilder.Entity<IntegrationAuthorizationCode>(entity =>
        {
            entity.ToTable("integration_authorization_codes");

            entity.Property(code => code.Scopes)
                .HasColumnType("text[]")
                .IsRequired();

            entity.HasOne(code => code.User)
                .WithMany()
                .HasForeignKey(code => code.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(code => code.CodeHash)
                .IsUnique();
            entity.HasIndex(code => code.ExpiresAt);
        });

        modelBuilder.Entity<TaskActivityEvent>(entity =>
        {
            entity.ToTable("task_activity_events");

            entity.HasOne<IntegrationToken>()
                .WithMany()
                .HasForeignKey(e => e.IntegrationTokenId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.EventType)
                .HasConversion<string>();

            entity.Property(e => e.Source)
                .HasConversion<string>();

            entity.Property(e => e.ActorType)
                .HasConversion<string>();

            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb");

            entity.Property(e => e.Sequence)
                .UseIdentityByDefaultColumn();

            entity.HasIndex(e => e.Sequence)
                .IsUnique();
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

        modelBuilder.Entity<UserAiSettings>(entity =>
        {
            entity.ToTable("user_ai_settings");
            entity.Property(s => s.Provider).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(s => s.UserId).IsUnique();
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

        modelBuilder.Entity<TelegramConnection>(entity =>
        {
            entity.ToTable("telegram_connections");
            entity.HasKey(c => c.UserId);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(c => c.ChatId).IsUnique().HasFilter("chat_id IS NOT NULL");
            entity.HasIndex(c => c.LinkCodeHash);
        });

        modelBuilder.Entity<TelegramTaskDraft>(entity =>
        {
            entity.ToTable("telegram_task_drafts");
            entity.Property(d => d.Tasks).HasColumnType("jsonb");
            entity.Property(d => d.Status).HasConversion<string>().HasMaxLength(16);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(d => d.UserId);
            entity.HasIndex(d => new { d.SourceChatId, d.SourceMessageId }).IsUnique();
        });

        modelBuilder.Entity<Note>(entity =>
        {
            entity.ToTable("notes");
            entity.Property(n => n.Title).HasMaxLength(200);
            entity.Property(n => n.ClientId).HasMaxLength(64);
            entity.Property(n => n.Tags)
                .HasColumnType("text[]")
                .HasDefaultValueSql("'{}'::text[]");
            entity.Property(n => n.Source).HasConversion<string>().HasMaxLength(16);
            entity.Property(n => n.Status).HasConversion<string>().HasMaxLength(16);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(n => n.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(n => new { n.UserId, n.UpdatedAt });
            entity.HasIndex(n => new { n.UserId, n.ClientId })
                .IsUnique()
                .HasFilter("client_id IS NOT NULL");
        });

        modelBuilder.Entity<NoteDictationJob>(entity =>
        {
            entity.ToTable("note_dictation_jobs");
            entity.HasKey(j => j.NoteId);
            entity.Property(j => j.Audio).HasColumnType("bytea");
            entity.Property(j => j.AudioMimeType).HasMaxLength(64);
            entity.HasOne<Note>()
                .WithOne()
                .HasForeignKey<NoteDictationJob>(j => j.NoteId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(j => j.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NoteTaskSuggestion>(entity =>
        {
            entity.ToTable("note_task_suggestions");
            entity.Property(s => s.Title).HasMaxLength(200);
            entity.Property(s => s.DueTime).HasColumnType("time without time zone");
            entity.Property(s => s.Priority).HasConversion<string>();
            entity.Property(s => s.Subtasks).HasColumnType("jsonb");
            entity.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
            entity.HasOne<Note>()
                .WithMany()
                .HasForeignKey(s => s.NoteId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(s => new { s.NoteId, s.Order });
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
