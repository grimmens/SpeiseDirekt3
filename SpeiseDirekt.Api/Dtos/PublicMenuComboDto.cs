namespace SpeiseDirekt.Api.Dtos;

/// <summary>A combo as shown to guests (public API).</summary>
public record PublicMenuComboDto(
    Guid Id,
    string Name,
    string Description,
    decimal ComboPrice,
    PublicComboMenuItemDto TriggerMenuItem,
    List<PublicComboMenuItemDto> Items);

public record PublicComboMenuItemDto(Guid Id, string Name, string Description, decimal Price);
