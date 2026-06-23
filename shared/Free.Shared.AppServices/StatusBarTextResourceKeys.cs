namespace Free.Shared.AppServices;

public static class StatusBarTextResourceKeys
{
    public const string ReadyText = "MainWindow_Text_Ready";

    public const string AverageFormat = "StatusBar_AverageFormat";
    public const string CountFormat = "StatusBar_CountFormat";
    public const string NumericalCountFormat = "StatusBar_NumericalCountFormat";
    public const string SumFormat = "StatusBar_SumFormat";
    public const string MinimumFormat = "StatusBar_MinFormat";
    public const string MaximumFormat = "StatusBar_MaxFormat";

    public const string Average = "StatusBar_Average";
    public const string Count = "StatusBar_Count";
    public const string NumericalCount = "StatusBar_NumericalCount";
    public const string Sum = "StatusBar_Sum";
    public const string Minimum = "StatusBar_Minimum";
    public const string Maximum = "StatusBar_Maximum";
    public const string EditMode = "StatusBar_EditMode";
    public const string EnterMode = "StatusBar_EnterMode";
    public const string PointMode = "StatusBar_PointMode";
    public const string ExtendSelectionMode = "StatusBar_ExtendSelectionMode";
    public const string AddToSelectionMode = "StatusBar_AddToSelectionMode";
    public const string EndMode = "StatusBar_EndMode";

    public static IReadOnlyList<string> RequiredKeys { get; } =
    [
        ReadyText,
        AverageFormat,
        CountFormat,
        NumericalCountFormat,
        SumFormat,
        MinimumFormat,
        MaximumFormat,
        Average,
        Count,
        NumericalCount,
        Sum,
        Minimum,
        Maximum,
        EditMode,
        EnterMode,
        PointMode,
        ExtendSelectionMode,
        AddToSelectionMode,
        EndMode
    ];

    public static string ReadoutFormat(StatusBarReadoutKind kind) =>
        kind switch
        {
            StatusBarReadoutKind.Average => AverageFormat,
            StatusBarReadoutKind.Count => CountFormat,
            StatusBarReadoutKind.NumericalCount => NumericalCountFormat,
            StatusBarReadoutKind.Sum => SumFormat,
            StatusBarReadoutKind.Minimum => MinimumFormat,
            StatusBarReadoutKind.Maximum => MaximumFormat,
            _ => CountFormat
        };

    public static string ReadoutLabel(StatusBarReadoutKind kind) =>
        kind switch
        {
            StatusBarReadoutKind.Average => Average,
            StatusBarReadoutKind.Count => Count,
            StatusBarReadoutKind.NumericalCount => NumericalCount,
            StatusBarReadoutKind.Sum => Sum,
            StatusBarReadoutKind.Minimum => Minimum,
            StatusBarReadoutKind.Maximum => Maximum,
            _ => Count
        };
}
