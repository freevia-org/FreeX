using System.Text.Json;
using FreeX.App.Presentation.FormulaBar;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private const string NameBoxDropdownPhysicalFixtureArgument = "--freex-name-box-dropdown-physical";
    private const string NameBoxDropdownParityPhysicalFixtureArgument = "--freex-name-box-dropdown-parity-physical";
    private const string NameBoxDropdownPhysicalEvidenceArgument = "--freex-name-box-dropdown-physical-evidence";
    private int _nameBoxDropdownPhysicalEvidenceSequence;

    partial void PrepareOptionalStartupState(IReadOnlyList<string> startupArguments)
    {
        if (HasStartupArgument(
                startupArguments,
                NameBoxDropdownPhysicalFixtureArgument))
        {
            SeedNameBoxDropdownPhysicalFixture();
        }

        if (HasStartupArgument(
                startupArguments,
                NameBoxDropdownParityPhysicalFixtureArgument))
        {
            SeedNameBoxDropdownParityFixture();
        }
    }

    partial void CompleteOptionalStartupState(IReadOnlyList<string> startupArguments)
    {
        if (HasStartupArgument(startupArguments, NameBoxDropdownPhysicalFixtureArgument) ||
            HasStartupArgument(startupArguments, NameBoxDropdownParityPhysicalFixtureArgument))
        {
            InitializeNameBoxDropdownPhysicalEvidence();
        }
    }

    partial void CompleteOptionalStartupFileOpen()
    {
        var startupArguments = App.StartupArguments;
        var fixtureSeeded = false;
        if (HasStartupArgument(startupArguments, NameBoxDropdownPhysicalFixtureArgument))
        {
            SeedNameBoxDropdownPhysicalFixture();
            fixtureSeeded = true;
        }

        if (HasStartupArgument(startupArguments, NameBoxDropdownParityPhysicalFixtureArgument))
        {
            SeedNameBoxDropdownParityFixture();
            fixtureSeeded = true;
        }

        if (!fixtureSeeded)
            return;

        RefreshShell(_statusText.Text ?? "Ready");
        InitializeNameBoxDropdownPhysicalEvidence();
    }

    partial void RecordOptionalNeutralCellSelection() =>
        RecordNameBoxDropdownPhysicalEvidence(item: null, stage: "neutral-cell-selected");

    partial void RecordOptionalNameBoxSelection(NameBoxNavigationItem item) =>
        RecordNameBoxDropdownPhysicalEvidence(item, "object-selected");

    partial void RecordOptionalNameBoxPopupOpened(string host, int x, int y, int width, int height) =>
        RecordNameBoxDropdownPhysicalEvidence(
            item: null,
            stage: "popup-opened",
            popupHost: host,
            popupX: x,
            popupY: y,
            popupWidth: width,
            popupHeight: height);

    private static bool HasStartupArgument(IReadOnlyList<string> arguments, string expected) =>
        arguments.Any(argument => string.Equals(
            argument,
            expected,
            StringComparison.OrdinalIgnoreCase));

    // The physical X11 lane needs stable non-defined-name entries without a user-authored file.
    private void SeedNameBoxDropdownPhysicalFixture()
    {
        var sheet = _session.ActiveSheet;
        var firstCell = new CellAddress(sheet.Id, 1, 1);
        var tableLastCell = new CellAddress(sheet.Id, 2, 2);
        _session.Workbook.NamedRanges["PhysicalName"] = new GridRange(firstCell, firstCell);
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 6701,
            Name = "PhysicalTable",
            DisplayName = "PhysicalTable",
            Range = new GridRange(firstCell, tableLastCell),
            HeaderRowCount = 1,
            TotalsRowCount = 0,
            HasAutoFilter = true,
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Id = Guid.Parse("67000000-0000-0000-0000-000000000001"),
            Name = "PhysicalShape",
            Anchor = new CellAddress(sheet.Id, 2, 4),
            Width = 96,
            Height = 48,
            IsVisible = true,
        });
        sheet.Pictures.Add(new PictureModel
        {
            Id = Guid.Parse("67000000-0000-0000-0000-000000000002"),
            Name = "PhysicalPicture",
            Anchor = new CellAddress(sheet.Id, 3, 4),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3, 4],
            ContentType = "image/png",
            Width = 96,
            Height = 48,
            IsVisible = true,
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Id = Guid.Parse("67000000-0000-0000-0000-000000000003"),
            Name = "PhysicalTextBox",
            Anchor = new CellAddress(sheet.Id, 4, 4),
            Text = "Physical Name Box text box",
            Width = 120,
            Height = 48,
            IsVisible = true,
        });
        sheet.Charts.Add(new ChartModel
        {
            Id = Guid.Parse("67000000-0000-0000-0000-000000000004"),
            Name = "PhysicalChart",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 5, 4),
                new CellAddress(sheet.Id, 6, 5)),
            IsVisible = true,
        });
    }

    // The managed parity capture and the physical X11 lane use separate opt-in fixtures so
    // neither contract can change the other's object identities or ordering.
    private void SeedNameBoxDropdownParityFixture()
    {
        var sheet = _session.ActiveSheet;
        foreach (var name in _session.Workbook.NamedRanges.Keys.ToArray())
            _session.Workbook.RemoveNamedRange(name);
        foreach (var scopedName in _session.Workbook.ScopedNamedRanges.Keys.ToArray())
            _session.Workbook.RemoveScopedNamedRange(scopedName.Name, scopedName.Sheet);
        foreach (var workbookSheet in _session.Workbook.Sheets)
        {
            workbookSheet.StructuredTables.Clear();
            workbookSheet.DrawingShapes.Clear();
            workbookSheet.Pictures.Clear();
            workbookSheet.TextBoxes.Clear();
            workbookSheet.Charts.Clear();
        }

        _session.Workbook.NamedRanges["Sales"] = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3));
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Id = Guid.Parse("68000000-0000-0000-0000-000000000001"),
            Name = "Tour Name Box Shape",
            Anchor = new CellAddress(sheet.Id, 22, 8),
            Width = 96,
            Height = 48,
            IsVisible = true,
        });
        sheet.Pictures.Add(new PictureModel
        {
            Id = Guid.Parse("68000000-0000-0000-0000-000000000002"),
            Name = "Tour Name Box Picture",
            Anchor = new CellAddress(sheet.Id, 23, 8),
            Kind = PictureKind.Image,
            ImageBytes = [1, 2, 3, 4],
            ContentType = "image/png",
            Width = 96,
            Height = 48,
            IsVisible = true,
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Id = Guid.Parse("68000000-0000-0000-0000-000000000003"),
            Name = "Tour Name Box Text Box",
            Anchor = new CellAddress(sheet.Id, 24, 8),
            Text = "Tour Name Box text box",
            Width = 120,
            Height = 48,
            IsVisible = true,
        });
        sheet.Charts.Add(new ChartModel
        {
            Id = Guid.Parse("68000000-0000-0000-0000-000000000004"),
            Name = "Tour Name Box Chart",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 25, 8),
                new CellAddress(sheet.Id, 26, 9)),
            IsVisible = true,
        });
        _session.SelectRange(new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3)));
        RefreshShell(_statusText.Text ?? "Ready");
    }

    private void InitializeNameBoxDropdownPhysicalEvidence()
    {
        RecordNameBoxDropdownPhysicalEvidence(item: null, stage: "fixture-seeded");
    }

    private void RecordNameBoxDropdownPhysicalEvidence(
        NameBoxNavigationItem? item,
        string stage,
        string? popupHost = null,
        int? popupX = null,
        int? popupY = null,
        int? popupWidth = null,
        int? popupHeight = null)
    {
        var path = FindNameBoxDropdownPhysicalEvidencePath(App.StartupArguments);
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var selectedKind = _selectedDrawingObjectKind?.ToString();
            var payload = new
            {
                sequence = ++_nameBoxDropdownPhysicalEvidenceSequence,
                utc = DateTimeOffset.UtcNow,
                stage,
                itemName = item?.Name,
                itemKind = item?.Kind.ToString(),
                itemObjectKind = item?.ObjectKind?.ToString(),
                nameBoxText = _cellAddressText.Text ?? string.Empty,
                selectedObjectKind = selectedKind,
                selectedObjectId = _selectedDrawingObjectId?.ToString(),
                activeSheetId = _session.ActiveSheet.Id.ToString(),
                activeCell = _session.ActiveCell.ToA1(),
                contextualState = selectedKind ?? "None",
                popupHost,
                popupX,
                popupY,
                popupWidth,
                popupHeight,
            };
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.AppendAllText(path, JsonSerializer.Serialize(payload) + Environment.NewLine);
        }
        catch
        {
            // Physical evidence is opt-in and must never alter normal worksheet behavior.
        }
    }

    private static string? FindNameBoxDropdownPhysicalEvidencePath(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (string.Equals(
                    arguments[index],
                    NameBoxDropdownPhysicalEvidenceArgument,
                    StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }
}
