using Free.Shared.Drawing;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.Validation.Avalonia;

internal enum PhysicalFixtureKind
{
    AnimationPane,
    SmartArtTextPane,
    InternalSlideHyperlink,
}

internal sealed record PhysicalFixtureOptions(
    PhysicalFixtureKind Kind,
    string? OutputDirectory = null)
{
    public const string AnimationPaneArgument = "--physical-animation-pane-fixture";
    public const string SmartArtTextPaneArgument = "--physical-smartart-text-pane-fixture";
    public const string InternalSlideHyperlinkArgument = "--physical-internal-slide-hyperlink-fixture";

    public static bool TryParse(
        IReadOnlyList<string> args,
        out PhysicalFixtureOptions? options,
        out string[] startupArguments,
        out string? error)
    {
        var filtered = new List<string>(args.Count);
        options = null;
        error = null;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, AnimationPaneArgument, StringComparison.Ordinal))
            {
                if (!TrySelect(PhysicalFixtureKind.AnimationPane, null, ref options, out error))
                    return Fail(filtered, out startupArguments);
                continue;
            }

            if (string.Equals(argument, SmartArtTextPaneArgument, StringComparison.Ordinal))
            {
                if (!TrySelect(PhysicalFixtureKind.SmartArtTextPane, null, ref options, out error))
                    return Fail(filtered, out startupArguments);
                continue;
            }

            if (argument.StartsWith(InternalSlideHyperlinkArgument + "=", StringComparison.Ordinal))
            {
                var outputDirectory = argument[(InternalSlideHyperlinkArgument.Length + 1)..];
                if (string.IsNullOrWhiteSpace(outputDirectory))
                {
                    error = $"{InternalSlideHyperlinkArgument} requires a non-empty output directory.";
                    return Fail(filtered, out startupArguments);
                }

                if (!TrySelect(
                        PhysicalFixtureKind.InternalSlideHyperlink,
                        outputDirectory,
                        ref options,
                        out error))
                {
                    return Fail(filtered, out startupArguments);
                }
                continue;
            }

            if (string.Equals(argument, InternalSlideHyperlinkArgument, StringComparison.Ordinal))
            {
                if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    error = $"{InternalSlideHyperlinkArgument} requires a non-empty output directory.";
                    return Fail(filtered, out startupArguments);
                }

                if (!TrySelect(
                        PhysicalFixtureKind.InternalSlideHyperlink,
                        args[++index],
                        ref options,
                        out error))
                {
                    return Fail(filtered, out startupArguments);
                }
                continue;
            }

            filtered.Add(argument);
        }

        startupArguments = filtered.ToArray();
        return true;
    }

    private static bool TrySelect(
        PhysicalFixtureKind kind,
        string? outputDirectory,
        ref PhysicalFixtureOptions? options,
        out string? error)
    {
        if (options is not null)
        {
            error = "Exactly one physical fixture selector may be supplied.";
            return false;
        }

        options = new PhysicalFixtureOptions(kind, outputDirectory);
        error = null;
        return true;
    }

    private static bool Fail(List<string> filtered, out string[] startupArguments)
    {
        startupArguments = filtered.ToArray();
        return false;
    }
}

internal static class PhysicalFixtureCoordinator
{
    public static void Start(MainWindow.ValidationAccessAdapter access, PhysicalFixtureOptions options)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(options);

        switch (options.Kind)
        {
            case PhysicalFixtureKind.AnimationPane:
                access.SetAnimationPaneRequestCoordinator(() => SeedAnimationPane(access));
                break;
            case PhysicalFixtureKind.SmartArtTextPane:
                SeedSmartArtTextPane(access);
                break;
            case PhysicalFixtureKind.InternalSlideHyperlink:
                StartInternalSlideHyperlink(access, options.OutputDirectory!);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private static void SeedAnimationPane(MainWindow.ValidationAccessAdapter access)
    {
        var slide = access.Editor.CurrentSlide;
        if (slide is null || slide.Animations.Count > 0)
            return;

        var shape = access.Editor.InsertTextBox("Animation Pane sample");
        slide.Animations.Add(new ShapeAnimation
        {
            ShapeId = shape.Id,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.OnClick,
            DurationMs = 500,
        });
        access.RefreshCanvas();
    }

    private static void SeedSmartArtTextPane(MainWindow.ValidationAccessAdapter access)
    {
        var smartArt = access.Editor.CurrentSlide?.Shapes
            .FirstOrDefault(shape => shape.SmartArt is not null);
        if (smartArt is null)
            return;

        access.Editor.Select(smartArt.Id);
        access.ShowSmartArtTextPane();
        access.RefreshCanvas();
    }

    private static void StartInternalSlideHyperlink(
        MainWindow.ValidationAccessAdapter access,
        string outputDirectory)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        SeedInternalSlideHyperlink(access, outputDirectory);
        access.SetHyperlinkAppliedObserver(() =>
            RecordHyperlinkAuthoring(access, outputDirectory));
        access.SetSlideShowInternalHyperlinkObserver((hyperlink, currentSlideIndex) =>
            RecordHyperlinkActivation(hyperlink, currentSlideIndex, outputDirectory));
    }

