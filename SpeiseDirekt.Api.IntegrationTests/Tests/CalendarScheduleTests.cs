using FluentAssertions;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

/// <summary>
/// <see cref="CalendarSchedule"/> (pure, no database): which calendar entry applies on a day — shared by the guest
/// resolution (<c>MenuService</c>, covered end to end in <see cref="MenuDisplayControllerTests"/>) and the back-office
/// calendar editor — and which entries collide (<see cref="QRCode.Validate"/> and the editor's live conflict marks).
/// </summary>
public class CalendarScheduleTests
{
    private static readonly Guid MenuA = Guid.NewGuid();
    private static readonly Guid MenuB = Guid.NewGuid();
    private static readonly Guid MenuC = Guid.NewGuid();

    private static CalendarEntry Entry(DateOnly date, Guid menuId, DateOnly? until = null, DayOfWeek? weekly = null) =>
        new() { Id = Guid.NewGuid(), Date = date, EndDate = until, RecurringDayOfWeek = weekly, MenuId = menuId };

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    // --- IsActiveOn / ActiveEntry ---

    [Fact]
    public void SingleDate_AppliesOnlyOnThatDay()
    {
        var single = Entry(D(2026, 12, 24), MenuA);

        CalendarSchedule.ActiveEntry([single], D(2026, 12, 24)).Should().BeSameAs(single);
        CalendarSchedule.ActiveEntry([single], D(2026, 12, 23)).Should().BeNull();
        CalendarSchedule.ActiveEntry([single], D(2026, 12, 25)).Should().BeNull();
    }

    [Fact]
    public void SingleDate_WithEndDateEqualToDate_IsOneDay()
    {
        var single = Entry(D(2026, 12, 24), MenuA, until: D(2026, 12, 24));

        CalendarSchedule.IsActiveOn(single, D(2026, 12, 24)).Should().BeTrue();
        CalendarSchedule.IsActiveOn(single, D(2026, 12, 25)).Should().BeFalse();
    }

    [Theory]
    [InlineData(23, false)]
    [InlineData(24, true)]
    [InlineData(25, true)]
    [InlineData(26, true)]
    [InlineData(27, false)]
    public void Range_AppliesOnEveryDayFromDateToEndDateInclusive(int day, bool active)
    {
        var range = Entry(D(2026, 12, 24), MenuA, until: D(2026, 12, 26));

        CalendarSchedule.IsActiveOn(range, D(2026, 12, day)).Should().Be(active);
    }

    [Theory]
    [InlineData(2026, 11, 6, true)]   // first Friday (the start date itself)
    [InlineData(2026, 11, 13, true)]
    [InlineData(2026, 11, 12, false)] // a Thursday
    [InlineData(2026, 10, 30, false)] // a Friday before the start
    [InlineData(2027, 1, 29, true)]   // the until date
    [InlineData(2027, 2, 5, false)]   // the Friday after the until date
    public void Weekly_WithUntil_AppliesOnItsWeekdayWithinThePeriod(int year, int month, int day, bool active)
    {
        var weekly = Entry(D(2026, 11, 6), MenuA, until: D(2027, 1, 29), weekly: DayOfWeek.Friday);

        CalendarSchedule.IsActiveOn(weekly, D(year, month, day)).Should().Be(active);
    }

    [Fact]
    public void Weekly_StartingOnAnotherWeekday_BeginsWithTheNextMatchingDay()
    {
        // Start on a Wednesday, repeat on Fridays: the first occurrence is the Friday after the start
        var weekly = Entry(D(2026, 11, 4), MenuA, weekly: DayOfWeek.Friday);

        CalendarSchedule.IsActiveOn(weekly, D(2026, 11, 4)).Should().BeFalse();
        CalendarSchedule.IsActiveOn(weekly, D(2026, 11, 6)).Should().BeTrue();
    }

