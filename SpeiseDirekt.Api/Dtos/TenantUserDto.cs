using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Api.Dtos;

/// <summary>
/// A new team member. Every member is a manager (AP14): a <c>role</c> sent by an older client is ignored, like every
/// unknown JSON property.
/// </summary>
public record CreateTenantUserDto(string Email, string DisplayName, string? Password);

/// <summary>
/// Changes a team member: active or not, and the display name when one is sent (null = unchanged). The former
/// <c>role</c> and <c>customPermissions</c> are ignored if a client still sends them.
/// </summary>
public record UpdateTenantUserDto(bool IsActive, [StringLength(100)] string? DisplayName = null);
