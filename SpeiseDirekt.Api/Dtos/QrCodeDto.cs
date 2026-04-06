namespace SpeiseDirekt.Api.Dtos;

public record QrCodeDto(string Title, Guid? MenuId, bool IsTimeTableBased, bool IsCalendarBased);
