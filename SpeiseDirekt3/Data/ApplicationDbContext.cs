using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt3.Model;
using SpeiseDirekt3.ServiceInterface;
using SpeiseDirekt3.Components.Account;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SpeiseDirekt3.Data
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

            builder.Entity<ApplicationUser>()
                   .HasOne(u => u.TenantSubscription)
                   .WithOne(ts => ts.Tenant)
                   .HasForeignKey<TenantSubscription>(ts => ts.TenantId)
                   .OnDelete(DeleteBehavior.Cascade);

            base.OnModelCreating(builder);
        }
        public DbSet<MenuItem> MenuItems { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Menu> Menus { get; set; }
        public DbSet<QRCode> QRCodes { get; set; }
        public DbSet<TimeTableEntry> TimeTableEntries { get; set; }
        public DbSet<TenantSubscription> TenantSubscriptions { get; set; }
        private void UpdateApplicationUserId(object sender, EntityEntryEventArgs e)
        {
            if (e.Entry.Entity is IAppUserEntity entity)
            {
                switch (e.Entry.State)
                {
                    case EntityState.Modified:
                        entity.ApplicationUserId = new Guid(userIdProvider.GetUserId());
                        break;
                    case EntityState.Added:
                        entity.ApplicationUserId = new Guid(userIdProvider.GetUserId());
                        break;
                }
            }
        }
    }
}
