using System.Text.RegularExpressions;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The owner invites an Employee (<c>/users/invite</c>); the Employee logs in with the temporary password,
/// sees the owner's data read-only and is turned away from edit pages.
/// </summary>
public class PermissionTests : BackOfficeTestBase
{
    public PermissionTests(E2EFixture fixture) : base(fixture) { }

    private async Task InviteAsync(string email, string displayName, string role)
    {
        await GotoInteractiveAsync("/users/invite");
        await Page.GetByPlaceholder("employee@example.com").FillAsync(email);
        await Page.GetByPlaceholder("John Doe").FillAsync(displayName);
        await Page.GetByPlaceholder("Min. 8 characters").FillAsync(Password);
        await Page.Locator("select").SelectOptionAsync(new SelectOptionValue { Label = role });
        await Button("Create Account").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/users$"));
    }

    /// <summary>Owner with one menu (category + item); returns the menu id and the employee's page.</summary>
    private async Task<(Guid MenuId, IPage Employee)> SetUpOwnerAndEmployeeAsync(string menuName)
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync(menuName);
        await CreateCategoryViaUiAsync(menuId, "Vorspeisen");
        await CreateMenuItemViaUiAsync(menuId, "Bruschetta", "Tomate, Basilikum", 5.50m);

        var employeeEmail = UniqueEmail("employee");
        await InviteAsync(employeeEmail, "Erika Mitarbeiterin", "Employee");
        await Assertions.Expect(Page.GetByText("Erika Mitarbeiterin").First).ToBeVisibleAsync();

        var (employeeContext, employee) = await NewContextAsync();
        _employeeContext = employeeContext;
        await LoginAsync(employee, employeeEmail);
        return (menuId, employee);
    }

    private IBrowserContext? _employeeContext;

    public new async Task DisposeAsync()
    {
        if (_employeeContext is not null)
            await _employeeContext.DisposeAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task Employee_SeesOwnersMenus_WithoutCreateEditDeleteButtons()
    {
        var (menuId, employee) = await SetUpOwnerAndEmployeeAsync("Team Menü");

        await GotoInteractiveAsync(employee, "/menus");
        await Assertions.Expect(MenuCardLink(employee, "Team Menü")).ToBeVisibleAsync();
        await Assertions.Expect(employee.GetByRole(AriaRole.Button, new() { Name = "Create New Menu" })).ToHaveCountAsync(0);
        await Assertions.Expect(employee.GetByRole(AriaRole.Button, new() { Name = "Edit" })).ToHaveCountAsync(0);

        await GotoInteractiveAsync(employee, $"/menudetail/{menuId}");
        await Assertions.Expect(employee.GetByRole(AriaRole.Heading, new() { Name = "Team Menü" })).ToBeVisibleAsync();
        await Assertions.Expect(employee.GetByRole(AriaRole.Button, new() { Name = "Edit" })).ToHaveCountAsync(0);

        await GotoInteractiveAsync(employee, $"/MenuItems/{menuId}");
        await Assertions.Expect(employee.Locator("td", new() { HasTextString = "Bruschetta" }).First).ToBeVisibleAsync();
        await Assertions.Expect(employee.GetByRole(AriaRole.Button, new() { Name = "New Menu Entry" })).ToHaveCountAsync(0);
        await Assertions.Expect(employee.GetByRole(AriaRole.Button, new() { Name = "Edit" })).ToHaveCountAsync(0);
        await Assertions.Expect(employee.GetByRole(AriaRole.Button, new() { Name = "Delete" })).ToHaveCountAsync(0);

        await GotoInteractiveAsync(employee, $"/Categories/{menuId}");
        await Assertions.Expect(employee.Locator("table a", new() { HasTextString = "Vorspeisen" })).ToBeVisibleAsync();
        await Assertions.Expect(employee.GetByRole(AriaRole.Button, new() { Name = "Add Category" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Employee_DirectEditUrls_AreDenied()
    {
        var (menuId, employee) = await SetUpOwnerAndEmployeeAsync("Gesperrtes Menü");

        // Menu edit form redirects to the list without the MenusEdit permission
        await GotoInteractiveAsync(employee, $"/menu/edit/{menuId}");
        await Assertions.Expect(employee).ToHaveURLAsync(new Regex("/menus$"));
        await Assertions.Expect(employee.Locator("#menuName")).ToHaveCountAsync(0);

        // Creating a menu is denied as well
        await GotoInteractiveAsync(employee, "/menu/edit");
        await Assertions.Expect(employee).ToHaveURLAsync(new Regex("/menus$"));

        // Inviting users requires UsersCreate
        await GotoInteractiveAsync(employee, "/users/invite");
        await Assertions.Expect(employee).ToHaveURLAsync(new Regex("/users$"));
        await Assertions.Expect(employee.GetByPlaceholder("employee@example.com")).ToHaveCountAsync(0);

        // Creating a QR code requires QrCodesCreate
        await GotoInteractiveAsync(employee, "/qrcode/edit");
        await Assertions.Expect(employee).ToHaveURLAsync(new Regex("/qrcodes$"));
        await Assertions.Expect(employee.Locator("#qrTitle")).ToHaveCountAsync(0);
    }
}
