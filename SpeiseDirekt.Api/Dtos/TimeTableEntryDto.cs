namespace SpeiseDirekt.Api.Dtos;

public record TimeTableEntryDto(TimeOnly? StartTime, TimeOnly? EndTime, Guid MenuId);
