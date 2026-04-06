using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests;

public static class TestSeedData
{
    public static readonly Guid Menu1Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid Menu2Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab");
    public static readonly Guid Category1Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly Guid Category2Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbc");
    public static readonly Guid MenuItem1Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    public static readonly Guid MenuItem2Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccd");
    public static readonly Guid MenuItem3Id = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccce");
    public static readonly Guid QrCodeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    public static readonly Guid TimeTableEntryId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    public static readonly Guid CalendarEntryId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    public static readonly Guid ImageId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    public static readonly Guid MenuView1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid MenuView2Id = Guid.Parse("11111111-1111-1111-1111-111111111112");
    public static readonly Guid MenuView3Id = Guid.Parse("11111111-1111-1111-1111-111111111113");
    public static readonly Guid MenuItemClick1Id = Guid.Parse("22222222-2222-2222-2222-222222222221");
    public static readonly Guid MenuItemClick2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid DirectQrCodeId = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddde");
    public static readonly string TestSessionId = "test-session-001";

    public static async Task SeedAsync(ApplicationDbContext context, string userId)
    {
        var appUserId = Guid.Parse(userId);

        // ApplicationUser
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = "testuser@test.com",
            NormalizedUserName = "TESTUSER@TEST.COM",
            Email = "testuser@test.com",
            NormalizedEmail = "TESTUSER@TEST.COM",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        context.Users.Add(user);

        // Menus
        var menu1 = new Menu
        {
            Id = Menu1Id,
            Name = "Lunch Menu",
            Description = "Daily lunch specials",
            Theme = DesignTheme.Modern,
            Language = MenuLanguage.German,
            ApplicationUserId = appUserId
        };
        var menu2 = new Menu
        {
            Id = Menu2Id,
            Name = "Dinner Menu",
            Description = "Evening dining options",
            Theme = DesignTheme.Elegant,
            Language = MenuLanguage.English,
            ApplicationUserId = appUserId
        };
        context.Menus.AddRange(menu1, menu2);

        // Categories
        var category1 = new Category
        {
            Id = Category1Id,
            Name = "Starters",
            MenuId = Menu1Id,
            ApplicationUserId = appUserId
        };
        var category2 = new Category
        {
            Id = Category2Id,
            Name = "Main Courses",
            MenuId = Menu2Id,
            ApplicationUserId = appUserId
        };
        context.Categories.AddRange(category1, category2);

        // MenuItems
        context.MenuItems.AddRange(
            new MenuItem
            {
                Id = MenuItem1Id,
                Name = "Caesar Salad",
                Description = "Fresh romaine lettuce with caesar dressing",
                Allergens = "Dairy, Gluten",
                Price = 8.50m,
                CategoryId = Category1Id,
                ApplicationUserId = appUserId
            },
            new MenuItem
            {
                Id = MenuItem2Id,
                Name = "Tomato Soup",
                Description = "Homemade tomato soup",
                Allergens = "",
                Price = 6.00m,
                CategoryId = Category1Id,
                ApplicationUserId = appUserId
            },
            new MenuItem
            {
                Id = MenuItem3Id,
                Name = "Grilled Salmon",
                Description = "Atlantic salmon with vegetables",
                Allergens = "Fish",
                Price = 22.00m,
                CategoryId = Category2Id,
                ApplicationUserId = appUserId
            }
        );

        // QRCode
        var qrCode = new QRCode
        {
            Id = QrCodeId,
            Title = "Table 1",
            MenuId = Menu1Id,
            IsTimeTableBased = true,
            IsCalendarBased = true,
            ApplicationUserId = appUserId
        };
        context.QRCodes.Add(qrCode);

        // Direct QRCode (no time-based resolution) for MenuDisplay tests
        var directQrCode = new QRCode
        {
            Id = DirectQrCodeId,
            Title = "Table 2 - Direct",
            MenuId = Menu1Id,
            IsTimeTableBased = false,
            IsCalendarBased = false,
            ApplicationUserId = appUserId
        };
        context.QRCodes.Add(directQrCode);

        // TimeTableEntry
        context.TimeTableEntries.Add(new TimeTableEntry
        {
            Id = TimeTableEntryId,
            StartTime = new TimeOnly(11, 0),
            EndTime = new TimeOnly(14, 0),
            MenuId = Menu1Id,
            QRCodeId = QrCodeId,
            ApplicationUserId = appUserId
        });

        // CalendarEntry
        context.CalendarEntries.Add(new CalendarEntry
        {
            Id = CalendarEntryId,
            Date = new DateOnly(2026, 1, 1),
            MenuId = Menu1Id,
            QRCodeId = QrCodeId,
            ApplicationUserId = appUserId
        });

        // MenuViews
        context.MenuViews.AddRange(
            new MenuView
            {
                Id = MenuView1Id,
                SessionId = TestSessionId,
                MenuId = Menu1Id,
                QRCodeId = QrCodeId,
                ViewedAt = DateTime.UtcNow.AddDays(-1),
                IpAddress = "127.0.0.1",
                UserAgent = "TestAgent/1.0"
            },
            new MenuView
            {
                Id = MenuView2Id,
                SessionId = TestSessionId,
                MenuId = Menu1Id,
                QRCodeId = QrCodeId,
                ViewedAt = DateTime.UtcNow.AddDays(-2),
                IpAddress = "127.0.0.1",
                UserAgent = "TestAgent/1.0"
            },
            new MenuView
            {
                Id = MenuView3Id,
                SessionId = "test-session-002",
                MenuId = Menu2Id,
                ViewedAt = DateTime.UtcNow.AddDays(-3),
                IpAddress = "192.168.1.1",
                UserAgent = "TestAgent/2.0"
            }
        );

        // MenuItemClicks
        context.MenuItemClicks.AddRange(
            new MenuItemClick
            {
                Id = MenuItemClick1Id,
                SessionId = TestSessionId,
                MenuItemId = MenuItem1Id,
                MenuId = Menu1Id,
                ClickedAt = DateTime.UtcNow.AddDays(-1),
                IpAddress = "127.0.0.1",
                UserAgent = "TestAgent/1.0"
            },
            new MenuItemClick
            {
                Id = MenuItemClick2Id,
                SessionId = "test-session-002",
                MenuItemId = MenuItem3Id,
                MenuId = Menu2Id,
                ClickedAt = DateTime.UtcNow.AddDays(-2),
                IpAddress = "192.168.1.1",
                UserAgent = "TestAgent/2.0"
            }
        );

        // Image
        context.Images.Add(new Image
        {
            Id = ImageId,
            Content = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 },
            MimeType = "image/jpeg"
        });

        await context.SaveChangesAsync();
    }
}
