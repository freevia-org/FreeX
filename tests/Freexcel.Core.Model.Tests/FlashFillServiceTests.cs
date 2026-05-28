using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class FlashFillServiceTests
{
    // ── Constant fill ─────────────────────────────────────────────────────────

    [Fact]
    public void Fill_ConstantPattern_FillsAllWithConstant()
    {
        var result = FlashFillService.Fill(
            [("Alice", "Hello"), ("Bob", "Hello")],
            ["Carol", "Dave"]);

        result.Should().BeEquivalentTo(["Hello", "Hello"], o => o.WithStrictOrdering());
    }

    // ── Case transforms ───────────────────────────────────────────────────────

    [Fact]
    public void Fill_UpperCase_TransformsSourceToUpper()
    {
        var result = FlashFillService.Fill(
            [("alice", "ALICE"), ("bob", "BOB")],
            ["carol"]);

        result.Should().BeEquivalentTo(["CAROL"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_LowerCase_TransformsSourceToLower()
    {
        var result = FlashFillService.Fill(
            [("ALICE", "alice"), ("BOB", "bob")],
            ["CAROL"]);

        result.Should().BeEquivalentTo(["carol"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ProperCase_TransformsSourceToTitleCase()
    {
        var result = FlashFillService.Fill(
            [("alice smith", "Alice Smith")],
            ["bob jones"]);

        result.Should().BeEquivalentTo(["Bob Jones"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_InconsistentCasePattern_ReturnsNull()
    {
        // "alice" → "ALICE" suggests UPPER, but "bob" → "Bob" suggests PROPER
        var result = FlashFillService.Fill(
            [("alice", "ALICE"), ("bob", "Bob")],
            ["carol"]);

        result.Should().BeNull();
    }

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
    public void Fill_ExtractSemicolonDelimitedToken_ReturnsNullWhenRemainingDelimiterIsMissing()
    {
        var result = FlashFillService.Fill(
            [("SKU-001;Retail;West", "Retail"), ("SKU-002;Wholesale;East", "Wholesale")],
            ["SKU-003 Online North"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("North (Retail)", "Retail", "South (Wholesale)", "Wholesale", "East (Online)", "Online")]
    [InlineData("INV [Open]", "Open", "INV [Closed]", "Closed", "INV [Pending]", "Pending")]
    [InlineData("Batch {Ready}", "Ready", "Batch {Held}", "Held", "Batch {Review}", "Review")]
    [InlineData("North \"Retail\"", "Retail", "South \"Wholesale\"", "Wholesale", "East \"Online\"", "Online")]
    [InlineData("North 'Retail'", "Retail", "South 'Wholesale'", "Wholesale", "East 'Online'", "Online")]
    [InlineData("Dept <Retail>", "Retail", "Dept <Wholesale>", "Wholesale", "Dept <Online>", "Online")]
    public void Fill_PairedDelimiterExtraction_ExtractsTextBetweenMatchingDelimiters(
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
    public void Fill_PairedDelimiterExtraction_ReturnsNullWhenRemainingDelimiterIsMissing()
    {
        var result = FlashFillService.Fill(
            [("North (Retail)", "Retail"), ("South (Wholesale)", "Wholesale")],
            ["East Online"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("North (Retail)", "North", "South (Wholesale)", "South", "East (Online)", "East")]
    [InlineData("INV [Open]", "INV", "REQ [Closed]", "REQ", "PO [Pending]", "PO")]
    [InlineData("Dept <Retail>", "Dept", "Team <Wholesale>", "Team", "Channel <Online>", "Channel")]
    public void Fill_PairedDelimiterRemoval_RemovesDelimitedQualifier(
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
    public void Fill_PairedDelimiterRemoval_ReturnsNullWhenRemainingDelimiterIsMissing()
    {
        var result = FlashFillService.Fill(
            [("North (Retail)", "North"), ("South (Wholesale)", "South")],
            ["East Online"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("Status: Open", "Open", "Status: Closed", "Closed", "Status: Pending", "Pending")]
    [InlineData("Priority = High", "High", "Priority = Low", "Low", "Priority = Medium", "Medium")]
    [InlineData("Owner - Ada", "Ada", "Owner - Grace", "Grace", "Owner - Alan", "Alan")]
    [InlineData("Owner-Ada", "Ada", "Owner-Grace", "Grace", "Owner-Alan", "Alan")]
    [InlineData("Status / Open", "Open", "Status / Closed", "Closed", "Status / Pending", "Pending")]
    [InlineData("Status/Open", "Open", "Status/Closed", "Closed", "Status/Pending", "Pending")]
    [InlineData("Status | Open", "Open", "Status | Closed", "Closed", "Status | Pending", "Pending")]
    [InlineData("Status|Open", "Open", "Status|Closed", "Closed", "Status|Pending", "Pending")]
    [InlineData("Status -> Open", "Open", "Status -> Closed", "Closed", "Status -> Pending", "Pending")]
    [InlineData("Status->Open", "Open", "Status->Closed", "Closed", "Status->Pending", "Pending")]
    [InlineData("Status => Open", "Open", "Status => Closed", "Closed", "Status => Pending", "Pending")]
    [InlineData("Status=>Open", "Open", "Status=>Closed", "Closed", "Status=>Pending", "Pending")]
    public void Fill_LabelValueExtraction_ExtractsTrimmedValueAfterSeparator(
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
    [InlineData("Owner  -   Ada", "Ada", "Owner\t-\tGrace", "Grace", "Owner - Alan", "Alan")]
    [InlineData("Status  /   Open", "Open", "Status\t/\tClosed", "Closed", "Status / Pending", "Pending")]
    [InlineData("Status  |   Open", "Open", "Status\t|\tClosed", "Closed", "Status | Pending", "Pending")]
    [InlineData("Status  ->   Open", "Open", "Status\t->\tClosed", "Closed", "Status -> Pending", "Pending")]
    [InlineData("Status  =>   Open", "Open", "Status\t=>\tClosed", "Closed", "Status => Pending", "Pending")]
    public void Fill_LabelValueExtraction_ToleratesUnevenSeparatorWhitespace(
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
    public void Fill_LabelValueExtraction_ReturnsNullWhenRemainingSeparatorIsMissing()
    {
        var result = FlashFillService.Fill(
            [("Status: Open", "Open"), ("Status: Closed", "Closed")],
            ["Status Pending"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("Status / Open", "Open", "Status / Closed", "Closed")]
    [InlineData("Status | Open", "Open", "Status | Closed", "Closed")]
    [InlineData("Status -> Open", "Open", "Status -> Closed", "Closed")]
    public void Fill_LabelValueExtraction_ReturnsNullWhenSlashPipeOrArrowSeparatorIsMissing(
        string source1,
        string expected1,
        string source2,
        string expected2)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            ["Status Pending"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("Status: Open", "Status", "Priority: High", "Priority", "Owner: Ada", "Owner")]
    [InlineData("Status = Open", "Status", "Priority = High", "Priority", "Owner = Ada", "Owner")]
    [InlineData("Status - Open", "Status", "Priority - High", "Priority", "Owner - Ada", "Owner")]
    [InlineData("Status-Open", "Status", "Priority-High", "Priority", "Owner-Ada", "Owner")]
    [InlineData("Status / Open", "Status", "Priority / High", "Priority", "Owner / Ada", "Owner")]
    [InlineData("Status/Open", "Status", "Priority/High", "Priority", "Owner/Ada", "Owner")]
    [InlineData("Status | Open", "Status", "Priority | High", "Priority", "Owner | Ada", "Owner")]
    [InlineData("Status|Open", "Status", "Priority|High", "Priority", "Owner|Ada", "Owner")]
    [InlineData("Status -> Open", "Status", "Priority -> High", "Priority", "Owner -> Ada", "Owner")]
    [InlineData("Status->Open", "Status", "Priority->High", "Priority", "Owner->Ada", "Owner")]
    [InlineData("Status => Open", "Status", "Priority => High", "Priority", "Owner => Ada", "Owner")]
    [InlineData("Status=>Open", "Status", "Priority=>High", "Priority", "Owner=>Ada", "Owner")]
    public void Fill_LabelQualifierRemoval_RemovesValueAfterSeparator(
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
    [InlineData("Status  -   Open", "Status", "Priority\t-\tHigh", "Priority", "Owner - Ada", "Owner")]
    [InlineData("Status  /   Open", "Status", "Priority\t/\tHigh", "Priority", "Owner / Ada", "Owner")]
    [InlineData("Status  |   Open", "Status", "Priority\t|\tHigh", "Priority", "Owner | Ada", "Owner")]
    [InlineData("Status  ->   Open", "Status", "Priority\t->\tHigh", "Priority", "Owner -> Ada", "Owner")]
    [InlineData("Status  =>   Open", "Status", "Priority\t=>\tHigh", "Priority", "Owner => Ada", "Owner")]
    public void Fill_LabelQualifierRemoval_ToleratesUnevenSeparatorWhitespace(
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
    public void Fill_LabelQualifierRemoval_ReturnsNullWhenRemainingSeparatorIsMissing()
    {
        var result = FlashFillService.Fill(
            [("Status: Open", "Status"), ("Priority: High", "Priority")],
            ["Owner Ada"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("Status / Open", "Status", "Priority / High", "Priority")]
    [InlineData("Status | Open", "Status", "Priority | High", "Priority")]
    [InlineData("Status -> Open", "Status", "Priority -> High", "Priority")]
    public void Fill_LabelQualifierRemoval_ReturnsNullWhenSlashPipeOrArrowSeparatorIsMissing(
        string source1,
        string expected1,
        string source2,
        string expected2)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            ["Owner Ada"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmailDisplayName_ConvertsDottedUserNameToProperName()
    {
        var result = FlashFillService.Fill(
            [
                ("ada.lovelace@contoso.com", "Ada Lovelace"),
                ("grace.hopper@contoso.com", "Grace Hopper")
            ],
            ["alan.turing@contoso.com"]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDisplayName_ConvertsUnderscoreUserNameToProperName()
    {
        var result = FlashFillService.Fill(
            [
                ("ada_lovelace@contoso.com", "Ada Lovelace"),
                ("grace_hopper@contoso.com", "Grace Hopper")
            ],
            ["alan_turing@contoso.com"]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDisplayName_ConvertsHyphenUserNameToProperName()
    {
        var result = FlashFillService.Fill(
            [
                ("ada-lovelace@contoso.com", "Ada Lovelace"),
                ("grace-hopper@contoso.com", "Grace Hopper")
            ],
            ["alan-turing@contoso.com"]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDisplayName_IgnoresPlusAddressTags()
    {
        var result = FlashFillService.Fill(
            [
                ("ada.lovelace+analytics@contoso.com", "Ada Lovelace"),
                ("grace.hopper+navy@contoso.com", "Grace Hopper")
            ],
            ["alan.turing+math@contoso.com"]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DelimitedWordsInitials_BuildsInitials()
    {
        var result = FlashFillService.Fill(
            [("Ada Lovelace", "AL"), ("Grace Hopper", "GH")],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo(["AT"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNameLastCommaFirst_ReordersDelimitedNameParts()
    {
        var result = FlashFillService.Fill(
            [("Ada Lovelace", "Lovelace, Ada"), ("Grace Hopper", "Hopper, Grace")],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo(["Turing, Alan"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_LastCommaFirstFullName_ReordersDelimitedNameParts()
    {
        var result = FlashFillService.Fill(
            [("Lovelace, Ada", "Ada Lovelace"), ("Hopper, Grace", "Grace Hopper")],
            ["Turing, Alan"]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_LastCommaFirstFullName_ReturnsNullForMalformedRemainingSource()
    {
        var result = FlashFillService.Fill(
            [("Lovelace, Ada", "Ada Lovelace"), ("Hopper, Grace", "Grace Hopper")],
            ["Alan Turing"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_ThreePartNames_ExtractsFirstAndLast()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Ada Lovelace"),
                ("Grace Brewster Hopper", "Grace Hopper")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_ReordersLastCommaFirst()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Lovelace, Ada"),
                ("Grace Brewster Hopper", "Hopper, Grace")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Turing, Alan"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_ReordersLastCommaFirstMiddle()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Lovelace, Ada Byron"),
                ("Grace Brewster Hopper", "Hopper, Grace Brewster")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Turing, Alan Mathison"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNames_AbbreviatesFirstInitialLastName()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", "A. Lovelace"),
                ("Grace Hopper", "G. Hopper")
            ],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo(["A. Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNames_AbbreviatesFirstNameLastInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", "Ada L."),
                ("Grace Hopper", "Grace H.")
            ],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo(["Alan T."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNames_AbbreviatesAllInitials()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", "A. L."),
                ("Grace Hopper", "G. H.")
            ],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo(["A. T."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNames_AbbreviatesLastNameFirstInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", "Lovelace A."),
                ("Grace Hopper", "Hopper G.")
            ],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo(["Turing A."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNames_AbbreviatesLastCommaFirstInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", "Lovelace, A."),
                ("Grace Hopper", "Hopper, G.")
            ],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo(["Turing, A."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesMiddleInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Ada B. Lovelace"),
                ("Grace Brewster Hopper", "Grace B. Hopper")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Alan M. Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesFirstInitialLastName()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "A. Lovelace"),
                ("Grace Brewster Hopper", "G. Hopper")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["A. Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesFirstAndMiddleInitials()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "A. B. Lovelace"),
                ("Grace Brewster Hopper", "G. B. Hopper")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["A. M. Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesAllInitials()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "A. B. L."),
                ("Grace Brewster Hopper", "G. B. H.")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["A. M. T."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesFirstNameMiddleInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Ada B."),
                ("Grace Brewster Hopper", "Grace B.")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Alan M."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesLastCommaFirstAndMiddleInitials()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Lovelace, A. B."),
                ("Grace Brewster Hopper", "Hopper, G. B.")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Turing, A. M."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesLastCommaFirstNameMiddleInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Lovelace, Ada B."),
                ("Grace Brewster Hopper", "Hopper, Grace B.")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Turing, Alan M."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesLastInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Ada Byron L."),
                ("Grace Brewster Hopper", "Grace Brewster H.")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Alan Mathison T."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesLastFirstAndMiddleInitials()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Lovelace A. B."),
                ("Grace Brewster Hopper", "Hopper G. B.")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Turing A. M."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_ExtractsMiddleInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "B."),
                ("Grace Murray Hopper", "M.")
            ],
            ["Katherine Coleman Johnson"]);

        result.Should().BeEquivalentTo(["C."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_ReturnsNullForAmbiguousTokenCounts()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Ada Lovelace"),
                ("Grace Brewster Hopper", "Grace Hopper")
            ],
            ["Alan Turing"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_DigitMask_FormatsPhoneNumberByExample()
    {
        var result = FlashFillService.Fill(
            [("4255550101", "(425) 555-0101"), ("2065550199", "(206) 555-0199")],
            ["3605550142"]);

        result.Should().BeEquivalentTo(["(360) 555-0142"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstInitialPeriodLast_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["A. Lovelace", "G. Hopper"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["A. Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstInitialLastLowercase_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["alovelace", "ghopper"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["aturing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastEmail_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["ada.lovelace@example.com", "grace.hopper@example.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["alan.turing@example.com"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastEmail_LearnsConstantDomainFromExamples()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["ada.lovelace@contoso.com", "grace.hopper@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["alan.turing@contoso.com"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("_", "alan_turing@contoso.com")]
    [InlineData("-", "alan-turing@contoso.com")]
    public void FillFromColumns_FirstLastSeparatedEmail_LearnsSeparatorAndConstantDomain(
        string separator,
        string expected)
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            [$"ada{separator}lovelace@contoso.com", $"grace{separator}hopper@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo([expected], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstInitialLastEmail_LearnsConstantDomainFromExamples()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["alovelace@contoso.com", "ghopper@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["aturing@contoso.com"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(".", "a.turing@contoso.com")]
    [InlineData("_", "a_turing@contoso.com")]
    [InlineData("-", "a-turing@contoso.com")]
    public void FillFromColumns_FirstInitialLastSeparatedEmail_LearnsSeparatorAndConstantDomain(
        string separator,
        string expected)
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            [$"a{separator}lovelace@contoso.com", $"g{separator}hopper@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo([expected], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_LastFirstInitialEmail_LearnsConstantDomainFromExamples()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["lovelacea@contoso.com", "hopperg@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["turinga@contoso.com"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(".", "turing.a@contoso.com")]
    [InlineData("_", "turing_a@contoso.com")]
    [InlineData("-", "turing-a@contoso.com")]
    public void FillFromColumns_LastFirstInitialSeparatedEmail_LearnsSeparatorAndConstantDomain(
        string separator,
        string expected)
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            [$"lovelace{separator}a@contoso.com", $"hopper{separator}g@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo([expected], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastEmail_ReturnsNullWhenExampleDomainsDiffer()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["ada.lovelace@contoso.com", "grace.hopper@example.org"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeNull();
    }

    [Fact]
    public void FillFromColumns_FirstInitialLastEmail_ReturnsNullWhenExampleDomainsDiffer()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["alovelace@contoso.com", "ghopper@example.org"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeNull();
    }

    [Fact]
    public void FillFromColumns_LastFirstInitialEmail_ReturnsNullWhenExampleDomainsDiffer()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["lovelacea@contoso.com", "hopperg@example.org"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeNull();
    }

    [Fact]
    public void FillFromColumns_LastFirstInitialPeriod_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["Lovelace A.", "Hopper G."],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["Turing A."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DelimitedWordsInitials_WithMixedExampleDelimiters_ReturnsNull()
    {
        var result = FlashFillService.Fill(
            [("Ada Lovelace", "AL"), ("Grace-Hopper", "GH")],
            ["Alan Turing"]);

        result.Should().BeNull();
    }

    [Fact]
    public void FillFromColumns_FirstLastWithSpace_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["Ada Lovelace", "Grace Hopper"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_LastFirstWithComma_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["Lovelace, Ada", "Hopper, Grace"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["Turing, Alan"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastWithPeriod_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["ada", "lovelace"],
                ["grace", "hopper"]
            ],
            ["ada.lovelace", "grace.hopper"],
            [
                ["alan", "turing"]
            ]);

        result.Should().BeEquivalentTo(["alan.turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastWithPeriodLowercase_NormalizesProperCaseNames()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["ada.lovelace", "grace.hopper"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["alan.turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastInitials_BuildsInitials()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["AL", "GH"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["AT"], o => o.WithStrictOrdering());
    }

    // ── Prefix / suffix trim ──────────────────────────────────────────────────

    [Fact]
    public void Fill_RemovePrefix_TrimsFixedPrefixFromSource()
    {
        var result = FlashFillService.Fill(
            [("Mr. Smith", "Smith"), ("Mr. Jones", "Jones")],
            ["Mr. Brown"]);

        result.Should().BeEquivalentTo(["Brown"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_AddPrefix_PrependsPrefixToSource()
    {
        var result = FlashFillService.Fill(
            [("Smith", "Mr. Smith"), ("Jones", "Mr. Jones")],
            ["Brown"]);

        result.Should().BeEquivalentTo(["Mr. Brown"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_AddSuffix_AppendsSuffixToSource()
    {
        var result = FlashFillService.Fill(
            [("Smith", "Smith Ltd"), ("Jones", "Jones Ltd")],
            ["Brown"]);

        result.Should().BeEquivalentTo(["Brown Ltd"], o => o.WithStrictOrdering());
    }

    // ── Substring extraction ──────────────────────────────────────────────────

    [Fact]
    public void Fill_SubstringExtraction_AppliesConsistentStartAndLength()
    {
        // "ABCDE" → "BCD" means substring(1, 3)
        // "FGHIJ" → "GHI" means substring(1, 3) — same pattern
        var result = FlashFillService.Fill(
            [("ABCDE", "BCD"), ("FGHIJ", "GHI")],
            ["KLMNO"]);

        result.Should().BeEquivalentTo(["LMN"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_NoExamples_ReturnsNull()
    {
        var result = FlashFillService.Fill([], ["Bob"]);
        result.Should().BeNull();
    }

    [Fact]
    public void Fill_SubstringPatternSourceTooShort_ReturnsNull()
    {
        // Pattern: startIndex=1, length=3 — but "AB" is only 2 chars
        var result = FlashFillService.Fill(
            [("ABCDE", "BCD"), ("FGHIJ", "GHI")],
            ["AB"]);
        result.Should().BeNull();
    }

    // ── No pattern ────────────────────────────────────────────────────────────

    [Fact]
    public void Fill_NoPattern_ReturnsNull()
    {
        var result = FlashFillService.Fill(
            [("Alice", "hello"), ("Bob", "world")],
            ["Carol"]);

        result.Should().BeNull();
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Fill_SingleExample_StillDetectsPattern()
    {
        // With one example we should still detect UPPER
        var result = FlashFillService.Fill(
            [("alice", "ALICE")],
            ["bob", "carol"]);

        result.Should().BeEquivalentTo(["BOB", "CAROL"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmptyRemaining_ReturnsEmptyList()
    {
        var result = FlashFillService.Fill(
            [("alice", "ALICE")],
            []);

        result.Should().NotBeNull();
        result!.Should().BeEmpty();
    }

    [Fact]
    public void Fill_SuffixTrimPattern_TrimsFixedSuffixFromSource()
    {
        var result = FlashFillService.Fill(
            [("Smith Ltd", "Smith"), ("Jones Ltd", "Jones")],
            ["Brown Ltd"]);

        result.Should().BeEquivalentTo(["Brown"], o => o.WithStrictOrdering());
    }
}

public sealed class FlashFillCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    [Fact]
    public void FlashFillCommand_Apply_FillsBlankCellsUsingDetectedPattern()
    {
        var (wb, sheet, ctx) = Setup();
        // Col A = source data (col index 1)
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("John Smith"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jane Doe"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Bob Brown"));
        // Col B = fill column (col index 2): user typed example in row 1
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("John"));
        // Rows 2 and 3 in col B are blank

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 2, sourceColIndex: 1, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        (sheet.GetValue(2, 2) as TextValue)?.Value.Should().Be("Jane");
        (sheet.GetValue(3, 2) as TextValue)?.Value.Should().Be("Bob");
    }

    [Fact]
    public void FlashFillCommand_Revert_RestoresBlankCells()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("John Smith"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jane Doe"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Bob Brown"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("John"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 2, sourceColIndex: 1, startRow: 1, endRow: 3);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        // B2 and B3 should be blank again
        sheet.GetValue(2, 2).Should().BeOfType<BlankValue>();
        sheet.GetValue(3, 2).Should().BeOfType<BlankValue>();
    }

    [Fact]
    public void FlashFillCommand_NoPattern_ReturnsFailureOutcome()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Alice"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Bob"));
        // Examples that have no consistent pattern
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("world"));
        // Row 3 is blank (the only row to fill) — but two examples with no pattern

        // Put something to fill
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Carol"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 2, sourceColIndex: 1, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Command_SourceColumnOnRight_FillsCorrectly()
    {
        var wb    = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx   = new SimpleCtx(wb);

        // Source data in col 2: "ALICE", "BOB", "CAROL"
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("ALICE"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("BOB"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("CAROL"));

        // Example in col 1 row 1: "alice" (LOWER pattern)
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("alice"));

        // Fill col=1, source col=2
        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 1, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("bob"));
        sheet.GetCell(3, 1)!.Value.Should().Be(new TextValue("carol"));
    }

    [Fact]
    public void FlashFillCommand_WhenTwoLeftColumnsArePopulated_UsesMultiColumnPatternFirst()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Ada Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Grace Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Alan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Turing"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("Alan Turing"));
    }

    [Fact]
    public void FlashFillCommand_WithFirstLastEmailExamples_UsesInferredDomain()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("ada.lovelace@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("grace.hopper@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Alan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Turing"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("alan.turing@contoso.com"));
    }

    [Fact]
    public void FlashFillCommand_WithFirstInitialLastEmailExamples_UsesInferredDomain()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("alovelace@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("ghopper@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Alan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Turing"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("aturing@contoso.com"));
    }

    [Fact]
    public void FlashFillCommand_WithLastFirstInitialEmailExamples_UsesInferredDomain()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("lovelacea@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("hopperg@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Alan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Turing"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("turinga@contoso.com"));
    }

    [Fact]
    public void FlashFillCommand_WhenSourceColumnIsNotImmediateLeft_UsesSingleSourcePattern()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Ada Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("ADA LOVELACE"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Grace Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("GRACE HOPPER"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Wrong"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new TextValue("ALAN TURING"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 4, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("Alan Turing"));
    }

    [Fact]
    public void Fill_StripThousandSeparators_RemovesCommasFromNumbers()
    {
        var result = FlashFillService.Fill(
            [("1,234", "1234"), ("5,678", "5678")],
            ["9,000", "12,345"]);

        result.Should().BeEquivalentTo(["9000", "12345"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_StripThousandSeparators_HandlesMultipleGroupSeparators()
    {
        var result = FlashFillService.Fill(
            [("1,234,567", "1234567"), ("9,000,001", "9000001")],
            ["2,500,000"]);

        result.Should().BeEquivalentTo(["2500000"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_StripThousandSeparators_HandlesMixedDecimalAndGrouping()
    {
        var result = FlashFillService.Fill(
            [("1,234.56", "1234.56"), ("9,000.00", "9000.00")],
            ["2,500.75"]);

        result.Should().BeEquivalentTo(["2500.75"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractDigitsOnly_StripsAllNonDigitCharacters()
    {
        var result = FlashFillService.Fill(
            [("(555) 867-5309", "5558675309"), ("(800) 555-0100", "8005550100")],
            ["(212) 555-1234"]);

        result.Should().BeEquivalentTo(["2125551234"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractDigitsOnly_WorksWithDashSeparatedFormats()
    {
        var result = FlashFillService.Fill(
            [("123-45-6789", "123456789"), ("987-65-4321", "987654321")],
            ["555-12-3456"]);

        result.Should().BeEquivalentTo(["555123456"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractDigitsOnly_ReturnsNullWhenSourceHasNoDigits()
    {
        var result = FlashFillService.Fill(
            [("(555) 867-5309", "5558675309"), ("(800) 555-0100", "8005550100")],
            ["no digits here"]);

        result.Should().BeNull();
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