    [Fact]
    public void Weekly_WithoutUntil_RepeatsForever()
    {
        var weekly = Entry(D(2026, 11, 2), MenuA, weekly: DayOfWeek.Monday);

        CalendarSchedule.IsActiveOn(weekly, D(2040, 1, 2)).Should().BeTrue(); // a Monday
        CalendarSchedule.IsActiveOn(weekly, D(2040, 1, 3)).Should().BeFalse();
    }

    [Fact]
    public void OneOffEntry_BeatsWeeklyEntry_OnTheSameDay_InAnyOrder()
    {
        var weekly = Entry(D(2026, 1, 2), MenuA, weekly: DayOfWeek.Friday);
        var christmas = Entry(D(2026, 12, 24), MenuB, until: D(2026, 12, 26)); // Thu–Sat, covers Friday the 25th
        var single = Entry(D(2027, 1, 1), MenuC);                              // a Friday

        CalendarSchedule.ActiveEntry([weekly, christmas, single], D(2026, 12, 25)).Should().BeSameAs(christmas);
        CalendarSchedule.ActiveEntry([christmas, weekly, single], D(2026, 12, 25)).Should().BeSameAs(christmas);
        CalendarSchedule.ActiveEntry([weekly, christmas, single], D(2027, 1, 1)).Should().BeSameAs(single);
        CalendarSchedule.ActiveEntry([weekly, christmas, single], D(2027, 1, 8)).Should().BeSameAs(weekly);
    }

    [Fact]
    public void SeveralEntriesOfTheSameKind_TheEarliestDateWins()
    {
        // Refused by validation, but guests must still get a defined menu (e.g. data created before the rule)
        var later = Entry(D(2026, 12, 20), MenuB, until: D(2026, 12, 31));
        var earlier = Entry(D(2026, 12, 1), MenuA, until: D(2026, 12, 24));
        CalendarSchedule.ActiveEntry([later, earlier], D(2026, 12, 22)).Should().BeSameAs(earlier);

        var weeklyLater = Entry(D(2026, 11, 13), MenuB, weekly: DayOfWeek.Friday);
        var weeklyEarlier = Entry(D(2026, 11, 6), MenuA, weekly: DayOfWeek.Friday);
        CalendarSchedule.ActiveEntry([weeklyLater, weeklyEarlier], D(2026, 11, 20)).Should().BeSameAs(weeklyEarlier);
    }

    [Fact]
    public void SameKindAndSameDate_KeepsTheInputOrder()
    {
        var first = Entry(D(2026, 12, 24), MenuA);
        var second = Entry(D(2026, 12, 24), MenuB);

        CalendarSchedule.ActiveEntry([first, second], D(2026, 12, 24)).Should().BeSameAs(first);
        CalendarSchedule.ActiveEntry([second, first], D(2026, 12, 24)).Should().BeSameAs(second);
    }

    [Fact]
    public void NoEntries_OrNull_GivesNull()
    {
        CalendarSchedule.ActiveEntry([], D(2026, 12, 24)).Should().BeNull();
        CalendarSchedule.ActiveEntry(null, D(2026, 12, 24)).Should().BeNull();
    }

    // --- Collide / Conflicts ---

    [Fact]
    public void Ranges_SharingADay_Collide_AdjacentOnesDoNot()
    {
        var a = Entry(D(2026, 11, 9), MenuA, until: D(2026, 11, 12));
        var sharing = Entry(D(2026, 11, 12), MenuB);
        var adjacent = Entry(D(2026, 11, 13), MenuB, until: D(2026, 11, 15));
        var inside = Entry(D(2026, 11, 10), MenuC);

        CalendarSchedule.Collide(a, sharing).Should().BeTrue();
        CalendarSchedule.Collide(sharing, a).Should().BeTrue();
        CalendarSchedule.Collide(a, inside).Should().BeTrue();
        CalendarSchedule.Collide(a, adjacent).Should().BeFalse();
    }

