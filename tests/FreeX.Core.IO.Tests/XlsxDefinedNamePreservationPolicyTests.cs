using System.IO.Compression;
using System.Diagnostics;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxDefinedNamePreservationPolicyTests
{
    private static readonly XNamespace WorkbookNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void PrepareCandidate_RemapsByNameWithoutChangingPayloadOrSource()
    {
        var workbook = new Workbook("Test");
        var other = workbook.AddSheet("Other");
        var scope = workbook.AddSheet("Scope");
        var source = CreateDefinedName("Legacy", "'Scope'!$A$1", 0);
        source.SetAttributeValue("hidden", "1");
        source.SetAttributeValue("comment", "keep");
        source.SetAttributeValue("function", "1");
        source.SetAttributeValue("vbProcedure", "1");
        source.SetAttributeValue("xlm", "1");

        var policy = new XlsxDefinedNamePreservationPolicy(
            workbook,
            ["Scope", "Other"],
            [scope.Id, other.Id],
            ["Other", "Scope"]);

        policy.TryPrepareCandidate(source, out var candidate).Should().BeTrue();
        candidate.Attribute("localSheetId")!.Value.Should().Be("1");
        candidate.Value.Should().Be("'Scope'!$A$1");
        candidate.Attributes().Where(attribute => attribute.Name.LocalName != "localSheetId")
            .Select(attribute => (attribute.Name, attribute.Value))
            .Should().Equal(source.Attributes()
                .Where(attribute => attribute.Name.LocalName != "localSheetId")
                .Select(attribute => (attribute.Name, attribute.Value)));
        source.Attribute("localSheetId")!.Value.Should().Be("0");
    }

    [Fact]
    public void PrepareCandidate_UsesStableIdForRenameAndRejectsDeadOrMalformedScopes()
    {
        var workbook = new Workbook("Test");
        var renamed = workbook.AddSheet("Renamed");
        var policy = new XlsxDefinedNamePreservationPolicy(
            workbook,
            ["Original", "Deleted"],
            [renamed.Id, SheetId.New()],
            ["Renamed"]);

        policy.TryPrepareCandidate(CreateDefinedName("Kept", "#REF!", 0), out var renamedCandidate)
            .Should().BeTrue();
        renamedCandidate.Attribute("localSheetId")!.Value.Should().Be("0");
        policy.TryPrepareCandidate(CreateDefinedName("Deleted", "#REF!", 1), out _).Should().BeFalse();

        foreach (var malformedScope in new[] { "-1", "2", "not-an-index" })
        {
            var malformed = CreateDefinedName("Malformed", "#REF!", null);
            malformed.SetAttributeValue("localSheetId", malformedScope);
            policy.TryPrepareCandidate(malformed, out _).Should().BeFalse();
        }

        var workbookScoped = CreateDefinedName("Global", "#REF!", null);
        policy.TryPrepareCandidate(workbookScoped, out var globalCandidate).Should().BeTrue();
        globalCandidate.Should().NotBeSameAs(workbookScoped);
    }

    [Fact]
    public void LivenessAndPrintPolicy_DistinguishesLiveDeletedReservedAndOpaqueNames()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.DefineNamedFormula("LiveFormula", "1+1", sheet.Id);
        var policy = new XlsxDefinedNamePreservationPolicy(
            workbook,
            ["Sheet1"],
            [sheet.Id],
            ["Sheet1"]);

        policy.ShouldPreserveModelCandidate(CreateDefinedName("LiveFormula", "1+1", 0)).Should().BeTrue();
        policy.ShouldPreserveModelCandidate(CreateDefinedName("DeletedFormula", "1+1", 0)).Should().BeFalse();
        policy.ShouldPreserveModelCandidate(CreateDefinedName("Opaque", "#REF!", 0)).Should().BeTrue();
        policy.ShouldPreserveModelCandidate(CreateDefinedName("_xlnm.FilterDatabase", "#REF!", 0))
            .Should().BeTrue();

        var printArea = CreateDefinedName("_xlnm.Print_Area", "'Sheet1'!$A$1:$B$2", 0);
        var printTitles = CreateDefinedName("_xlnm.Print_Titles", "'Sheet1'!$1:$1", 0);
        policy.ShouldPreservePrintSetting(printArea).Should().BeFalse();
        policy.ShouldPreservePrintSetting(printTitles).Should().BeFalse();

        sheet.SetPrintAreas([new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2))]);
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        policy.ShouldPreservePrintSetting(printArea).Should().BeTrue();
        policy.ShouldPreservePrintSetting(printTitles).Should().BeTrue();
    }

    [Fact]
    public void KeyAndBackfill_PreserveScopeAndOnlyFillMissingAttributes()
    {
        var source = CreateDefinedName("Legacy", "source formula", 3);
        source.SetAttributeValue("hidden", "1");
        source.SetAttributeValue("comment", "source comment");
        source.SetAttributeValue("description", "description");
        source.SetAttributeValue("function", "1");
        source.SetAttributeValue("vbProcedure", "1");
        source.SetAttributeValue("xlm", "1");
        source.SetAttributeValue("customMenu", "menu");
        source.SetAttributeValue("help", "help");
        source.SetAttributeValue("statusBar", "status");
        source.SetAttributeValue("functionGroupId", "7");
        source.SetAttributeValue("shortcutKey", "K");
        source.SetAttributeValue("publishToServer", "1");
        source.SetAttributeValue("workbookParameter", "1");
        var target = CreateDefinedName("Legacy", "target formula", 3);
        target.SetAttributeValue("comment", "target comment");

        XlsxDefinedNamePreservationPolicy.GetKey(source).Should().Be("Legacy\u001f3");
        XlsxDefinedNamePreservationPolicy.BackfillMissingAttributes(source, target).Should().BeTrue();
        target.Value.Should().Be("target formula");
        target.Attribute("comment")!.Value.Should().Be("target comment");
        foreach (var attribute in source.Attributes().Where(attribute => attribute.Name.LocalName != "comment"))
            target.Attribute(attribute.Name)!.Value.Should().Be(attribute.Value);
        XlsxDefinedNamePreservationPolicy.BackfillMissingAttributes(source, target).Should().BeFalse();
    }

    [Fact]
    public void FirstElementIndex_PreservesFirstDuplicateAndSeparatesGlobalFromScopedNames()
    {
        var firstGlobal = CreateDefinedName("Shared", "first", null);
        var caseVariantDuplicate = CreateDefinedName("shared", "duplicate", null);
        var scoped = CreateDefinedName("Shared", "scoped", 0);

        var index = XlsxDefinedNamePreservationPolicy.CreateFirstElementIndex(
            [firstGlobal, caseVariantDuplicate, scoped]);

        index.Should().HaveCount(2);
        index[XlsxDefinedNamePreservationPolicy.GetKey(firstGlobal)].Should().BeSameAs(firstGlobal);
        index[XlsxDefinedNamePreservationPolicy.GetKey(scoped)].Should().BeSameAs(scoped);

        var appended = CreateDefinedName("Added", "appended", 1);
        index.TryAdd(XlsxDefinedNamePreservationPolicy.GetKey(appended), appended).Should().BeTrue();
        index[XlsxDefinedNamePreservationPolicy.GetKey(appended)].Should().BeSameAs(appended);
    }

    [Fact]
    public void DefinedNamePreservers_IndexTargetsOnceWithoutPerSourceTargetRescans()
    {
        var fullSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorkbookMetadataPreserver.cs");
        var fullMethod = fullSource[
            fullSource.IndexOf("private static bool MergeDefinedNames", StringComparison.Ordinal)..
            fullSource.IndexOf("private static void InsertCustomWorkbookViewsInOrder", StringComparison.Ordinal)];
        var patchSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SourcePackageSnapshot.cs");
        var patchMethod = patchSource[
            patchSource.IndexOf("private static void RestorePatchWorkbookDefinedNames", StringComparison.Ordinal)..
            patchSource.IndexOf("private static readonly string[] RestoredDefinedNamesPrecedingSiblings", StringComparison.Ordinal)];

        fullMethod.Should().Contain("CreateFirstElementIndex(");
        fullMethod.Should().Contain("existingNamesByKey.TryGetValue(key, out var existingElement)");
        fullMethod.Should().Contain("existingNamesByKey.TryAdd(key, candidate)");
        fullMethod.Should().NotContain(".FirstOrDefault(");
        patchMethod.Should().Contain("CreateFirstElementIndex(");
        patchMethod.Should().Contain("existingNamesByKey.TryGetValue(key, out var existing)");
        patchMethod.Should().Contain("existingNamesByKey.TryAdd(key, resurrected)");
        patchMethod.Should().NotContain(".FirstOrDefault(");
    }

    [BenchmarkFact]
    public void Benchmark_FirstElementIndexTenThousandNames_ReportsTimingAndAllocation()
    {
        const int nameCount = 10_000;
        const int iterations = 5;
        var names = Enumerable.Range(0, nameCount)
            .Select(index => CreateDefinedName($"Name{index}", $"Sheet1!$A${index + 1}", index % 4))
            .ToArray();

        XlsxDefinedNamePreservationPolicy.CreateFirstElementIndex(names).Should().HaveCount(nameCount);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var elapsed = new double[iterations];
        var found = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var stopwatch = Stopwatch.StartNew();
            var index = XlsxDefinedNamePreservationPolicy.CreateFirstElementIndex(names);
            for (var nameIndex = 0; nameIndex < names.Length; nameIndex++)
            {
                if (index.ContainsKey(XlsxDefinedNamePreservationPolicy.GetKey(names[nameIndex])))
                    found++;
            }
            stopwatch.Stop();
            elapsed[iteration] = stopwatch.Elapsed.TotalMilliseconds;
        }

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        found.Should().Be(nameCount * iterations);
        Console.WriteLine(
            "PERF DEFINED_NAME_INDEX " +
            $"names={nameCount} steps={iterations} mean_ms={elapsed.Average():F2} " +
            $"max_ms={elapsed.Max():F2} allocated_bytes={allocatedBytes:N0}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PackageRoundTrip_PreservesCompleteOpaquePayloadOnBothSavePaths(bool forceFullSave)
    {
        var workbook = new Workbook("Test");
        var data = workbook.AddSheet("Data");
        workbook.AddSheet("Other");
        data.SetCell(new CellAddress(data.Id, 1, 1), new TextValue("source"));
        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddRichDefinedName(source);

        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var loaded = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(loaded, out var blockReason)
            .Should().BeTrue(blockReason);
        if (forceFullSave)
            loaded.Sheets.Single(sheet => sheet.Name == "Other").Name = "Renamed";
        else
            loaded.Sheets.Single(sheet => sheet.Name == "Data").SetCell(
                new CellAddress(loaded.Sheets[0].Id, 2, 1),
                new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var expectedPath = forceFullSave ? XlsxSavePath.FullSave : XlsxSavePath.SourcePatch;
        adapter.LastSaveDiagnostics!.Path.Should().Be(expectedPath, adapter.LastSaveDiagnostics.Reason);
        var candidate = ReadDefinedNames(saved).Should().ContainSingle().Subject;
        candidate.Value.Should().Be("#REF!");
        candidate.Attribute("localSheetId")!.Value.Should().Be("0");
        foreach (var expected in RichAttributes)
            candidate.Attribute(expected.Key)!.Value.Should().Be(expected.Value);

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.Sheets.Should().HaveCount(2);
    }

    private static readonly IReadOnlyDictionary<string, string> RichAttributes =
        new Dictionary<string, string>
        {
            ["name"] = "OpaqueLocal",
            ["hidden"] = "1",
            ["comment"] = "comment",
            ["description"] = "description",
            ["function"] = "1",
            ["vbProcedure"] = "1",
            ["xlm"] = "1",
            ["customMenu"] = "menu",
            ["help"] = "help",
            ["statusBar"] = "status",
            ["functionGroupId"] = "7",
            ["shortcutKey"] = "K",
            ["publishToServer"] = "1",
            ["workbookParameter"] = "1",
        };

    private static XElement CreateDefinedName(string name, string formula, int? localSheetId)
    {
        var element = new XElement(WorkbookNs + "definedName", new XAttribute("name", name), formula);
        if (localSheetId is { } scope)
            element.SetAttributeValue("localSheetId", scope);
        return element;
    }

    private static void AddRichDefinedName(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        XDocument document;
        using (var stream = entry.Open())
            document = XDocument.Load(stream);

        var definedNames = document.Root!.Element(WorkbookNs + "definedNames");
        if (definedNames is null)
        {
            definedNames = new XElement(WorkbookNs + "definedNames");
            document.Root.Element(WorkbookNs + "sheets")!.AddAfterSelf(definedNames);
        }

        var candidate = CreateDefinedName("OpaqueLocal", "#REF!", 0);
        foreach (var attribute in RichAttributes.Where(attribute => attribute.Key != "name"))
            candidate.SetAttributeValue(attribute.Key, attribute.Value);
        definedNames.Add(candidate);

        entry.Delete();
        var replacement = archive.CreateEntry("xl/workbook.xml");
        using var replacementStream = replacement.Open();
        document.Save(replacementStream, SaveOptions.DisableFormatting);
        package.Position = 0;
    }

    private static List<XElement> ReadDefinedNames(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        using var stream = archive.GetEntry("xl/workbook.xml")!.Open();
        return XDocument.Load(stream).Root!
            .Element(WorkbookNs + "definedNames")!
            .Elements(WorkbookNs + "definedName")
            .Where(element => element.Attribute("name")?.Value == "OpaqueLocal")
            .ToList();
    }
}
