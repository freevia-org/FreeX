using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

public sealed record FunctionArgumentSpec(string Name, string Description, bool Optional = false);

public sealed partial class FunctionArgumentsDialog : Window
{
    private readonly string _functionName;
    private readonly IReadOnlyList<FunctionArgumentSpec> _arguments;
    private readonly List<TextBox> _argumentBoxes = [];
    private readonly TextBlock _formulaPreview = new();

    public string? ResultFormula { get; private set; }

    public FunctionArgumentsDialog(InsertFunctionCatalogEntry function)
    {
        _functionName = function.Name.Trim().ToUpperInvariant();
        _arguments = GetArgumentSpecs(_functionName);

        Title = "Function Arguments";
        Width = 520;
        Height = Math.Max(300, Math.Min(620, 220 + (_arguments.Count * 58)));
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(12) };
        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 76, rowMargin: new Thickness(0, 12, 0, 0));
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var body = new StackPanel();
        root.Children.Add(body);
        body.Children.Add(new TextBlock
        {
            Text = _functionName,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        body.Children.Add(new TextBlock
        {
            Text = function.Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 0, 0, 12)
        });

        foreach (var argument in _arguments)
            AddArgumentRow(body, argument);

        body.Children.Add(new TextBlock { Text = "Formula result:", Margin = new Thickness(0, 12, 0, 2) });
        _formulaPreview.FontWeight = FontWeights.SemiBold;
        _formulaPreview.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(_formulaPreview);
        UpdatePreview();

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static IReadOnlyList<FunctionArgumentSpec> GetArgumentSpecs(string functionName)
    {
        var normalized = functionName.Trim().ToUpperInvariant();
        if (KnownArguments.TryGetValue(normalized, out var arguments))
            return arguments;

        return [new FunctionArgumentSpec("Number1", "The first value, reference, or expression.")];
    }

    public static string CreateFormula(string functionName, IEnumerable<string?> arguments)
    {
        var normalized = functionName.Trim().ToUpperInvariant();
        var cleaned = arguments.Select(argument => argument?.Trim() ?? "").ToList();
        while (cleaned.Count > 0 && cleaned[^1].Length == 0)
            cleaned.RemoveAt(cleaned.Count - 1);

        return $"{normalized}({string.Join(", ", cleaned)})";
    }

    private void AddArgumentRow(Panel body, FunctionArgumentSpec argument)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var box = new TextBox { Margin = new Thickness(8, 0, 0, 2) };
        box.TextChanged += (_, _) => UpdatePreview();
        _argumentBoxes.Add(box);

        var label = new Label
        {
            Content = argument.Optional ? $"{argument.Name}:" : $"{argument.Name}:",
            Target = box,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(label);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);

        var help = new TextBlock
        {
            Text = argument.Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush,
            FontSize = 11,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetRow(help, 1);
        Grid.SetColumn(help, 1);
        grid.Children.Add(help);

        body.Children.Add(grid);
    }

    private void UpdatePreview()
    {
        _formulaPreview.Text = CreateFormula(_functionName, _argumentBoxes.Select(box => box.Text));
    }

    private void Accept()
    {
        ResultFormula = CreateFormula(_functionName, _argumentBoxes.Select(box => box.Text));
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        var firstArgument = _argumentBoxes.FirstOrDefault();
        if (firstArgument is null)
            return;

        firstArgument.Focus();
        firstArgument.SelectAll();
        Keyboard.Focus(firstArgument);
    }

}
