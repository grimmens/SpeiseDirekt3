using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.Dtos;

public record CreateTenantUserDto(string Email, string DisplayName, TenantRole Role, string? Password);
public record UpdateTenantUserDto(TenantRole Role, Permission? CustomPermissions, bool IsActive);
