namespace FreeX.App.Host;

public static class BackstageGreetingFormatter
{
    private const int AfternoonStartHour = 12;
    private const int EveningStartHour = 17;

    public static string FormatGreeting(DateTime now) => FormatGreeting(now.Hour);

    public static string FormatGreeting(int hour) => hour switch
    {
        >= 0 and < AfternoonStartHour => "Good morning",
        >= AfternoonStartHour and < EveningStartHour => "Good afternoon",
        _ => "Good evening"
    };
}
