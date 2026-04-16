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
        
    }
}