    [Fact]
    public void WeeklyEntries_CollideOnlyOnTheSameWeekdayWithOverlappingPeriods()
    {
        var mondays = Entry(D(2026, 11, 9), MenuA, weekly: DayOfWeek.Monday);                       // forever
        var laterMondays = Entry(D(2026, 12, 7), MenuB, weekly: DayOfWeek.Monday);
        var tuesdays = Entry(D(2026, 12, 7), MenuB, weekly: DayOfWeek.Tuesday);
        var endedMondays = Entry(D(2026, 1, 5), MenuC, until: D(2026, 11, 2), weekly: DayOfWeek.Monday);

        CalendarSchedule.Collide(mondays, laterMondays).Should().BeTrue("without an end date a weekly entry repeats forever");
        CalendarSchedule.Collide(mondays, tuesdays).Should().BeFalse();
        CalendarSchedule.Collide(mondays, endedMondays).Should().BeFalse("the earlier one ends before the later one starts");
    }

    [Fact]
    public void OneOffAndWeekly_NeverCollide()
    {
        var fridays = Entry(D(2026, 11, 6), MenuA, until: D(2027, 1, 29), weekly: DayOfWeek.Friday);
        var christmas = Entry(D(2026, 12, 24), MenuB, until: D(2026, 12, 26));

        CalendarSchedule.Collide(fridays, christmas).Should().BeFalse("the period wins on the Friday inside it");
    }

    [Fact]
    public void EntryEndingBeforeItsStart_CollidesWithNothing()
    {
        var broken = Entry(D(2026, 11, 12), MenuA, until: D(2026, 11, 10));
        var other = Entry(D(2026, 11, 9), MenuB, until: D(2026, 11, 15));

        CalendarSchedule.HasEndBeforeStart(broken).Should().BeTrue();
        CalendarSchedule.Collide(broken, other).Should().BeFalse();
    }

    [Fact]
    public void Conflicts_ListsEveryCollidingPair()
    {
        var long1 = Entry(D(2026, 12, 1), MenuA, until: D(2026, 12, 10));
        var inner1 = Entry(D(2026, 12, 2), MenuB, until: D(2026, 12, 3));
        var inner2 = Entry(D(2026, 12, 5), MenuC, until: D(2026, 12, 6));
        var separate = Entry(D(2026, 12, 20), MenuC);

        CalendarSchedule.Conflicts([long1, inner1, inner2, separate]).Should().BeEquivalentTo(
            new[] { (long1, inner1), (long1, inner2) }, o => o.WithStrictOrdering());
        CalendarSchedule.Conflicts(null).Should().BeEmpty();
    }

    [Fact]
    public void Validate_RefusesExactlyWhatCollideReports()
    {
        static IEnumerable<string> Errors(params CalendarEntry[] entries)
        {
            var qr = new QRCode { Title = "T", IsCalendarBased = true, MenuId = MenuA, CalendarEntries = entries.ToList() };
            return qr.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(qr)).Select(r => r.ErrorMessage!);
        }

        Errors(Entry(D(2026, 11, 9), MenuA, until: D(2026, 11, 12)), Entry(D(2026, 11, 12), MenuB))
            .Should().ContainSingle().Which.Should().Be("Die Datumsperioden überschneiden sich: 09.11.2026–12.11.2026 und 12.11.2026–12.11.2026.");
        Errors(Entry(D(2026, 11, 9), MenuA, weekly: DayOfWeek.Monday), Entry(D(2026, 12, 7), MenuB, weekly: DayOfWeek.Monday))
            .Should().ContainSingle().Which.Should().StartWith("Die wöchentlichen Einträge am Montag überschneiden sich");
        Errors(Entry(D(2026, 11, 6), MenuA, until: D(2027, 1, 29), weekly: DayOfWeek.Friday), Entry(D(2026, 12, 24), MenuB, until: D(2026, 12, 26)))
            .Should().BeEmpty();
        Errors(Entry(D(2026, 11, 9), MenuA, until: D(2026, 11, 12)), Entry(D(2026, 11, 13), MenuB))
            .Should().BeEmpty();
    }
}
