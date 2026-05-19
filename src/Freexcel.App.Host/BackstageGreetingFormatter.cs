namespace Freexcel.App.Host;

public static class BackstageGreetingFormatter
{
    public static string FormatGreeting(DateTime now) => FormatGreeting(now.Hour);

    public static string FormatGreeting(int hour) => hour switch
    {
        >= 0 and < 12 => "Good morning",
        >= 12 and < 17 => "Good afternoon",
        _ => "Good evening"
    };
}
