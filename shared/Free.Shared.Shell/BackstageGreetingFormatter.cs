namespace Free.Shared.Shell;

public static class BackstageGreetingFormatter
{
    private const int AfternoonStartHour = 12;
    private const int EveningStartHour = 17;

    public static string FormatGreeting(DateTime now) => FormatGreeting(now.Hour);

    public static string FormatGreeting(int hour) => hour switch
    {
        >= 0 and < AfternoonStartHour => BackstageStrings.Current.Get("Backstage_GreetingMorning"),
        >= AfternoonStartHour and < EveningStartHour => BackstageStrings.Current.Get("Backstage_GreetingAfternoon"),
        _ => BackstageStrings.Current.Get("Backstage_GreetingEvening")
    };
}