    private static void SeedInternalSlideHyperlink(
        MainWindow.ValidationAccessAdapter access,
        string outputDirectory)
    {
        var presentation = access.Presentation;
        if (presentation.Slides.Count != 1)
            return;

        var firstSlide = access.Editor.CurrentSlide ?? presentation.Slides[0];
        var shapeWidth = DrawingMlCoordinateUnits.EmuPerInch * 4;
        var shapeHeight = DrawingMlCoordinateUnits.EmuPerInch * 2;
        var linkShape = new SlideShape
        {
            Id = 9001,
            Name = "Physical internal-slide hyperlink target",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = (presentation.SlideSizeCxEmu - shapeWidth) / 2,
            OffsetYEmu = (presentation.SlideSizeCyEmu - shapeHeight) / 2,
            ExtentCxEmu = shapeWidth,
            ExtentCyEmu = shapeHeight,
            Fill = new ShapeFill.Solid(new SrgbColor(0x44, 0x72, 0xC3)),
            TextBody = new TextBody
            {
                Wrap = true,
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "CLICK LINK TO SLIDE 2" } },
                    },
                },
            },
        };
        access.Editor.AddShape(linkShape);
        if (linkShape.ExtentCxEmu <= 0 || linkShape.ExtentCyEmu <= 0 ||
            !firstSlide.Shapes.Any(shape => shape.Id == linkShape.Id))
        {
            throw new InvalidOperationException(
                "Physical hyperlink fixture did not create a visible slide-1 rectangle.");
        }

        access.Editor.InsertSlide();
        access.Editor.InsertTextBox("TARGET SLIDE 2");
        access.Editor.SelectSlide(0);
        access.Editor.Select(linkShape.Id);
        access.RefreshCanvas();
        WritePostcondition(
            outputDirectory,
            "fixture-postcondition.txt",
            $"slide1Id={firstSlide.Id}\n" +
            $"slide2Id={presentation.Slides[1].Id}\n" +
            $"currentSlideIndex={access.Editor.CurrentSlideIndex}\n" +
            $"shapeOffsetXEmu={linkShape.OffsetXEmu}\n" +
            $"shapeOffsetYEmu={linkShape.OffsetYEmu}\n" +
            $"shapeExtentCxEmu={linkShape.ExtentCxEmu}\n" +
            $"shapeExtentCyEmu={linkShape.ExtentCyEmu}\n" +
            $"slideSizeCxEmu={presentation.SlideSizeCxEmu}\n" +
            $"slideSizeCyEmu={presentation.SlideSizeCyEmu}\n");
    }

    private static void RecordHyperlinkAuthoring(
        MainWindow.ValidationAccessAdapter access,
        string outputDirectory)
    {
        var selectedShapeId = access.Editor.SelectedShapeIds.SingleOrDefault();
        var targetSlideId = access.Editor.CurrentSlide?.Shapes
            .FirstOrDefault(shape => shape.Id == selectedShapeId)?
            .Hyperlink?
            .TargetSlideId;
        WritePostcondition(
            outputDirectory,
            "authoring-postcondition.txt",
            $"selectedShapeId={selectedShapeId}\ntargetSlideId={targetSlideId}\n");
    }

    private static void RecordHyperlinkActivation(
        Hyperlink hyperlink,
        int currentSlideIndex,
        string outputDirectory) =>
        WritePostcondition(
            outputDirectory,
            "activation-postcondition.txt",
            $"activation=internal-slide-hyperlink\n" +
            $"targetSlideId={hyperlink.TargetSlideId}\n" +
            $"currentSlideIndex={currentSlideIndex}\n");

    private static void WritePostcondition(
        string outputDirectory,
        string fileName,
        string content) =>
        File.WriteAllText(Path.Combine(outputDirectory, fileName), content);
}
