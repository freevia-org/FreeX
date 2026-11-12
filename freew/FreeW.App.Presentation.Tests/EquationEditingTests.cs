using Free.Shared.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class EquationEditingTests
{
    // ── Editor session ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NewSession_CommitsWhatIsTyped_AsAnInsert()
    {
        var session = EquationEditorSession.ForNew();

        session.Commit().Should().BeNull("an empty equation is never written to the document");
        session.HasChanges.Should().BeFalse();

        session.SetText("a/b");
        var commit = session.Commit();

        commit.Should().NotBeNull();
        commit!.Original.Should().BeNull();
        commit.Equation.Runs.Should().ContainSingle().Which.Kind.Should().Be(MathRunKind.Fraction);
        commit.Equation.IsDisplayMath.Should().BeFalse();
    }

    [Fact]
    public void ExistingSession_OpensWithTheLinearSource_AndCommitsOnlyRealChanges()
    {
        var equation = new Equation([MathRun.PlainText("E = m"), MathRun.Superscript("c", "2")]);
        var session = EquationEditorSession.ForExisting(equation);

        // Literal spaces in the model are written as "\ " (a typed space only triggers build-up).
        session.Text.Should().Be(@"E\ =\ mc^2");
        session.InitialText.Should().Be(@"E\ =\ mc^2");
        session.HasOpaqueParts.Should().BeFalse();
        session.Commit().Should().BeNull("an untouched equation must not add an undo step");

        session.SetText(@"E\ =\ mc^3");
        var commit = session.Commit();
        commit!.Original.Should().BeSameAs(equation);
        commit.Equation.Runs[1].Sup.Should().Be("3");

        session.SetText(string.Empty);
        session.Commit().Should().BeNull("emptying the text does not delete the equation");
    }

    [Fact]
    public void LayoutChange_IsACommittableChange()
    {
        var equation = EquationLinearFormat.Parse("a/b");
        var session = EquationEditorSession.ForExisting(equation);

        session.IsDisplay = true;
        session.Justification = MathJustification.Right;
        var commit = session.Commit();

        commit.Should().NotBeNull();
        commit!.Equation.IsDisplayMath.Should().BeTrue();
        commit.Equation.Justification.Should().Be(MathJustification.Right);

        // Justification means nothing for an inline equation and is not carried.
        session.IsDisplay = false;
        session.Commit().Should().BeNull();
        session.BuildEquation().Justification.Should().Be(MathJustification.CenterGroup);
    }

    [Fact]
    public void OpaqueParts_SurviveAnEditThroughTheSession()
    {
        var preserved = MathRun.PreservedOmml("<m:box xmlns:m=\"m\"/>", "boxed");
        var equation = new Equation([MathRun.PlainText("y="), preserved]);
        var session = EquationEditorSession.ForExisting(equation);

        session.HasOpaqueParts.Should().BeTrue();
        session.SetText(session.Text + "+x^2");
        var committed = session.Commit()!.Equation;

        committed.Runs.Should().HaveCount(4);
        committed.Runs[1].Should().BeSameAs(preserved);
        committed.Runs[3].Kind.Should().Be(MathRunKind.Superscript);
    }

    [Fact]
    public void Insert_ReplacesTheSelection_AndSelectsTheFirstPlaceholder()
    {
        var edit = EquationEditorSession.Insert("x=+1", 2, 0, "⬚/⬚");
        edit.Text.Should().Be("x=⬚/⬚+1");
        edit.SelectionStart.Should().Be(2);
        edit.SelectionLength.Should().Be(1);

        var symbol = EquationEditorSession.Insert("ab", 1, 1, "α");
        symbol.Text.Should().Be("aα");
        symbol.SelectionStart.Should().Be(2);
        symbol.SelectionLength.Should().Be(0);

        var clamped = EquationEditorSession.Insert(null, 99, 99, "x");
        clamped.Should().Be(new EquationEditorTextEdit("x", 1, 0));
        EquationEditorSession.Insert("abc", -5, 2, null).Should().Be(new EquationEditorTextEdit("c", 0, 0));
    }

    [Fact]
    public void AutoCorrect_ExpandsAKnownNameWhenASpaceIsTyped()
    {
        EquationEditorSession.AutoCorrect(@"x+\alpha ", 9).Should().Be(new EquationEditorTextEdit("x+α", 3, 0));
        EquationEditorSession.AutoCorrect(@"\sqrt y", 6).Should().Be(new EquationEditorTextEdit("√y", 1, 0));

        EquationEditorSession.AutoCorrect(@"\foo ", 5).Should().BeNull("unknown names stay as typed");
        EquationEditorSession.AutoCorrect(@"\\alpha ", 8).Should().BeNull("an escaped backslash is literal");
        EquationEditorSession.AutoCorrect("alpha ", 6).Should().BeNull();
        EquationEditorSession.AutoCorrect(@"\alpha", 6).Should().BeNull("only a typed space triggers it");
        EquationEditorSession.AutoCorrect(@"\alpha x", 8).Should().BeNull("the caret is not right after the name");
        EquationEditorSession.AutoCorrect(null, 0).Should().BeNull();
        EquationEditorSession.AutoCorrect(@"\alpha ", 99).Should().BeNull();
    }

    [Fact]
    public void EditorText_EveryStringIsLocalizableInBothResourceFiles()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var keys = EquationEditorText.RequiredResourceKeys;
        keys.Should().OnlyHaveUniqueItems().And.HaveCount(13);

        foreach (var file in new[] { "Strings.resx", "Strings.fr-FR.resx" })
        {
            var resources = System.Xml.Linq.XDocument
                .Load(Path.Combine(root, "freew", "FreeW.App.Localization", "Resources", file))
                .Root!
                .Elements("data")
                .ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value"));

            foreach (var key in keys)
                resources.Should().ContainKey(key, file).WhoseValue.Should().NotBeNullOrWhiteSpace();
        }

        var surface = EquationEditorText.BuildSurface(key => "localized:" + key);
        surface.Title.Should().Be("localized:EquationEditor_Dialog_Title");
        surface.Justifications.Select(entry => entry.Justification).Should().Equal(
            MathJustification.CenterGroup, MathJustification.Left, MathJustification.Center, MathJustification.Right);
        EquationEditorText.BuildSurface().Title.Should().Be("Equation");
    }

    // ── Catalogs ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SymbolCatalog_HasWordsFourSets_WithUniqueCaseInsensitiveIds()
    {
        EquationSymbolCatalog.Sets.Select(set => set.Label)
            .Should().Equal("Basic Math", "Greek Letters", "Operators", "Arrows");

        var ids = EquationSymbolCatalog.Symbols.Select(symbol => symbol.CommandId).ToList();
        ids.Should().OnlyHaveUniqueItems();
        ids.Select(id => id.ToLowerInvariant()).Should().OnlyHaveUniqueItems(
            "command ids are compared, and name icon files, without regard to case");
        ids.Should().OnlyContain(id => id == id.ToLowerInvariant());

        foreach (var symbol in EquationSymbolCatalog.Symbols)
        {
            EquationLinearSymbols.Names[symbol.Name].Should().Be(symbol.Text);
            symbol.Label.Should().NotBeNullOrWhiteSpace();
        }

        var greek = EquationSymbolCatalog.Sets.Single(set => set.Id == "greek").Symbols;
        greek.Where(symbol => !symbol.Id.StartsWith("capital-", StringComparison.Ordinal)).Should().HaveCount(24);
        string.Concat(greek.Take(3).Select(symbol => symbol.Text)).Should().Be("αβγ");
        greek.Last().Text.Should().Be("Ω");
    }

    [Fact]
    public void StructureCatalog_HasWordsElevenGalleries_InOrder()
    {
        EquationStructureCatalog.Groups.Select(group => group.Label).Should().Equal(
            "Fraction", "Script", "Radical", "Integral", "Large Operator", "Bracket", "Function", "Accent",
            "Limit and Log", "Operator", "Matrix");

        var ids = EquationStructureCatalog.Structures.Select(structure => structure.CommandId).ToList();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => id == id.ToLowerInvariant());
        EquationStructureCatalog.Groups.Should().OnlyContain(group => group.Structures.Count >= 3);
        EquationStructureCatalog.Structures.Should().OnlyContain(
            structure => structure.CommandId.StartsWith(EquationStructureCatalog.CommandPrefix + structure.GroupId + "-", StringComparison.Ordinal));
    }

    public static TheoryData<string> StructureIds()
    {
        var data = new TheoryData<string>();
        foreach (var structure in EquationStructureCatalog.Structures)
            data.Add(structure.CommandId);
        return data;
    }

    [Theory]
    [MemberData(nameof(StructureIds))]
    public void EveryStructureTemplate_IsCanonicalLinearText_ThatBuildsARealStructure(string commandId)
    {
        var structure = EquationStructureCatalog.Structures.Single(candidate => candidate.CommandId == commandId);

        var equation = EquationLinearFormat.Parse(structure.LinearText);
        var written = EquationLinearFormat.ToLinear(equation);

        written.HasOpaqueParts.Should().BeFalse();
        written.Text.Should().Be(structure.LinearText, "a template must be exactly what the editor shows for it");
        if (structure.GroupId != "operator" || structure.LinearText.Contains(EquationStructureCatalog.Placeholder, StringComparison.Ordinal))
        {
            equation.Runs.Should().Contain(
                run => run.Kind != MathRunKind.Text,
                "'{0}' must build a structure, not stay plain text",
                structure.LinearText);
        }
    }

    [Theory]
    [InlineData("fraction", MathRunKind.Fraction)]
    [InlineData("script", MathRunKind.Superscript)]
    [InlineData("radical", MathRunKind.Radical)]
    [InlineData("integral", MathRunKind.NAry)]
    [InlineData("large-operator", MathRunKind.NAry)]
    [InlineData("bracket", MathRunKind.Delimiter)]
    [InlineData("function", MathRunKind.FunctionApply)]
    [InlineData("accent", MathRunKind.Accent)]
    [InlineData("limit-and-log", MathRunKind.FunctionApply)]
    [InlineData("matrix", MathRunKind.Matrix)]
    public void FirstTemplateOfEachGallery_BuildsThatGallerysStructure(string groupId, MathRunKind expected)
    {
        var first = EquationStructureCatalog.Groups.Single(group => group.Id == groupId).Structures[0];

        EquationLinearFormat.Parse(first.LinearText).Runs.Should().ContainSingle().Which.Kind.Should().Be(expected);
    }

    // ── Built-in gallery ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PresetCatalog_HasWordsBuiltInEquations()
    {
        EquationPresetCatalog.BuiltInEquations.Select(preset => preset.BuiltInName).Should().Equal(
            "Area of Circle",
            "Binomial Theorem",
            "Expansion of a Sum",
            "Fourier Series",
            "Pythagorean Theorem",
            "Quadratic Formula",
            "Taylor Expansion",
            "Trig Identity 1",
            "Trig Identity 2");

        EquationPresetCatalog.Presets.Select(preset => preset.CommandId).Should().OnlyHaveUniqueItems();
        EquationPresetCatalog.Presets.Select(preset => preset.Kind).Should().OnlyHaveUniqueItems();
        EquationPresetCatalog.Presets.Select(preset => preset.Kind)
            .Should().BeEquivalentTo(Enum.GetValues<EquationPresetKind>());
    }

    public static TheoryData<EquationPresetKind> BuiltInKinds()
    {
        var data = new TheoryData<EquationPresetKind>();
        foreach (var preset in EquationPresetCatalog.BuiltInEquations)
            data.Add(preset.Kind);
        return data;
    }

    [Theory]
    [MemberData(nameof(BuiltInKinds))]
    public void EveryBuiltInEquation_IsFullyEditable_AndStableAsLinearText(EquationPresetKind kind)
    {
        var preset = EquationPresetCatalog.Get(kind);

        var equation = preset.CreateEquation();
        var written = EquationLinearFormat.ToLinear(equation);

        written.HasOpaqueParts.Should().BeFalse();
        written.Text.Should().Be(preset.BuiltInLinearText);
        equation.Runs.Should().Contain(run => run.Kind != MathRunKind.Text);
        preset.CreateEquation().Should().NotBeSameAs(equation, "every insertion gets a fresh model");
    }

    [Fact]
    public void BuiltInEquations_HaveTheExpectedShape()
    {
        var quadratic = EquationPresetCatalog.Get(EquationPresetKind.QuadraticFormula).CreateEquation();
        quadratic.Runs.Select(run => run.Kind).Should().Equal(MathRunKind.Text, MathRunKind.Fraction);
        quadratic.Runs[1].NumeratorEquation!.Runs.Should().Contain(run => run.Kind == MathRunKind.Radical);

        var binomial = EquationPresetCatalog.Get(EquationPresetKind.BinomialTheorem).CreateEquation();
        var sum = binomial.Runs.Single(run => run.Kind == MathRunKind.NAry);
        sum.Operator.Should().Be("∑");
        sum.Sub.Should().Be("k=0");
        var coefficient = sum.NAryOperandEquation!.Runs[0];
        coefficient.Kind.Should().Be(MathRunKind.Delimiter);
        coefficient.DelimiterContentEquation!.Runs.Single().FractionType.Should().Be(MathFractionType.NoBar);

        var fourier = EquationPresetCatalog.Get(EquationPresetKind.FourierSeries).CreateEquation();
        fourier.Runs.Single(run => run.Kind == MathRunKind.NAry).Sup.Should().Be("∞");

        var trig = EquationPresetCatalog.Get(EquationPresetKind.TrigIdentity1).CreateEquation();
        trig.Runs.Count(run => run.Kind == MathRunKind.FunctionApply).Should().Be(4);
    }

    // ── Rendering of the constructs the editor adds ──────────────────────────────────────────────────

    [Fact]
    public void VisualPlan_HidesTheBarOfANoBarFraction()
    {
        var binomial = FreeW.App.Presentation.DocumentView.EquationVisualPlanner.Build(EquationLinearFormat.Parse(@"n\atop k"));
        var fraction = FreeW.App.Presentation.DocumentView.EquationVisualPlanner.Build(EquationLinearFormat.Parse("n/k"));

        binomial.Elements.Should().ContainSingle().Which.FractionBarVisible.Should().BeFalse();
        fraction.Elements.Should().ContainSingle().Which.FractionBarVisible.Should().BeTrue();
    }

    [Fact]
    public void VisualPlan_DrawsALimitAttachedToItsBase()
    {
        var lower = FreeW.App.Presentation.DocumentView.EquationVisualPlanner.Build(new Equation([MathRun.LowerLimit("lim", "x")]));
        var upper = FreeW.App.Presentation.DocumentView.EquationVisualPlanner.Build(new Equation([MathRun.UpperLimit("x", "def")]));

        lower.Segments.Select(segment => (segment.Text, segment.Role)).Should().Equal(
            ("lim", FreeW.App.Presentation.DocumentView.EquationVisualSegmentRole.Base),
            ("x", FreeW.App.Presentation.DocumentView.EquationVisualSegmentRole.Subscript));
        upper.Segments.Select(segment => (segment.Text, segment.Role)).Should().Equal(
            ("x", FreeW.App.Presentation.DocumentView.EquationVisualSegmentRole.Base),
            ("def", FreeW.App.Presentation.DocumentView.EquationVisualSegmentRole.Superscript));
    }

    // ── Tools workflow ───────────────────────────────────────────────────────────────────────────────

    private sealed class Host
    {
        public List<Equation> Inserted { get; } = [];
        public List<string> Typed { get; } = [];
        public List<(Equation Original, Equation Replacement)> Replaced { get; } = [];
        public int NewRequests { get; set; }
        public int EditRequests { get; set; }
        public Equation? Selected { get; set; }

        public EquationToolsRibbonPorts FullPorts() => new(
            Inserted.Add,
            () => NewRequests++,
            () => EditRequests++,
            Typed.Add,
            () => Selected,
            (original, replacement) => Replaced.Add((original, replacement)));
    }

    private static IRibbonCommand Registered(FreeWRibbonCommandBindingPorts bindings, string commandId)
    {
        bindings.TryGet(new RibbonCommandId(commandId), out var command).Should().BeTrue("{0} must be registered", commandId);
        return command!;
    }

    private static bool IsEnabled(IRibbonCommand command) =>
        command is not IRibbonStatefulCommand stateful || stateful.GetState().IsEnabled;

    [Fact]
    public void Workflow_RegistersEveryCommandItDeclares()
    {
        var bindings = new FreeWRibbonCommandBindingPorts();
        EquationToolsRibbonWorkflow.Register(bindings, new Host().FullPorts());

        EquationToolsRibbonWorkflow.CommandIds.Should().OnlyHaveUniqueItems();
        foreach (var commandId in EquationToolsRibbonWorkflow.CommandIds)
            IsEnabled(Registered(bindings, commandId)).Should().BeTrue(commandId);
    }

    [Fact]
    public void Workflow_WithAnEditor_RoutesSymbolsAndStructuresToItsCaret()
    {
        var host = new Host();
        var bindings = new FreeWRibbonCommandBindingPorts();
        EquationToolsRibbonWorkflow.Register(bindings, host.FullPorts());

        Registered(bindings, EquationToolsRibbonWorkflow.NewCommandId).Execute(RibbonCommandContext.Empty);
        Registered(bindings, EquationToolsRibbonWorkflow.EditCommandId).Execute(RibbonCommandContext.Empty);
        Registered(bindings, "freew.equation-symbol-alpha").Execute(RibbonCommandContext.Empty);
        Registered(bindings, "freew.equation-symbol-capital-omega").Execute(RibbonCommandContext.Empty);
        Registered(bindings, "freew.equation-structure-fraction-stacked").Execute(RibbonCommandContext.Empty);

        host.NewRequests.Should().Be(1);
        host.EditRequests.Should().Be(1);
        host.Typed.Should().Equal("α", "Ω", "⬚/⬚");
        host.Inserted.Should().BeEmpty();
    }

    [Fact]
    public void Workflow_WithoutAnEditor_InsertsStructuresAsEquations_AndLeavesTheRestUnavailable()
    {
        var host = new Host();
        var bindings = new FreeWRibbonCommandBindingPorts();
        EquationToolsRibbonWorkflow.Register(bindings, new EquationToolsRibbonPorts(host.Inserted.Add));

        Registered(bindings, EquationToolsRibbonWorkflow.NewCommandId).Execute(RibbonCommandContext.Empty);
        Registered(bindings, "freew.equation-structure-radical-square-root").Execute(RibbonCommandContext.Empty);

        host.Inserted.Should().HaveCount(2);
        host.Inserted[0].LinearText.Should().Be(EquationPresetCatalog.CreateDefaultEquation().LinearText);
        host.Inserted[1].Runs.Should().ContainSingle().Which.Kind.Should().Be(MathRunKind.Radical);

        IsEnabled(Registered(bindings, EquationToolsRibbonWorkflow.EditCommandId)).Should().BeFalse();
        IsEnabled(Registered(bindings, "freew.equation-symbol-alpha")).Should().BeFalse();
        foreach (var (commandId, _) in EquationToolsRibbonWorkflow.Conversions)
            IsEnabled(Registered(bindings, commandId)).Should().BeFalse(commandId);
    }

    [Fact]
    public void Workflow_ConversionsReplaceTheSelectedEquation_AndDoNothingWithoutOne()
    {
        var host = new Host();
        var bindings = new FreeWRibbonCommandBindingPorts();
        EquationToolsRibbonWorkflow.Register(bindings, host.FullPorts());
        var display = Registered(bindings, EquationToolsRibbonWorkflow.DisplayCommandId);

        display.Execute(RibbonCommandContext.Empty);
        host.Replaced.Should().BeEmpty();

        host.Selected = EquationLinearFormat.Parse("a/b");
        display.Execute(RibbonCommandContext.Empty);

        var (original, replacement) = host.Replaced.Should().ContainSingle().Subject;
        original.Should().BeSameAs(host.Selected);
        replacement.Should().NotBeSameAs(host.Selected);
        replacement.IsDisplayMath.Should().BeTrue();
        host.Selected.IsDisplayMath.Should().BeFalse("the selected equation itself is never mutated");

        Registered(bindings, EquationToolsRibbonWorkflow.InlineCommandId).Execute(RibbonCommandContext.Empty);
        host.Replaced.Should().ContainSingle("an inline equation is already inline");
    }

    [Fact]
    public void Convert_LinearAndProfessional_AreInverses()
    {
        var equation = EquationLinearFormat.Parse(@"x=(-b\pm\sqrt(b^2-4ac))/(2a)");
        equation.IsDisplayMath = true;
        equation.Justification = MathJustification.Left;

        var linear = EquationToolsRibbonWorkflow.Convert(equation, EquationConversion.Linear)!;
        linear.Runs.Should().ContainSingle().Which.Kind.Should().Be(MathRunKind.Text);
        linear.Runs[0].Text.Should().Be("x=(-b±√(b^2-4ac))/(2a)");
        linear.IsDisplayMath.Should().BeTrue();
        linear.Justification.Should().Be(MathJustification.Left);

        var professional = EquationToolsRibbonWorkflow.Convert(linear, EquationConversion.Professional)!;
        EquationStructure.AreEquivalent(professional, equation, compareLayout: true).Should().BeTrue();

        EquationToolsRibbonWorkflow.Convert(professional, EquationConversion.Professional).Should().BeNull();
        EquationToolsRibbonWorkflow.Convert(linear, EquationConversion.Linear).Should().NotBeNull(
            "the linear text of linear text escapes it again, as it would in Word");
    }

    [Fact]
    public void Convert_Linear_IsRefusedWhenItWouldLoseAnOpaquePart()
    {
        var equation = new Equation([MathRun.PlainText("a+"), MathRun.PreservedOmml("<m:box xmlns:m=\"m\"/>", "b")]);

        EquationToolsRibbonWorkflow.Convert(equation, EquationConversion.Linear).Should().BeNull();
    }

    [Theory]
    [InlineData(EquationConversion.JustifyLeft, MathJustification.Left)]
    [InlineData(EquationConversion.JustifyCenter, MathJustification.Center)]
    [InlineData(EquationConversion.JustifyRight, MathJustification.Right)]
    public void Convert_Justification_MakesTheEquationDisplay(EquationConversion conversion, MathJustification expected)
    {
        var equation = EquationLinearFormat.Parse("a/b");

        var converted = EquationToolsRibbonWorkflow.Convert(equation, conversion)!;

        converted.IsDisplayMath.Should().BeTrue();
        converted.Justification.Should().Be(expected);
        EquationToolsRibbonWorkflow.Convert(converted, conversion).Should().BeNull();
        EquationToolsRibbonWorkflow.Convert(converted, EquationConversion.JustifyCenterAsGroup)!
            .Justification.Should().Be(MathJustification.CenterGroup);

        var inline = EquationToolsRibbonWorkflow.Convert(converted, EquationConversion.Inline)!;
        inline.IsDisplayMath.Should().BeFalse();
        inline.Justification.Should().Be(MathJustification.CenterGroup);
    }
}
