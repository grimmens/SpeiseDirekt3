using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SpeiseDirekt.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        private readonly IUserIdProvider userIdProvider;
        private string UserId => userIdProvider.GetUserId();
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IUserIdProvider userIdProvider) : base(options)
        {
            this.userIdProvider = userIdProvider;
            ChangeTracker.StateChanged += UpdateApplicationUserId;
            ChangeTracker.Tracked += UpdateApplicationUserId;

        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            var userId = userIdProvider.GetUserId();
            builder.Entity<Category>().HasIndex(e => new { e.Id, e.ApplicationUserId });
            builder.Entity<MenuItem>().HasIndex(e => new { e.Id, e.ApplicationUserId });
            builder.Entity<MenuItem>().HasQueryFilter(e => e.ApplicationUserId == Guid.Parse(UserId));
            builder.Entity<Category>().HasQueryFilter(e => e.ApplicationUserId == Guid.Parse(UserId));
            builder.Entity<Menu>().HasQueryFilter(e => e.ApplicationUserId == Guid.Parse(UserId));
            builder.Entity<QRCode>().HasQueryFilter(e => e.ApplicationUserId == Guid.Parse(UserId));
            builder.Entity<TimeTableEntry>().HasQueryFilter(e => e.ApplicationUserId == Guid.Parse(UserId));
            builder.Entity<CalendarEntry>().HasQueryFilter(e => e.ApplicationUserId == Guid.Parse(UserId));
            builder.Entity<Allergen>().HasQueryFilter(e => e.ApplicationUserId == Guid.Parse(UserId));

            // Allergen: many-to-many with MenuItem via join table
            builder.Entity<MenuItem>()
                .HasMany(mi => mi.Allergens)
                .WithMany(a => a.MenuItems)
                .UsingEntity("MenuItemAllergen");

            builder.Entity<Allergen>(entity =>
            {
                entity.HasIndex(e => new { e.Code, e.MenuId }).IsUnique();
                entity.HasOne(e => e.Menu)
                      .WithMany(m => m.Allergens)
                      .HasForeignKey(e => e.MenuId)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            builder.Entity<ApplicationUser>()
                   .HasOne(u => u.TenantSubscription)
                   .WithOne(ts => ts.Tenant)
                   .HasForeignKey<TenantSubscription>(ts => ts.TenantId)
                   .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<TranslationCache>(entity =>
            {
                entity.HasIndex(e => new { e.SourceLanguage, e.TargetLanguage });
                entity.HasIndex(e => e.LastUsedAt);
                entity.HasIndex(e => e.CreatedAt);
            });

            // Configure tracking entities
            builder.Entity<MenuView>(entity =>
            {
                entity.HasIndex(e => e.SessionId);
                entity.HasIndex(e => e.MenuId);
                entity.HasIndex(e => e.ViewedAt);
                entity.HasIndex(e => new { e.SessionId, e.MenuId, e.ViewedAt });

                // Configure foreign key relationships to avoid cascade conflicts
                entity.HasOne(e => e.Menu)
                      .WithMany()
                      .HasForeignKey(e => e.MenuId)
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(e => e.QRCode)
                      .WithMany()
                      .HasForeignKey(e => e.QRCodeId)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            builder.Entity<MenuItemClick>(entity =>
            {
                entity.HasIndex(e => e.SessionId);
                entity.HasIndex(e => e.MenuItemId);
                entity.HasIndex(e => e.MenuId);
                entity.HasIndex(e => e.ClickedAt);
                entity.HasIndex(e => new { e.SessionId, e.MenuItemId, e.ClickedAt });

                // Configure foreign key relationships to avoid cascade conflicts
                entity.HasOne(e => e.MenuItem)
                      .WithMany()
                      .HasForeignKey(e => e.MenuItemId)
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(e => e.Menu)
                      .WithMany()
                      .HasForeignKey(e => e.MenuId)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            // Self-referencing FK: sub-accounts point to owner
            builder.Entity<ApplicationUser>()
                .HasOne(u => u.TenantOwner)
                .WithMany()
                .HasForeignKey(u => u.TenantOwnerId)
                .OnDelete(DeleteBehavior.NoAction);

            // TenantUser relationships
            builder.Entity<TenantUser>(entity =>
            {
                entity.HasOne(tu => tu.ApplicationUser)
                    .WithMany()
                    .HasForeignKey(tu => tu.ApplicationUserId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(tu => tu.TenantOwner)
                    .WithMany(u => u.TenantUsers)
                    .HasForeignKey(tu => tu.TenantOwnerId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(tu => new { tu.ApplicationUserId, tu.TenantOwnerId }).IsUnique();
            });

            base.OnModelCreating(builder);
        }
        public DbSet<Allergen> Allergens { get; set; }
        public DbSet<MenuItem> MenuItems { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Menu> Menus { get; set; }
        public DbSet<QRCode> QRCodes { get; set; }
        public DbSet<TimeTableEntry> TimeTableEntries { get; set; }
        public DbSet<CalendarEntry> CalendarEntries { get; set; }
        public DbSet<TenantSubscription> TenantSubscriptions { get; set; }
        public DbSet<TranslationCache> TranslationCaches { get; set; }
        public DbSet<Image> Images { get; set; }
        public DbSet<MenuView> MenuViews { get; set; }
        public DbSet<MenuItemClick> MenuItemClicks { get; set; }
        public DbSet<TenantUser> TenantUsers { get; set; }
        private void UpdateApplicationUserId(object sender, EntityEntryEventArgs e)
        {
            if (e.Entry.Entity is IAppUserEntity entity)
            {
                var userId = userIdProvider.GetUserId();

                // Only update if we have a valid user ID (not empty GUID)
                if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var parsedUserId) && parsedUserId != Guid.Empty)
                {
                    switch (e.Entry.State)
                    {
                        case EntityState.Modified:
                            entity.ApplicationUserId = parsedUserId;
                            break;
                        case EntityState.Added:
                            entity.ApplicationUserId = parsedUserId;
                            break;
                    }
                }
                else
                {
                    // If we don't have a valid user ID, this is a problem
                    throw new InvalidOperationException("Cannot save entity: User is not authenticated or user ID is invalid.");
                }
            }
        }
    }
}
