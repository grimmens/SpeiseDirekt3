using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Api.Dtos;

/// <summary>
/// Request model for creating/updating a menu combo. On update, <see cref="MenuId"/> is ignored:
/// a combo stays in its menu.
/// </summary>
public record MenuComboDto(
    [Required, StringLength(100)] string Name,
    [StringLength(500)] string? Description,
    decimal ComboPrice,
    Guid? TriggerMenuItemId,
    List<Guid>? ItemIds,
    Guid MenuId);
