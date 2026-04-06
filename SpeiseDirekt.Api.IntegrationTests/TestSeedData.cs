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
