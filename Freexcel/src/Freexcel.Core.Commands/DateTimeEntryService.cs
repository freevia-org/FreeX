using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static class DateTimeEntryService
{
    public static DateTimeValue CurrentDate(DateTime now) =>
        DateTimeValue.FromDateTime(now.Date);

    public static DateTimeValue CurrentTime(DateTime now) =>
        new(now.TimeOfDay.TotalDays);
}
