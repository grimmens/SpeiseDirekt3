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

    // Allergen IDs (created per-menu, no longer globally seeded)
    public static readonly Guid AllergenGlutenId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    public static readonly Guid AllergenMilkId = Guid.Parse("a0000000-0000-0000-0000-000000000007");
    public static readonly Guid AllergenFishId = Guid.Parse("a0000000-0000-0000-0000-000000000004");
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

    // POS seed data
    public static readonly Guid TaxRateStandardId = Guid.Parse("77777777-7777-7777-7777-777777777701");
    public static readonly Guid TaxRateReducedId = Guid.Parse("77777777-7777-7777-7777-777777777702");
    public static readonly Guid DiscountId = Guid.Parse("88888888-8888-8888-8888-888888888801");
    public static readonly Guid ComboId = Guid.Parse("66666666-6666-6666-6666-666666666601");
    public static readonly Guid ComboItem1Id = Guid.Parse("66666666-6666-6666-6666-666666666602");

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

        // Create allergens per-menu (no longer globally seeded)
        var glutenAllergen = new Allergen
        {
            Id = AllergenGlutenId,
            Code = "A",
            Name = "Gluten",
            MenuId = Menu1Id,
            ApplicationUserId = appUserId
        };
        var milkAllergen = new Allergen
        {
            Id = AllergenMilkId,
            Code = "G",
            Name = "Milk",
            MenuId = Menu1Id,
            ApplicationUserId = appUserId
        };
        var fishAllergen = new Allergen
        {
            Id = AllergenFishId,
            Code = "D",
            Name = "Fish",
            MenuId = Menu2Id,
            ApplicationUserId = appUserId
        };
        context.Allergens.AddRange(glutenAllergen, milkAllergen, fishAllergen);

        // MenuItems
        var menuItem1 = new MenuItem
        {
            Id = MenuItem1Id,
            Name = "Caesar Salad",
            Description = "Fresh romaine lettuce with caesar dressing",
            Price = 8.50m,
            CategoryId = Category1Id,
            ApplicationUserId = appUserId
        };
        menuItem1.Allergens.Add(milkAllergen);
        menuItem1.Allergens.Add(glutenAllergen);

        var menuItem2 = new MenuItem
        {
            Id = MenuItem2Id,
            Name = "Tomato Soup",
            Description = "Homemade tomato soup",
            Price = 6.00m,
            CategoryId = Category1Id,
            ApplicationUserId = appUserId
        };

        var menuItem3 = new MenuItem
        {
            Id = MenuItem3Id,
            Name = "Grilled Salmon",
            Description = "Atlantic salmon with vegetables",
            Price = 22.00m,
            CategoryId = Category2Id,
            ApplicationUserId = appUserId
        };
        menuItem3.Allergens.Add(fishAllergen);

        context.MenuItems.AddRange(menuItem1, menuItem2, menuItem3);

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

        // POS: Tax Rates
        context.TaxRates.AddRange(
            new TaxRate
            {
                Id = TaxRateStandardId,
                Name = "Standard VAT",
                Rate = 0.2000m,
                IsDefault = true,
                ApplicationUserId = appUserId
            },
            new TaxRate
            {
                Id = TaxRateReducedId,
                Name = "Reduced VAT",
                Rate = 0.1000m,
                IsDefault = false,
                ApplicationUserId = appUserId
            }
        );

        // POS: Discount
        context.Discounts.Add(new Discount
        {
            Id = DiscountId,
            Code = "SAVE10",
            Description = "10% off your order",
            Type = DiscountType.Percentage,
            Value = 10m,
            MinOrderAmount = 5m,
            IsActive = true,
            MaxUses = 100,
            CurrentUses = 0,
            ApplicationUserId = appUserId
        });

        // POS: MenuCombo (Caesar Salad combo with Tomato Soup)
        var combo = new MenuCombo
        {
            Id = ComboId,
            Name = "Salad Combo",
            Description = "Caesar Salad + Tomato Soup",
            ComboPrice = 12.00m,
            TriggerMenuItemId = MenuItem1Id,
            MenuId = Menu1Id,
            ApplicationUserId = appUserId
        };
        combo.Items.Add(new MenuComboItem
        {
            Id = ComboItem1Id,
            MenuComboId = ComboId,
            MenuItemId = MenuItem2Id,
            ApplicationUserId = appUserId
        });
        context.MenuCombos.Add(combo);

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
