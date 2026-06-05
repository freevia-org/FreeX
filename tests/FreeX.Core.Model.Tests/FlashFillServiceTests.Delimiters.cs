using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FlashFillServiceTests
{
    // ── Extract by delimiter ──────────────────────────────────────────────────

    [Fact]
    public void Fill_ExtractFirstWord_ExtractsPartZeroBySpace()
    {
        var result = FlashFillService.Fill(
            [("John Smith", "John"), ("Jane Doe", "Jane")],
            ["Bob Brown"]);

        result.Should().BeEquivalentTo(["Bob"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractLastWord_ExtractsPartOneBySpace()
    {
        var result = FlashFillService.Fill(
            [("John Smith", "Smith"), ("Jane Doe", "Doe")],
            ["Bob Brown"]);

        result.Should().BeEquivalentTo(["Brown"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractEmailUsername_ExtractsPartZeroByAt()
    {
        var result = FlashFillService.Fill(
            [("alice@example.com", "alice"), ("bob@test.org", "bob")],
            ["carol@domain.net"]);

        result.Should().BeEquivalentTo(["carol"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractFileExtension_ExtractsPartAfterDot()
    {
        var result = FlashFillService.Fill(
            [("report.xlsx", "xlsx"), ("budget.csv", "csv")],
            ["notes.txt"]);

        result.Should().BeEquivalentTo(["txt"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractFileExtension_ReturnsNullWhenRemainingDotIsMissing()
    {
        var result = FlashFillService.Fill(
            [("report.xlsx", "xlsx"), ("budget.csv", "csv")],
            ["notes"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_ExtractFinalDottedToken_HandlesDifferentDotCounts()
    {
        var result = FlashFillService.Fill(
            [("report.final.xlsx", "xlsx"), ("budget.csv", "csv")],
            ["notes.archive.txt"]);

        result.Should().BeEquivalentTo(["txt"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractFinalDottedToken_ReturnsNullWhenRemainingDotIsMissing()
    {
        var result = FlashFillService.Fill(
            [("report.final.xlsx", "xlsx"), ("budget.csv", "csv")],
            ["notes"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("SKU-2024-Retail", "Retail", "SKU-Wholesale", "Wholesale", "SKU-North-America-Online", "Online")]
    [InlineData("Sales/West/Retail", "Retail", "Sales/Wholesale", "Wholesale", "Global/North/America/Online", "Online")]
    [InlineData("Archive\\2024\\Retail", "Retail", "Archive\\Wholesale", "Wholesale", "Archive\\North\\America\\Online", "Online")]
    [InlineData("Cost_Center_Retail", "Retail", "Channel_Wholesale", "Wholesale", "Org_North_America_Online", "Online")]
    public void Fill_ExtractFinalDelimitedToken_UsesRightmostTokenAcrossVariableCounts(
        string source1,
        string expected1,
        string source2,
        string expected2,
        string remaining,
        string expectedRemaining)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            [remaining]);

        result.Should().BeEquivalentTo([expectedRemaining], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(
        @"C:\Reports\Q1.xlsx",
        "Reports",
        @"D:\Archive\Sales.final.csv",
        "Archive",
        @"\\share\dept\Budget.v2.txt",
        "dept")]
    [InlineData(
        "SKU-2024-Retail",
        "2024",
        "SKU-2025-Wholesale",
        "2025",
        "SKU-NORTH-2026-Online",
        "2026")]
    [InlineData(
        "/reports/q1.xlsx",
        "reports",
        "/archive/sales.csv",
        "archive",
        "/mnt/ops/budget.txt",
        "ops")]
    public void Fill_ExtractPenultimateDelimitedToken_UsesSecondTokenFromRightAcrossVariableCounts(
        string source1,
        string expected1,
        string source2,
        string expected2,
        string remaining,
        string expectedRemaining)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            [remaining]);

        result.Should().BeEquivalentTo([expectedRemaining], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("SKU")]
    [InlineData("SKU-")]
    [InlineData("SKU--Retail")]
    public void Fill_ExtractPenultimateDelimitedToken_ReturnsNullWhenRemainingTokenIsMissing(string remaining)
    {
        var result = FlashFillService.Fill(
            [("SKU-2024-Retail", "2024"), ("SKU-2025-Wholesale", "2025")],
            [remaining]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("SKU-2024-0001", "SKU-2024", "SKU-2025-0042", "SKU-2025", "SKU-NORTH-2026-0007", "SKU-NORTH-2026")]
    [InlineData("Archive/2024/January", "Archive/2024", "Archive/2025/February", "Archive/2025", "Archive/North/America/March", "Archive/North/America")]
    [InlineData("Archive\\2024\\January", "Archive\\2024", "Archive\\2025\\February", "Archive\\2025", "Archive\\North\\America\\March", "Archive\\North\\America")]
    [InlineData("Cost_Center_Retail", "Cost_Center", "Channel_Wholesale", "Channel", "Org_North_America_Online", "Org_North_America")]
    public void Fill_RemoveFinalDelimitedToken_DropsRightmostTokenAcrossVariableCounts(
        string source1,
        string expected1,
        string source2,
        string expected2,
        string remaining,
        string expectedRemaining)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            [remaining]);

        result.Should().BeEquivalentTo([expectedRemaining], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("US-East-001", "East-001", "EU-West-002", "West-002", "APAC-South-003", "South-003")]
    [InlineData("Region,East,001", "East,001", "Market,West,002", "West,002", "Area,South,003", "South,003")]
    [InlineData("Region;East;001", "East;001", "Market;West;002", "West;002", "Area;South;003", "South;003")]
    [InlineData("Region:East:001", "East:001", "Market:West:002", "West:002", "Area:South:003", "South:003")]
    [InlineData("Region|East|001", "East|001", "Market|West|002", "West|002", "Area|South|003", "South|003")]
    [InlineData("Region_East_001", "East_001", "Market_West_002", "West_002", "Area_South_003", "South_003")]
    [InlineData("Region/East/001", "East/001", "Market/West/002", "West/002", "Area/South/003", "South/003")]
    [InlineData("Region\\East\\001", "East\\001", "Market\\West\\002", "West\\002", "Area\\South\\003", "South\\003")]
    public void Fill_RemoveLeadingDelimitedToken_DropsLeftmostTokenAcrossSupportedDelimiters(
        string source1,
        string expected1,
        string source2,
        string expected2,
        string remaining,
        string expectedRemaining)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            [remaining]);

        result.Should().BeEquivalentTo([expectedRemaining], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_RemoveLeadingDelimitedToken_TrimsOuterWhitespaceAndPreservesInternalDelimiters()
    {
        var result = FlashFillService.Fill(
            [
                ("US - East - 001", "East - 001"),
                ("EU - West - 002", "West - 002")
            ],
            ["EMEA - North - 003"]);

        result.Should().BeEquivalentTo(["North - 003"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("APACSouth003")]
    [InlineData("-South-003")]
    [InlineData("APAC-")]
    [InlineData("APAC-   ")]
    public void Fill_RemoveLeadingDelimitedToken_ReturnsNullWhenRemainingCannotSatisfyPattern(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("US-East-001", "East-001"),
                ("EU-West-002", "West-002")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_RemoveLeadingDottedToken_DropsLeftmostTokenAcrossVariableCounts()
    {
        var result = FlashFillService.Fill(
            [
                ("Region.East.001", "East.001"),
                ("Market.West.002", "West.002")
            ],
            ["Area.South.003", "Global.North.America.004"]);

        result.Should().BeEquivalentTo(["South.003", "North.America.004"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_RemoveLeadingDottedToken_TrimsOuterWhitespaceAndPreservesInternalDots()
    {
        var result = FlashFillService.Fill(
            [
                (" Region . East . 001 ", "East . 001"),
                (" Market . West . 002 ", "West . 002")
            ],
            [" Area . South . 003 "]);

        result.Should().BeEquivalentTo(["South . 003"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("AreaSouth003")]
    [InlineData(".South.003")]
    [InlineData("Area.")]
    [InlineData("Area.   ")]
    [InlineData("   .South.003")]
    public void Fill_RemoveLeadingDottedToken_ReturnsNullWhenRemainingCannotSatisfyPattern(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("Region.East.001", "East.001"),
                ("Market.West.002", "West.002")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(
        @"C:\Reports\Q1.xlsx",
        "Q1",
        @"D:\Archive\Sales.final.csv",
        "Sales.final",
        @"\\share\dept\Budget.v2.txt",
        "Budget.v2")]
    [InlineData(
        "/reports/q1.xlsx",
        "q1",
        "/archive/sales.final.csv",
        "sales.final",
        "/mnt/ops/budget.v2.txt",
        "budget.v2")]
    public void Fill_ExtractFinalPathSegmentStem_RemovesPathAndExtension(
        string source1,
        string expected1,
        string source2,
        string expected2,
        string remaining,
        string expectedRemaining)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            [remaining]);

        result.Should().BeEquivalentTo([expectedRemaining], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(@"C:\Reports\README")]
    [InlineData("report.xlsx")]
    public void Fill_ExtractFinalPathSegmentStem_ReturnsNullWhenRemainingIsNotAFilePathStem(string remaining)
    {
        var result = FlashFillService.Fill(
            [(@"C:\Reports\north.xlsx", "north"), (@"D:\Archive\sales.summary.csv", "sales.summary")],
            [remaining]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(
        @"C:\Reports\North\Revenue.xlsx",
        "North",
        @"D:\Archive\South\Sales.final.csv",
        "South",
        @"E:\Finance\West\Budget.v2.txt",
        "West")]
    [InlineData(
        "/mnt/reports/North/revenue.xlsx",
        "North",
        "/mnt/archive/South/sales.final.csv",
        "South",
        "/mnt/finance/West/budget.v2.txt",
        "West")]
    [InlineData(
        @"\\server\share\Finance\Budget.xlsx",
        "Finance",
        @"\\server\share\Legal\Contracts.csv",
        "Legal",
        @"\\server\share\Operations\Plan.txt",
        "Operations")]
    public void Fill_ExtractFileParentDirectoryName_ExtractsDirectoryBeforeFinalFileSegment(
        string source1,
        string expected1,
        string source2,
        string expected2,
        string remaining,
        string expectedRemaining)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            [remaining]);

        result.Should().BeEquivalentTo([expectedRemaining], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractFileParentDirectoryTitle_TitleizesParentDirectorySlug()
    {
        var result = FlashFillService.Fill(
            [
                (@"C:\Reports\north-america\Revenue.xlsx", "North America"),
                (@"D:\Archive\south_america\Sales.final.csv", "South America")
            ],
            [@"E:\Finance\west-europe\Budget.v2.txt"]);

        result.Should().BeEquivalentTo(["West Europe"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractFileParentDirectoryTitle_TitleizesParentDirectoryWithSpaces()
    {
        var result = FlashFillService.Fill(
            [
                (@"/mnt/reports/north america/revenue.xlsx", "North America"),
                (@"/mnt/archive/south america/sales.final.csv", "South America")
            ],
            [@"/mnt/finance/west europe/budget.v2.txt"]);

        result.Should().BeEquivalentTo(["West Europe"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("Revenue.xlsx")]
    [InlineData(@"C:\Revenue.xlsx")]
    [InlineData(@"C:\Reports\")]
    [InlineData(@"C:\Reports\README")]
    [InlineData("/revenue.xlsx")]
    public void Fill_ExtractFileParentDirectoryName_ReturnsNullWhenRemainingHasNoParentDirectoryAndFile(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                (@"C:\Reports\North\Revenue.xlsx", "North"),
                (@"D:\Archive\South\Sales.final.csv", "South")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_ExtractSemicolonDelimitedToken_ExtractsConsistentPart()
    {
        var result = FlashFillService.Fill(
            [("SKU-001;Retail;West", "Retail"), ("SKU-002;Wholesale;East", "Wholesale")],
            ["SKU-003;Online;North"]);

        result.Should().BeEquivalentTo(["Online"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("Smith, John", "John", "Doe, Jane", "Jane", "Brown, Bob", "Bob")]
    [InlineData("SKU-001; Retail; West", "Retail", "SKU-002; Wholesale; East", "Wholesale", "SKU-003; Online; North", "Online")]
    [InlineData("SKU-001 | Retail | West", "Retail", "SKU-002 | Wholesale | East", "Wholesale", "SKU-003 | Online | North", "Online")]
    public void Fill_ExtractDelimitedToken_TrimsTokenEdges(
        string source1,
        string expected1,
        string source2,
        string expected2,
        string remaining,
        string expectedRemaining)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            [remaining]);

        result.Should().BeEquivalentTo([expectedRemaining], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractColonDelimitedToken_ExtractsConsistentPart()
    {
        var result = FlashFillService.Fill(
            [("08:15", "08"), ("14:45", "14")],
            ["19:30"]);

        result.Should().BeEquivalentTo(["19"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractSemicolonDelimitedToken_ReturnsNullWhenRemainingDelimiterIsMissing()
    {
        var result = FlashFillService.Fill(
            [("SKU-001;Retail;West", "Retail"), ("SKU-002;Wholesale;East", "Wholesale")],
            ["SKU-003 Online North"]);

        result.Should().BeNull();
    }

}
