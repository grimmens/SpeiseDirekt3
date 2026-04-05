namespace SpeiseDirekt.Api.Dtos;

public record CalendarEntryDto(DateOnly Date, DateOnly? EndDate, DayOfWeek? RecurringDayOfWeek, Guid MenuId);
