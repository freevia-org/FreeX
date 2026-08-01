using System.IO.Compression;
using System.Text;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class ExternalRichTextClipboardTests
{
    [Fact]
    public void XamlPackageFlowDocument_PreservesCommonParagraphAndInlineFormatting()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Paragraph TextAlignment="Center" Margin="12,4,0,8">
                <Run FontFamily="Arial" FontSize="16" FontWeight="Bold" Foreground="#FF0080C0" Text="Title" />
                <Span FontStyle="Italic"><Run Text=" and detail" /></Span>
                <LineBreak />
                <Underline>underlined</Underline>
              </Paragraph>
            </FlowDocument>
            """;

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Title and detail\nunderlined");
        payload.Body.Paragraphs.Should().ContainSingle();
        var paragraph = payload.Body.Paragraphs.Single();
        paragraph.Align.Should().Be(TextAlign.Center);
        paragraph.MarginLeftEmu.Should().Be(114300);
        paragraph.SpaceBeforePt.Should().Be(3);
        paragraph.SpaceAfterPt.Should().Be(6);
        paragraph.Runs[0].FontFamily.Should().Be("Arial");
        paragraph.Runs[0].FontSizePt.Should().Be(12);
        paragraph.Runs[0].Bold.Should().BeTrue();
        paragraph.Runs[0].Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x0080C0));
        paragraph.Runs[1].Italic.Should().BeTrue();
        paragraph.Runs[2].Text.Should().Be("\n");
        paragraph.Runs[3].Underline.Should().BeTrue();
    }

    [Fact]
    public void XamlPackageFlowDocument_PreservesAuthoredInlineWhitespace_AndIgnoresIndentation()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Paragraph>
                <Run Text="left" />
                <Run Text=" " />
                <Bold xml:space="preserve"> right </Bold>
              </Paragraph>
            </FlowDocument>
            """;

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("left  right ");
        payload.Body.Paragraphs.Single().Runs.Select(run => run.Text)
            .Should().Equal("left", " ", " right ");
        payload.Body.Paragraphs.Single().Runs[2].Bold.Should().BeTrue();
    }

    [Fact]
    public void XamlPackageFlowDocument_PreservesBaselineAlignmentAndStyleInheritance()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <FlowDocument.Resources>
                <ResourceDictionary>
                  <Style x:Key="ScriptText">
                    <Setter Property="BaselineAlignment" Value="Superscript" />
                  </Style>
                </ResourceDictionary>
              </FlowDocument.Resources>
              <Paragraph>
                <Run Text="base" />
                <Run BaselineAlignment="Superscript" Text="up" />
                <Span BaselineAlignment="Subscript">down</Span>
                <Run BaselineAlignment="Baseline" Text="normal" />
              </Paragraph>
              <Paragraph Style="{StaticResource ScriptText}">styled up</Paragraph>
            </FlowDocument>
            """;

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml));

        payload.Should().NotBeNull();
        var paragraphs = payload!.Body.Paragraphs;
        paragraphs[0].Runs.Select(run => run.BaselineOffset)
            .Should().Equal(null, 10_000, -10_000, null);
        paragraphs[1].Runs.Single().BaselineOffset.Should().Be(10_000);
    }

    [Fact]
    public void XamlPackageFlowDocument_PreservesFlowDirectionInheritanceAndOverrides()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          FlowDirection="RightToLeft">
              <Paragraph>
                <Run Text="אבג" />
                <Run FlowDirection="LeftToRight" Text="LTR" />
              </Paragraph>
              <Paragraph FlowDirection="LeftToRight">plain direction</Paragraph>
            </FlowDocument>
            """;

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml));

        payload.Should().NotBeNull();
        var paragraphs = payload!.Body.Paragraphs;
        paragraphs.Select(paragraph => paragraph.RightToLeft)
            .Should().Equal(true, false);
        paragraphs[0].Runs.Select(run => run.RightToLeft)
            .Should().Equal(true, false);
        paragraphs[1].Runs.Single().RightToLeft.Should().BeFalse();
    }

    [Fact]
    public void XamlPackageFlowDocument_PreservesTextAlignmentInheritanceAndOverrides()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                          TextAlignment="Center">
              <FlowDocument.Resources>
                <ResourceDictionary>
                  <Style x:Key="JustifiedText">
                    <Setter Property="TextAlignment" Value="Justify" />
                  </Style>
                </ResourceDictionary>
              </FlowDocument.Resources>
              <Paragraph>inherited center</Paragraph>
              <Paragraph TextAlignment="Right">direct right</Paragraph>
              <Paragraph Style="{StaticResource JustifiedText}">styled justify</Paragraph>
            </FlowDocument>
            """;

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml));

        payload.Should().NotBeNull();
        payload!.Body.Paragraphs.Select(paragraph => paragraph.Align)
            .Should().Equal(TextAlign.Center, TextAlign.Right, TextAlign.Justify);
    }

    [Fact]
    public void XamlPackageFlowDocument_ResolvesSolidColorBrushResources()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <FlowDocument.Resources>
                <ResourceDictionary>
                  <SolidColorBrush x:Key="AccentBrush" Color="#FF2F5597" />
                </ResourceDictionary>
              </FlowDocument.Resources>
              <Paragraph Foreground="{StaticResource AccentBrush}">Resource colored</Paragraph>
            </FlowDocument>
            """;

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml));

        payload.Should().NotBeNull();
        payload!.Body.Paragraphs.Single().Runs.Single().Color!.Resolved
            .Should().Be(SrgbColor.FromRgb(0x2F5597));
    }

    [Fact]
    public void XamlPackageFlowDocument_ResolvesFontFamilyAndSizeResources()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                          xmlns:sys="clr-namespace:System;assembly=mscorlib">
              <FlowDocument.Resources>
                <ResourceDictionary>
                  <FontFamily x:Key="BodyFont">Aptos</FontFamily>
                  <sys:Double x:Key="BodySize">18</sys:Double>
                </ResourceDictionary>
              </FlowDocument.Resources>
              <Paragraph FontFamily="{DynamicResource BodyFont}"
                         FontSize="{StaticResource BodySize}">Resource typography</Paragraph>
            </FlowDocument>
            """;

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml));

        payload.Should().NotBeNull();
        var run = payload!.Body.Paragraphs.Single().Runs.Single();
        run.FontFamily.Should().Be("Aptos");
        run.FontSizePt.Should().Be(13.5);
    }

    [Fact]
    public void XamlPackageFlowDocument_AppliesTextSettersFromReferencedStyleResources()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                          xmlns:sys="clr-namespace:System;assembly=mscorlib">
              <FlowDocument.Resources>
                <ResourceDictionary>
                  <SolidColorBrush x:Key="BodyBrush" Color="#FF1F4E79" />
                  <FontFamily x:Key="BodyFont">Aptos</FontFamily>
                  <sys:Double x:Key="BodySize">16</sys:Double>
                  <Style x:Key="BodyText">
                    <Setter Property="Foreground" Value="{StaticResource BodyBrush}" />
                    <Setter Property="FontFamily" Value="{DynamicResource BodyFont}" />
                    <Setter Property="FontSize" Value="{StaticResource BodySize}" />
                    <Setter Property="FontWeight" Value="Bold" />
                    <Setter Property="TextDecorations" Value="Underline" />
                  </Style>
                </ResourceDictionary>
              </FlowDocument.Resources>
              <Paragraph Style="{StaticResource BodyText}">Styled paragraph</Paragraph>
            </FlowDocument>
            """;

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml));

        payload.Should().NotBeNull();
        var run = payload!.Body.Paragraphs.Single().Runs.Single();
        run.FontFamily.Should().Be("Aptos");
        run.FontSizePt.Should().Be(12);
        run.Bold.Should().BeTrue();
        run.Underline.Should().BeTrue();
        run.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
    }

    [Fact]
    public void XamlPackageFlowDocument_ResolvesBasedOnStyleChainsWithoutLooping()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <FlowDocument.Resources>
                <ResourceDictionary>
                  <Style x:Key="BaseText">
                    <Setter Property="FontFamily" Value="Aptos" />
                    <Setter Property="FontSize" Value="14" />
                  </Style>
                  <Style x:Key="HeadingText" BasedOn="{StaticResource BaseText}">
                    <Setter Property="FontWeight" Value="Bold" />
                  </Style>
                  <Style x:Key="LoopA" BasedOn="{StaticResource LoopB}" />
                  <Style x:Key="LoopB" BasedOn="{StaticResource LoopA}" />
                </ResourceDictionary>
              </FlowDocument.Resources>
              <Paragraph Style="{StaticResource HeadingText}">Heading</Paragraph>
              <Paragraph Style="{StaticResource LoopA}">Loop remains safe</Paragraph>
            </FlowDocument>
            """;

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml));

        payload.Should().NotBeNull();
        var paragraphs = payload!.Body.Paragraphs;
        paragraphs.Should().HaveCount(2);
        paragraphs[0].Runs.Single().FontFamily.Should().Be("Aptos");
        paragraphs[0].Runs.Single().FontSizePt.Should().Be(10.5);
        paragraphs[0].Runs.Single().Bold.Should().BeTrue();
        paragraphs[1].Runs.Single().Text.Should().Be("Loop remains safe");
    }

    [Fact]
    public void XamlPackageFlowDocument_PreservesAllowedHyperlinks_AndBlocksUnsafeSchemes()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Paragraph>
                <Hyperlink NavigateUri="https://example.test/docs" ToolTip="Open docs">Docs</Hyperlink>
                <Hyperlink NavigateUri="javascript:alert(1)">Unsafe</Hyperlink>
                <Run NavigateUri="mailto:help@example.test" Text="Mail" />
              </Paragraph>
            </FlowDocument>
            """;

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml));

        payload.Should().NotBeNull();
        var runs = payload!.Body.Paragraphs.Single().Runs;
        runs[0].Text.Should().Be("Docs");
        runs[0].Hyperlink.Should().NotBeNull();
        runs[0].Hyperlink!.Url.Should().Be("https://example.test/docs");
        runs[0].Hyperlink!.Tooltip.Should().Be("Open docs");
        runs[1].Text.Should().Be("Unsafe");
        runs[1].Hyperlink.Should().BeNull();
        runs[2].Text.Should().Be("Mail");
        runs[2].Hyperlink!.Url.Should().Be("mailto:help@example.test");
    }

    [Fact]
    public void XamlPackageFlowDocument_PreservesListMarkerStylesAndNestedLevels()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <List MarkerStyle="Decimal" StartIndex="3">
                <ListItem><Paragraph>Three</Paragraph></ListItem>
                <ListItem>
                  <Paragraph>Four</Paragraph>
                  <List MarkerStyle="LowerLatin"><ListItem><Paragraph>Nested</Paragraph></ListItem></List>
                </ListItem>
              </List>
              <List MarkerStyle="Circle"><ListItem><Paragraph>Circle</Paragraph></ListItem></List>
            </FlowDocument>
            """;

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml));

        payload.Should().NotBeNull();
        var paragraphs = payload!.Body.Paragraphs;
        paragraphs.Should().HaveCount(4);
        paragraphs[0].BulletKind.Should().Be(BulletKind.Auto);
        paragraphs[0].AutoNumType.Should().Be(AutoNumType.ArabicPeriod);
        paragraphs[0].AutoNumStartAt.Should().Be(3);
        paragraphs[0].AutoNumStartAtSpecified.Should().BeTrue();
        paragraphs[1].BulletKind.Should().Be(BulletKind.Auto);
        paragraphs[1].AutoNumStartAtSpecified.Should().BeFalse();
        paragraphs[2].Level.Should().Be(1);
        paragraphs[2].BulletKind.Should().Be(BulletKind.Auto);
        paragraphs[2].AutoNumType.Should().Be(AutoNumType.AlphaLcPeriod);
        paragraphs[3].BulletKind.Should().Be(BulletKind.Char);
        paragraphs[3].BulletChar.Should().Be("\u25E6");
    }

    [Fact]
    public void XamlPackageFlowDocument_FlattensTablesLikeWpfProjection_AndPreservesCellFormatting()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Paragraph>Before</Paragraph>
              <Table>
                <TableRowGroup>
                  <TableRow>
                    <TableCell><Paragraph><Bold>Header</Bold></Paragraph></TableCell>
                    <TableCell><Paragraph><Italic>Value</Italic></Paragraph></TableCell>
                  </TableRow>
                  <TableRow>
                    <TableCell><Paragraph>Left</Paragraph></TableCell>
                    <TableCell><Paragraph><Underline>Right</Underline></Paragraph></TableCell>
                  </TableRow>
                </TableRowGroup>
              </Table>
              <Paragraph>After</Paragraph>
            </FlowDocument>
            """;

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Before\nHeader\tValue\nLeft\tRight\nAfter");
        payload.Body.Paragraphs.Should().HaveCount(4);
        payload.Body.Paragraphs[1].Runs.Should().Contain(run => run.Text == "Header" && run.Bold);
        payload.Body.Paragraphs[1].Runs.Should().Contain(run => run.Text == "Value" && run.Italic);
        payload.Body.Paragraphs[2].Runs.Should().Contain(run => run.Text == "Right" && run.Underline);
    }

    [Fact]
    public void XamlPackageFlowDocument_PreservesAllImagePayloadsInDocumentOrder()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <BlockUIContainer><Image Source="Images/first.png" Width="96" Height="48" /></BlockUIContainer>
              <BlockUIContainer><Image Source="Images/second.jpg" /></BlockUIContainer>
            </FlowDocument>
            """;
        var first = new byte[] { 0x01, 0x02 };
        var second = new byte[] { 0x03, 0x04, 0x05 };

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml,
                ("Images/first.png", first),
                ("Images/second.jpg", second)));

        payload.Should().NotBeNull();
        payload!.GetImagePayloads().Should().HaveCount(2);
        payload.GetImagePayloads()[0].Bytes.Should().Equal(first);
        payload.GetImagePayloads()[0].ContentType.Should().Be("image/png");
        payload.GetImagePayloads()[0].WidthEmu.Should().Be(914400);
        payload.GetImagePayloads()[0].HeightEmu.Should().Be(457200);
        payload.GetImagePayloads()[1].Bytes.Should().Equal(second);
        payload.GetImagePayloads()[1].ContentType.Should().Be("image/jpeg");
        payload.GetImagePayloads()[1].WidthEmu.Should().BeNull();
        payload.GetImagePayloads()[1].HeightEmu.Should().BeNull();
        payload.ImageBytes.Should().Equal(first);
        payload.ImageContentType.Should().Be("image/png");
    }

    [Fact]
    public void XamlPackageFlowDocument_PreservesInlineImageRunOrderAndExtent()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Paragraph><Run Text="Before"/><Image Source="Images/inline.png" Width="24" Height="12"/><Run Text="After"/></Paragraph>
            </FlowDocument>
            """;
        var bytes = new byte[] { 0x01, 0x02, 0x03 };

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml, ("Images/inline.png", bytes)));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Before\uFFFCAfter");
        payload.Body.Paragraphs.Single().Runs.Select(run => run.Text)
            .Should().Equal("Before", "\uFFFC", "After");
        var inline = payload.Body.Paragraphs.Single().Runs[1];
        inline.InlineImage!.Bytes.Should().Equal(bytes);
        inline.InlineImageWidthEmu.Should().Be(228_600);
        inline.InlineImageHeightEmu.Should().Be(114_300);

        var reopened = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));
        reopened!.Body.Paragraphs.Single().Runs[1].InlineImage!.Bytes.Should().Equal(bytes);
        reopened.Body.Paragraphs.Single().Runs[1].InlineImageWidthEmu.Should().Be(228_600);
    }

    [Fact]
    public void XamlPackageFlowDocument_PreservesNestedInlineTableAsObjectReplacementRun()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Paragraph><Run Text="Before"/><InlineUIContainer><Border>
                <Table><TableRowGroup><TableRow>
                  <TableCell Background="#FFF2CC"><Paragraph>Outer</Paragraph></TableCell>
                  <TableCell><Paragraph>Inner <InlineUIContainer><Border>
                    <Table><TableRowGroup><TableRow>
                      <TableCell><Paragraph>Nested</Paragraph></TableCell>
                    </TableRow></TableRowGroup></Table>
                  </Border></InlineUIContainer></Paragraph></TableCell>
                </TableRow></TableRowGroup></Table>
              </Border></InlineUIContainer><Run Text="After"/></Paragraph>
            </FlowDocument>
            """;

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml));

        payload.Should().NotBeNull();
        var runs = payload!.Body.Paragraphs.Single().Runs;
        runs.Select(run => run.Text).Should().Equal("Before", "\uFFFC", "After");
        var outer = runs[1].InlineTable;
        outer.Should().NotBeNull();
        outer!.Table.Rows.Should().HaveCount(1);
        outer.Table.Rows[0].Cells.Should().HaveCount(2);
        outer.Table.Rows[0].Cells[0].Fill.Should().BeOfType<ShapeFill.Solid>();
        outer.Table.Rows[0].Cells[1].TextBody!.Paragraphs[0].Runs
            .Select(run => run.Text).Should().Contain("\uFFFC");
        outer.Table.Rows[0].Cells[1].TextBody!.Paragraphs[0].Runs
            .Single(run => run.InlineTable is not null)
            .InlineTable!.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text
            .Should().Be("Nested");

        var reopened = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));
        reopened.Should().NotBeNull();
        reopened!.Body.Paragraphs.Single().Runs[1].InlineTable!.Table.Rows[0].Cells[1]
            .TextBody!.Paragraphs[0].Runs.Single(run => run.InlineTable is not null)
            .InlineTable!.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text
            .Should().Be("Nested");
    }

    [Fact]
    public void XamlPackageFlowDocument_RejectsOversizedTableRows()
    {
        var cells = string.Concat(Enumerable.Repeat(
            "<TableCell><Paragraph>x</Paragraph></TableCell>",
            ExternalXamlClipboardPlanner.MaxTableCellsPerRow + 1));
        var xaml = $"<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Table><TableRowGroup><TableRow>{cells}</TableRow></TableRowGroup></Table></FlowDocument>";

        ExternalXamlClipboardPlanner.TryParseXamlPackage(CreateXamlPackage(xaml))
            .Should().BeNull();
    }

    [Fact]
    public void Rtf1Success_PreservesParagraphsRunsFontColorUnicodeTabsAndLineBreaks()
    {
        const string rtf =
            @"{\rtf1\ansi\ansicpg1252\deff0\uc1
{\fonttbl{\f0 Calibri;}{\f1 Arial;}}
{\colortbl;\red192\green0\blue0;\red0\green128\blue0;}
\f0\fs24\b Bold\b0\tab\cf1 Red\cf0\ul Under\ul0\par
\f1\fs18\i\u945?\i0\line Plain}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Bold\tRedUnder\n\u03B1\nPlain");
        payload.Body.Paragraphs.Should().HaveCount(2);
        payload.Body.Paragraphs[0].Runs.Should().HaveCount(4);
        payload.Body.Paragraphs[0].Runs[0].Text.Should().Be("Bold");
        payload.Body.Paragraphs[0].Runs[0].FontFamily.Should().Be("Calibri");
        payload.Body.Paragraphs[0].Runs[0].FontSizePt.Should().Be(12);
        payload.Body.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
        payload.Body.Paragraphs[0].Runs[1].Text.Should().Be("\t");
        payload.Body.Paragraphs[0].Runs[2].Text.Should().Be("Red");
        payload.Body.Paragraphs[0].Runs[2].Color!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        payload.Body.Paragraphs[0].Runs[3].Text.Should().Be("Under");
        payload.Body.Paragraphs[0].Runs[3].Underline.Should().BeTrue();
        payload.Body.Paragraphs[1].Runs[0].Text.Should().Be("\u03B1");
        payload.Body.Paragraphs[1].Runs[0].FontFamily.Should().Be("Arial");
        payload.Body.Paragraphs[1].Runs[0].FontSizePt.Should().Be(9);
        payload.Body.Paragraphs[1].Runs[0].Italic.Should().BeTrue();
        payload.Body.Paragraphs[1].Runs[1].Text.Should().Be("\nPlain");
    }

    [Fact]
    public void RtfTabStops_PreservePositionsAlignmentResetAndRichClipboardRoundTrip()
    {
        const string rtf =
            @"{\rtf1\ansi
\pard\tqc\tx1440\tqr\tx2880\tqdec\tx4320 First\tab Center\tab 12.50\par
\pard\tx720 Second}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.Body.Paragraphs.Should().HaveCount(2);
        payload.Body.Paragraphs[0].TabStops.Select(stop =>
                (stop.PositionEmu, stop.Alignment))
            .Should().Equal(
                (914_400L, TabStopAlignment.Center),
                (1_828_800L, TabStopAlignment.Right),
                (2_743_200L, TabStopAlignment.Decimal));
        payload.Body.Paragraphs[1].TabStops.Should().ContainSingle()
            .Which.Should().Match<TabStop>(stop =>
                stop.PositionEmu == 457_200L
                && stop.Alignment == TabStopAlignment.Left);

        var reopened = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));

        reopened.Should().NotBeNull();
        reopened!.Body.Paragraphs[0].TabStops.Select(stop =>
                (stop.PositionEmu, stop.Alignment))
            .Should().Equal(
                (914_400L, TabStopAlignment.Center),
                (1_828_800L, TabStopAlignment.Right),
                (2_743_200L, TabStopAlignment.Decimal));
    }

    [Fact]
    public void RtfTabStops_GroupLocalControlsDoNotLeakAfterGroupClose()
    {
        const string rtf =
            @"{\rtf1\ansi\pard\tx1440 Outer {\tqc\tx2880 Inner} After}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.Body.Paragraphs.Single().TabStops.Should().ContainSingle()
            .Which.Should().Match<TabStop>(stop =>
                stop.PositionEmu == 914_400L
                && stop.Alignment == TabStopAlignment.Left);
    }

    [Fact]
    public void RtfTabStopLeaders_PreserveEachLeaderThroughRichClipboardRoundTrip()
    {
        const string rtf =
            @"{\rtf1\ansi\pard\tldot\tx1440\tlhyph\tx2880\tlul\tx4320\tlth\tx5760\tleq\tx7200 Contents}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.Body.Paragraphs.Single().TabStops
            .Select(stop => (stop.PositionEmu, stop.Leader))
            .Should().Equal(
                (914_400L, TabStopLeader.Dots),
                (1_828_800L, TabStopLeader.Hyphens),
                (2_743_200L, TabStopLeader.Underscore),
                (3_657_600L, TabStopLeader.ThickLine),
                (4_572_000L, TabStopLeader.Equal));

        var reopened = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));

        reopened!.Body.Paragraphs.Single().TabStops
            .Select(stop => stop.Leader)
            .Should().Equal(
                TabStopLeader.Dots,
                TabStopLeader.Hyphens,
                TabStopLeader.Underscore,
                TabStopLeader.ThickLine,
                TabStopLeader.Equal);
    }

    [Fact]
    public void RtfNestedTable_PreservesRecursiveInlineTableAndSurroundingText()
    {
        const string rtf =
            @"{\rtf1\ansi
\trowd\itap1\trqc\trrh-480\trpaddl120\trpaddr240\trpaddt60\trpaddb80
\clpadl300\cellx2000\cellx4000
\intbl Outer A\cell
\trowd\itap2\nesttableprops\trqr\trrh720\clpadl40\clpadr80\clpadt100\clpadb140\cellx1000\cellx2000
\intbl Inner B\nestcell
\intbl Inner C\nestcell
\nestrow
\itap1\cell
\row
 Before and after}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.Body.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Any(run => run.InlineTable is not null)
            .Should().BeTrue();
        var outerRun = payload.Body.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.InlineTable is not null);
        outerRun.InlineTable!.Table.Rows.Should().HaveCount(1);
        outerRun.InlineTable.Table.Rows[0].HeightEmu.Should().Be(304_800);
        outerRun.InlineTable.Table.Rows[0].HeightRule.Should().Be(TableRowHeightRule.Exact);
        outerRun.InlineTable.Table.Rows[0].HorizontalAlignment
            .Should().Be(TableRowHorizontalAlignment.Center);
        outerRun.InlineTable.Table.Rows[0].Cells.Should().HaveCount(2);
        var outerCells = outerRun.InlineTable.Table.Rows[0].Cells;
        outerCells[0].InsetLeftPt.Should().Be(15);
        outerCells[0].InsetRightPt.Should().Be(12);
        outerCells[0].InsetTopPt.Should().Be(3);
        outerCells[0].InsetBottomPt.Should().Be(4);
        outerCells[1].InsetLeftPt.Should().Be(6);
        outerCells[1].InsetRightPt.Should().Be(12);
        outerCells[1].InsetTopPt.Should().Be(3);
        outerCells[1].InsetBottomPt.Should().Be(4);
        outerRun.InlineTable.Table.Rows[0].Cells[0].TextBody!
            .Paragraphs[0].Runs[0].Text.Should().Be("Outer A");
        var innerRun = outerRun.InlineTable.Table.Rows[0].Cells[1].TextBody!
            .Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.InlineTable is not null);
        innerRun.InlineTable!.Table.Rows.Should().HaveCount(1);
        innerRun.InlineTable.Table.Rows[0].HeightEmu.Should().Be(457_200);
        innerRun.InlineTable.Table.Rows[0].HeightRule.Should().Be(TableRowHeightRule.AtLeast);
        innerRun.InlineTable.Table.Rows[0].HorizontalAlignment
            .Should().Be(TableRowHorizontalAlignment.Right);
        innerRun.InlineTable.Table.Rows[0].Cells.Select(cell =>
                cell.TextBody!.Paragraphs[0].Runs[0].Text)
            .Should().Equal("Inner B", "Inner C");
        var innerCells = innerRun.InlineTable.Table.Rows[0].Cells;
        innerCells[0].InsetLeftPt.Should().Be(2);
        innerCells[0].InsetRightPt.Should().Be(4);
        innerCells[0].InsetTopPt.Should().Be(5);
        innerCells[0].InsetBottomPt.Should().Be(7);
        innerCells[1].InsetLeftPt.Should().BeNull();
        payload.PlainText.Should().Contain("\uFFFC");
        payload.PlainText.Should().Contain("Before and after");

        var reopened = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));
        reopened.Should().NotBeNull();
        reopened!.Body.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.InlineTable is not null)
            .InlineTable!.Table.Rows[0].Cells[1].TextBody!
            .Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.InlineTable is not null)
            .InlineTable!.Table.Rows[0].Cells[1].TextBody!
            .Paragraphs[0].Runs[0].Text.Should().Be("Inner C");
        reopened.Body.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.InlineTable is not null)
            .InlineTable!.Table.Rows[0].HeightRule.Should().Be(TableRowHeightRule.Exact);
        var reopenedOuterCells = reopened.Body.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.InlineTable is not null)
            .InlineTable!.Table.Rows[0].Cells;
        reopenedOuterCells[0].InsetLeftPt.Should().Be(15);
        reopenedOuterCells[1].InsetLeftPt.Should().Be(6);
        reopenedOuterCells[1].InsetRightPt.Should().Be(12);
        var reopenedInnerCells = reopenedOuterCells[1].TextBody!
            .Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.InlineTable is not null)
            .InlineTable!.Table.Rows[0].Cells;
        reopenedInnerCells[0].InsetLeftPt.Should().Be(2);
        reopenedInnerCells[0].InsetRightPt.Should().Be(4);
        reopenedInnerCells[0].InsetTopPt.Should().Be(5);
        reopenedInnerCells[0].InsetBottomPt.Should().Be(7);
    }

    [Fact]
    public void RtfTableRowAlignment_PreservesLeftControlAndDefaultsToLeftWhenOmitted()
    {
        const string rtf =
            @"{\rtf1\ansi
\trowd\itap1\trql\cellx2000\cellx4000
\intbl Explicit left\cell
\trowd\itap2\nesttableprops\cellx1000
\intbl Nested default\nestcell
\nestrow
\itap1\cell\row}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        var table = payload!.Body.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.InlineTable is not null)
            .InlineTable!.Table;
        table.Rows.Should().ContainSingle();
        table.Rows[0].HorizontalAlignment.Should().Be(TableRowHorizontalAlignment.Left);
        table.Rows[0].Cells[1].TextBody!.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.InlineTable is not null)
            .InlineTable!.Table.Rows[0].HorizontalAlignment.Should().BeNull();
    }

    [Fact]
    public void RtfInlineTable_PreservesTableIndentAlignmentAndCellGap()
    {
        const string rtf =
            @"{\rtf1\ansi
\trowd\itap1\trrh-480\trleft240\trgaph60\trqc\trpaddl120\trpaddr240\trpaddt60\trpaddb80
\clpadl300\cellx2000\cellx4000
\intbl Outer A\cell
\trowd\itap2\nesttableprops\trrh720\cellx1000\cellx2000
\intbl Inner B\nestcell
\intbl Inner C\nestcell
\nestrow
\itap1\cell
\row
 Before and after}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        var table = payload!.Body.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.InlineTable is not null)
            .InlineTable!.Table;
        table.RichTextLeftIndentPt.Should().Be(12);
        table.RichTextCellSpacingPt.Should().Be(6);
        table.Rows[0].HorizontalAlignment.Should().Be(TableRowHorizontalAlignment.Center);

        var reopened = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));
        var reopenedTable = reopened!.Body.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.InlineTable is not null)
            .InlineTable!.Table;
        reopenedTable.RichTextLeftIndentPt.Should().Be(12);
        reopenedTable.RichTextCellSpacingPt.Should().Be(6);
        reopenedTable.Rows[0].HorizontalAlignment.Should().Be(TableRowHorizontalAlignment.Center);
    }

    [Fact]
    public void RtfTableRowBorders_MapToOuterCellEdgesAndSurviveClipboardRoundTrip()
    {
        const string rtf =
            @"{\rtf1\ansi{\colortbl;\red255\green0\blue0;}
\trowd\trbrdrl\brdrs\brdrw20\brdrcf1
\trbrdrr\brdrs\brdrw20\brdrcf1
\trbrdrt\brdrs\brdrw20\brdrcf1
\trbrdrb\brdrs\brdrw20\brdrcf1
\cellx2000\cellx4000
\intbl Outer\cell
\trowd\itap2\nesttableprops\trbrdrt\brdrs\brdrw20\brdrcf1\cellx1000\cellx2000
\intbl Inner left\nestcell
\intbl Inner right\nestcell
\nestrow
\itap1\cell\row}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        var table = payload!.Body.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.InlineTable is not null)
            .InlineTable!.Table;
        var cells = table.Rows.Should().ContainSingle().Which.Cells;
        cells.Should().HaveCount(2);

        var left = cells[0].Borders!;
        var right = cells[1].Borders!;
        ((ShapeOutline.Visible)left.Left!).WidthPt.Should().Be(1);
        left.Right.Should().BeNull();
        ((ShapeOutline.Visible)right.Right!).WidthPt.Should().Be(1);
        ((ShapeOutline.Visible)left.Top!).WidthPt.Should().Be(1);
        ((ShapeOutline.Visible)right.Bottom!).WidthPt.Should().Be(1);
        ((ShapeOutline.Visible)left.Left!).Color.Resolved.Should().Be(SrgbColor.FromRgb(0xFF0000));

        var reopened = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));
        var reopenedCells = reopened!.Body.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.InlineTable is not null)
            .InlineTable!.Table.Rows.Single().Cells;
        ((ShapeOutline.Visible)reopenedCells[0].Borders!.Left!).WidthPt.Should().Be(1);
        ((ShapeOutline.Visible)reopenedCells[1].Borders!.Right!).Color.Resolved
            .Should().Be(SrgbColor.FromRgb(0xFF0000));
    }

    [Fact]
    public void RtfSuperscriptAndSubscript_PreserveBaselineControls()
    {
        const string rtf =
            @"{\rtf1\ansi\deff0{\fonttbl{\f0 Calibri;}}\f0\fs24 H\super i\sub j\nosupersub k\up12 u\dn6 d}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        var runs = payload!.Body.Paragraphs.Single().Runs;
        runs.Select(run => run.Text).Should().Equal("H", "i", "j", "k", "u", "d");
        runs[0].BaselineOffset.Should().BeNull();
        runs[1].BaselineOffset.Should().Be(25_000);
        runs[2].BaselineOffset.Should().Be(-25_000);
        runs[3].BaselineOffset.Should().BeNull();
        runs[4].BaselineOffset.Should().Be(50_000);
        runs[5].BaselineOffset.Should().Be(-25_000);
    }

    [Fact]
    public void RtfCapsControls_PreserveRunCapitalizationSemantics()
    {
        const string rtf =
            @"{\rtf1\ansi\deff0{\fonttbl{\f0 Calibri;}}\f0\fs24 a\caps b\caps0 c\scaps d\scaps0 e}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        var runs = payload!.Body.Paragraphs.Single().Runs;
        runs.Select(run => run.Text).Should().Equal("a", "b", "c", "d", "e");
        runs[0].Caps.Should().Be(RunTextCaps.None);
        runs[1].Caps.Should().Be(RunTextCaps.All);
        runs[2].Caps.Should().Be(RunTextCaps.None);
        runs[3].Caps.Should().Be(RunTextCaps.Small);
        runs[4].Caps.Should().Be(RunTextCaps.None);
    }

    [Fact]
    public void RtfPict_PreservesPngPayloadAlongsideText()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");
        var rtf = Encoding.ASCII.GetBytes(
            @"{\rtf1\ansi Before {\pict\pngblip " + Convert.ToHexString(png) + @"} After}");

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(rtf);

        payload.Should().NotBeNull();
        payload!.HasImage.Should().BeTrue();
        payload.ImageContentType.Should().Be("image/png");
        payload.ImageBytes.Should().Equal(png);
        payload.PlainText.Should().Be("Before \uFFFC After");
        payload.Body.Paragraphs.Single().Runs
            .Any(run => run.InlineImage is { } image && image.Bytes.SequenceEqual(png))
            .Should().BeTrue();
    }

    [Fact]
    public void RtfPict_PreservesAuthoredDisplayDimensionsAndScale()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");
        var rtf = Encoding.ASCII.GetBytes(
            @"{\rtf1\ansi{\pict\pngblip\picwgoal1440\pichgoal720\picscalex50\picscaley200 "
            + Convert.ToHexString(png) + "}}");

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(rtf);

        payload.Should().NotBeNull();
        var image = payload!.GetImagePayloads().Should().ContainSingle().Subject;
        image.WidthEmu.Should().Be(457_200);
        image.HeightEmu.Should().Be(914_400);

        var reopened = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));
        reopened.Should().NotBeNull();
        reopened!.GetImagePayloads().Single().WidthEmu.Should().Be(457_200);
        reopened.GetImagePayloads().Single().HeightEmu.Should().Be(914_400);
    }

    [Fact]
    public void RtfPict_RecognizesJpegSignature()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xD9];
        var rtf = Encoding.ASCII.GetBytes(
            @"{\rtf1\ansi{\pict\jpegblip " + Convert.ToHexString(jpeg) + "}}");

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(rtf);

        payload.Should().NotBeNull();
        payload!.ImageContentType.Should().Be("image/jpeg");
        payload.ImageBytes.Should().Equal(jpeg);
    }

    [Fact]
    public void RtfPict_PreservesEveryImageAndRichClipboardRoundTripsThem()
    {
        var first = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");
        var second = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var rtf = Encoding.ASCII.GetBytes(
            @"{\rtf1\ansi Before {\pict\pngblip " + Convert.ToHexString(first)
            + @"} middle {\pict\jpegblip " + Convert.ToHexString(second) + @"} After}");

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(rtf);

        payload.Should().NotBeNull();
        payload!.GetImagePayloads().Should().HaveCount(2);
        payload.GetImagePayloads()[0].Bytes.Should().Equal(first);
        payload.GetImagePayloads()[0].ContentType.Should().Be("image/png");
        payload.GetImagePayloads()[1].Bytes.Should().Equal(second);
        payload.GetImagePayloads()[1].ContentType.Should().Be("image/jpeg");
        payload.PlainText.Should().Be("Before \uFFFC middle \uFFFC After");

        var reopened = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));
        reopened.Should().NotBeNull();
        reopened!.GetImagePayloads().Select(image => image.ContentType)
            .Should().Equal("image/png", "image/jpeg");
        reopened.GetImagePayloads()[0].Bytes.Should().Equal(first);
        reopened.GetImagePayloads()[1].Bytes.Should().Equal(second);
    }

    [Fact]
    public void RtfObject_PreservesVisibleResultAndEmbeddedPayload()
    {
        const string rtf =
            @"{\rtf1\ansi Before {\object{\*\objclass Word.Document.12}{\*\objdata 010203}{\result Embedded result}} After}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Before \uFFFCEmbedded result After");
        payload.GetObjectPayloads().Should().ContainSingle();
        payload.GetObjectPayloads()[0].Bytes.Should().Equal(0x01, 0x02, 0x03);
        payload.GetObjectPayloads()[0].FileName.Should().Be("Embedded.docx");
        payload.GetObjectPayloads()[0].ClassName.Should().Be("Word.Document.12");
        var inline = payload.Body.Paragraphs.Single().Runs
            .Single(run => run.InlineOleObject is not null);
        inline.Text.Should().Be("\uFFFC");
        inline.InlineOleObject!.EmbeddedBytes.Should().Equal(0x01, 0x02, 0x03);
        inline.InlineOleObject.FileName.Should().Be("Embedded.docx");

        var restored = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));
        restored.Should().NotBeNull();
        restored!.GetObjectPayloads().Should().ContainSingle();
        restored.GetObjectPayloads()[0].Bytes.Should().Equal(0x01, 0x02, 0x03);
        restored.GetObjectPayloads()[0].FileName.Should().Be("Embedded.docx");
        restored.GetObjectPayloads()[0].ClassName.Should().Be("Word.Document.12");
        restored.Body.Paragraphs.Single().Runs
            .Single(run => run.InlineOleObject is not null)
            .InlineOleObject!.EmbeddedBytes.Should().Equal(0x01, 0x02, 0x03);

        var slideBody = InCanvasRichClipboardPlanner.CloneBodyForSlideFallback(payload.Body);
        InCanvasTextEditPlanner.ExtractPlainText(slideBody)
            .Should().Be("Before Embedded result After");
        slideBody.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .All(run => run.InlineOleObject is null)
            .Should().BeTrue();
    }

    [Fact]
    public void RtfObject_PreservesCustomOleClassThroughInsertionMetadata()
    {
        const string rtf =
            @"{\rtf1\ansi{\object{\*\objclass Vendor.Custom.Widget.7}{\*\objdata 0102}{\result Widget}}}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        var source = payload!.GetObjectPayloads().Should().ContainSingle().Subject;
        source.FileName.Should().Be("Embedded.bin");
        source.ClassName.Should().Be("Vendor.Custom.Widget.7");

        var ole = OleInsertionPlanner.CreatePayload(source.Bytes, source.FileName, source.ClassName);
        ole.ProgId.Should().Be("Vendor.Custom.Widget.7");
        ole.OleObjXml.Should().Contain("progId=\"Vendor.Custom.Widget.7\"");
    }

    [Fact]
    public void UnsupportedAndMalformedRtf_IsBoundedAndNeverThrows()
    {
        var partial = ExternalRichTextClipboardPlanner.TryParseRtf(
            Encoding.ASCII.GetBytes(@"{\rtf1\ansi Before {\*\generator ignored} After\b bold"));

        partial.Should().NotBeNull();
        partial!.PlainText.Should().Be("Before  Afterbold");
        ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes("plain text"))
            .Should().BeNull();

        var oversized = Encoding.ASCII.GetBytes(
            "{\\rtf1\\ansi " + new string('x', ExternalRichTextClipboardPlanner.MaxOutputCharacters + 1));
        ExternalRichTextClipboardPlanner.TryParseRtf(oversized).Should().BeNull();
    }

    [Fact]
    public void RtfCharacterDirection_RtlchAndLtrch_PreserveMixedRunOverrides()
    {
        const string rtf =
            @"{\rtf1\ansi\rtlpar\rtlch\u1488?\u1489?\u1490?\ltrch LTR}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        var runs = payload!.Body.Paragraphs.Single().Runs;
        runs.Should().Contain(run => run.Text == "\u05D0\u05D1\u05D2" && run.RightToLeft == true);
        runs.Should().Contain(run => run.Text == "LTR" && run.RightToLeft == false);
    }

    [Fact]
    public void PlannerApply_PastesExternalFragmentWithItsRichRuns()
    {
        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(
            Encoding.ASCII.GetBytes(@"{\rtf1\ansi\b External\b0\par second}"));
        var destination = InCanvasRichClipboardPayload.FromPlainText("BeforeAfter").Body;

        payload.Should().NotBeNull();
        var updated = InCanvasRichClipboardPlanner.Apply(
            destination,
            new InCanvasEditorTextSelection(6, 6),
            payload!,
            out var caret);

        caret.Should().Be(6 + payload!.PlainText.Length);
        InCanvasTextEditPlanner.ExtractPlainText(updated).Should().Be("BeforeExternal\nsecondAfter");
        updated.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().Contain(run => run.Text == "External" && run.Bold);
    }

    [Fact]
    public void WordListTable_PreservesNestedLevelsNumberFormatRestartAndParagraphLayout()
    {
        const string rtf =
            @"{\rtf1\ansi
{\listtable
{\list\listid1
{\listlevel\levelnfc0\levelstartat3\leveltext\'02\'00.;\levelnumbers\'01;}
{\listlevel\levelnfc23\levelstartat1\leveltext\'01\u8226?;\levelnumbers;}
}}
{\listoverridetable{\listoverride\listid1\ls1}}
\pard\ls1\ilvl0\li720\fi-360\ql\sb120\sa240 First\par
\pard\ls1\ilvl1\li1440\fi-360\qc Nested\par
\pard\ls1\ilvl0\qr Second}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("First\nNested\nSecond");
        payload.Body.Paragraphs.Should().HaveCount(3);

        var first = payload.Body.Paragraphs[0];
        first.BulletKind.Should().Be(BulletKind.Auto);
        first.AutoNumType.Should().Be(AutoNumType.ArabicPeriod);
        first.AutoNumStartAt.Should().Be(3);
        first.AutoNumStartAtSpecified.Should().BeTrue();
        first.Level.Should().Be(0);
        first.Align.Should().Be(TextAlign.Left);
        first.MarginLeftEmu.Should().Be(457200);
        first.IndentEmu.Should().Be(-228600);
        first.SpaceBeforePt.Should().Be(6);
        first.SpaceAfterPt.Should().Be(12);

        var nested = payload.Body.Paragraphs[1];
        nested.BulletKind.Should().Be(BulletKind.Char);
        nested.BulletChar.Should().Be("\u2022");
        nested.Level.Should().Be(1);
        nested.Align.Should().Be(TextAlign.Center);

        var continuation = payload.Body.Paragraphs[2];
        continuation.BulletKind.Should().Be(BulletKind.Auto);
        continuation.AutoNumStartAt.Should().Be(3);
        continuation.AutoNumStartAtSpecified.Should().BeFalse();
        continuation.Align.Should().Be(TextAlign.Right);
    }

    [Fact]
    public void WordListTable_UsesCustomLevelTextGlyphForBulletLevels()
    {
        const string rtf =
            @"{\rtf1\ansi
{\listtable
{\list\listid7
{\listlevel\levelnfc23\levelstartat1\leveltext\'01\u9654?;\levelnumbers;}
}}
{\listoverridetable{\listoverride\listid7\ls7}}
\pard\ls7\ilvl0 Custom bullet}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Custom bullet");
        var paragraph = payload.Body.Paragraphs.Single();
        paragraph.BulletKind.Should().Be(BulletKind.Char);
        paragraph.BulletChar.Should().Be("▶");
    }

    [Fact]
    public void WordListTable_UsesLevelTextPunctuationForExistingAutoNumberVariants()
    {
        const string rtf =
            @"{\rtf1\ansi
{\listtable
{\list\listid8
{\listlevel\levelnfc3\levelstartat1\leveltext\'02\'00);\levelnumbers;}
}}
{\listoverridetable{\listoverride\listid8\ls8}}
\pard\ls8\ilvl0 Alpha list}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.Body.Paragraphs.Single().AutoNumType.Should().Be(AutoNumType.AlphaUcParenR);
    }

    [Fact]
    public void WordListTable_PreservesMultiLevelLevelTextTemplateAndRichClipboardRoundTrip()
    {
        const string rtf =
            @"{\rtf1\ansi
{\listtable
{\list\listid9
{\listlevel\levelnfc0\levelstartat1\leveltext\'02\'00.;\levelnumbers\'01;}
{\listlevel\levelnfc0\levelstartat1\leveltext\'04\'00.\'01.;\levelnumbers\'01\'02;}
}}
{\listoverridetable{\listoverride\listid9\ls9}}
\pard\ls9\ilvl0 Root\par
\pard\ls9\ilvl1 Child}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.Body.Paragraphs[0].AutoNumTextTemplate.Should().Be("%1.");
        payload.Body.Paragraphs[1].AutoNumTextTemplate.Should().Be("%1.%2.");

        var reopened = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));
        reopened.Should().NotBeNull();
        reopened!.Body.Paragraphs.Select(paragraph => paragraph.AutoNumTextTemplate)
            .Should().Equal("%1.", "%1.%2.");
    }

    [Fact]
    public void WordListOverride_StartAtRestart_IsAppliedOnlyToItsFirstParagraph()
    {
        const string rtf =
            @"{\rtf1\ansi
{\listtable
{\list\listid1
{\listlevel\levelnfc0\levelstartat1\leveltext\'02\'00.;\levelnumbers\'01;}
}}
{\listoverridetable
{\listoverride\listid1\listoverridecount1
{\lfolevel\listoverridestart\levelstartat7}\ls1}}
\pard\ls1\ilvl0 Restarted\par
\pard\ls1\ilvl0 Continues}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.Body.Paragraphs.Should().HaveCount(2);
        var first = payload.Body.Paragraphs[0];
        first.BulletKind.Should().Be(BulletKind.Auto);
        first.AutoNumType.Should().Be(AutoNumType.ArabicPeriod);
        first.AutoNumStartAt.Should().Be(7);
        first.AutoNumStartAtSpecified.Should().BeTrue();

        var continuation = payload.Body.Paragraphs[1];
        continuation.AutoNumStartAt.Should().Be(7);
        continuation.AutoNumStartAtSpecified.Should().BeFalse();
    }

    [Fact]
    public void WordListOverride_FormattingLevel_PreservesBulletAndIndentGeometry()
    {
        const string rtf =
            @"{\rtf1\ansi
{\listtable
{\list\listid1
{\listlevel\levelnfc0\levelstartat1\leveltext\'02\'00.;\levelnumbers\'01;}
}}
{\listoverridetable
{\listoverride\listid1\listoverridecount1
{\lfolevel\listoverrideformat1
{\listlevel\levelnfc23\levelstartat1\li1440\fi-360\leveltext\'01\u8226?;\levelnumbers;}}
\ls1}}
\pard\ls1\ilvl0 Overridden}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        var paragraph = payload!.Body.Paragraphs.Single();
        paragraph.BulletKind.Should().Be(BulletKind.Char);
        paragraph.BulletChar.Should().Be("\u2022");
        paragraph.MarginLeftEmu.Should().Be(914400);
        paragraph.IndentEmu.Should().Be(-228600);
    }

    [Fact]
    public void WordTableControls_FlattenRowsAndCellsLikeWpfProjection_AndPreserveCellFormatting()
    {
        const string rtf =
            @"{\rtf1\ansi
\trowd\trgaph108\cellx1440\cellx2880
{\b Header}\cell{\i Value}\cell\row
\trowd\cellx1440\cellx2880
Left\cell{\ul Right}\ul0\cell\row}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Header\tValue\nLeft\tRight");
        payload.TableColumnWidthsEmu.Should().Equal(914400L, 914400L);
        payload.Body.Paragraphs.Should().HaveCount(2);
        payload.Body.Paragraphs[0].Runs.Should().Contain(run => run.Text == "Header" && run.Bold);
        payload.Body.Paragraphs[0].Runs.Should().Contain(run => run.Text == "Value" && run.Italic);
        payload.Body.Paragraphs[1].Runs.Should().Contain(run => run.Text == "Right" && run.Underline);
    }

    [Fact]
    public void WordTableCellStyles_PreserveSolidFillAndCommonBorders()
    {
        const string rtf =
            @"{\rtf1\ansi
{\colortbl;\red255\green255\blue0;\red31\green78\blue121;}
\trowd\clcbpat1\clvertalc\clpadl120\clpadr240\clpadt60\clpadb180\clbrdrl\brdrs\brdrw10\brdrcf2\cellx1440\cellx2880
Header\cell Value\cell\row}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.TableCellStyles.Should().HaveCount(2);
        payload.TableCellStyles![0].FillRgb.Should().Be(0xFFFF00);
        var left = payload.TableCellStyles[0].Left;
        left.Should().NotBeNull();
        left!.ColorRgb.Should().Be(0x1F4E79);
        left.WidthPt.Should().Be(0.5);
        payload.TableCellStyles[0].Anchor.Should().Be(TableCellAnchor.Middle);
        payload.TableCellStyles[0].InsetLeftPt.Should().Be(6);
        payload.TableCellStyles[0].InsetRightPt.Should().Be(12);
        payload.TableCellStyles[0].InsetTopPt.Should().Be(3);
        payload.TableCellStyles[0].InsetBottomPt.Should().Be(9);
        payload.TableCellStyles[1].FillRgb.Should().BeNull();
    }

    [Fact]
    public void WordTableCellShading_PreservesPatternAndForegroundBackgroundColors()
    {
        const string rtf =
            @"{\rtf1\ansi
{\colortbl;\red255\green255\blue255;\red31\green78\blue121;\red242\green242\blue242;}
\trowd\clcbpat1\clcfpat2\clbghoriz\cellx1440\clcbpat3\clcfpat2\clbgcross\cellx2880
\clcbpat3\clshdng100\cellx4320
Header\cell Body\cell Solid\cell\row}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.TableCellStyles.Should().HaveCount(3);
        payload.TableCellStyles![0].FillPattern.Should().Be("horzStripe");
        payload.TableCellStyles[0].FillForegroundRgb.Should().Be(0x1F4E79);
        payload.TableCellStyles[0].FillBackgroundRgb.Should().Be(0xFFFFFF);
        payload.TableCellStyles[1].FillPattern.Should().Be("cross");
        payload.TableCellStyles[2].FillPattern.Should().Be("pct100");

        ClipboardTablePlanner.TryBuildStandaloneTable(
            payload.Body,
            payload.TableColumnWidthsEmu,
            payload.TableCellStyles,
            out var table).Should().BeTrue();

        var firstFill = table.Rows[0].Cells[0].Fill.Should().BeOfType<ShapeFill.Pattern>().Subject;
        firstFill.Preset.Should().Be("horzStripe");
        firstFill.ForegroundColor.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        firstFill.BackgroundColor.Resolved.Should().Be(SrgbColor.FromRgb(0xFFFFFF));
    }

    [Fact]
    public void WordTableMergeControls_PreserveNativeGridAndRowSpans()
    {
        const string rtf =
            @"{\rtf1\ansi
\trowd\clmgf\cellx1440\clmrg\cellx2880\cellx4320
Merged\cell\cell Tail\cell\row
\trowd\clvmgf\cellx1440\cellx2880
Top\cell Right\cell\row
\trowd\clvmrg\cellx1440\cellx2880
\cell Bottom\cell\row}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.TableCellStyles.Should().HaveCount(7);
        payload.TableCellStyles![0].HorizontalMergeStart.Should().BeTrue();
        payload.TableCellStyles[1].HorizontalMergeContinuation.Should().BeTrue();
        payload.TableCellStyles[3].VerticalMergeStart.Should().BeTrue();
        payload.TableCellStyles[5].VerticalMergeContinuation.Should().BeTrue();

        ClipboardTablePlanner.TryBuildStandaloneTable(
            payload.Body,
            payload.TableColumnWidthsEmu,
            payload.TableCellStyles,
            out var table).Should().BeTrue();

        table.Rows[0].Cells[0].GridSpan.Should().Be(2);
        table.Rows[0].Cells[1].HMerge.Should().BeTrue();
        table.Rows[0].Cells[2].GridSpan.Should().Be(1);
        table.Rows[1].Cells[0].RowSpan.Should().Be(2);
        table.Rows[2].Cells[0].VMerge.Should().BeTrue();
    }

    [Fact]
    public void NestedTableGroups_UseSameBoundedCellAndRowProjection()
    {
        const string rtf =
            @"{\rtf1\ansi\trowd\cellx1440\cellx2880
Outer {\b one}\cell{\i two}\cell\row
\trowd\cellx1440\cellx2880
Three\cell Four\cell\row}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Outer one\ttwo\nThree\tFour");
        payload.Body.Paragraphs[0].Runs.Should().Contain(run => run.Text == "one" && run.Bold);
        payload.Body.Paragraphs[0].Runs.Should().Contain(run => run.Text == "two" && run.Italic);
    }

    [Fact]
    public void ExcessiveTableCells_AreRejectedAsUntrustedInput()
    {
        var cells = string.Concat(Enumerable.Repeat("x\\cell ",
            ExternalRichTextClipboardPlanner.MaxTableCellsPerRow + 1));
        var rtf = Encoding.ASCII.GetBytes("{\\rtf1\\ansi\\trowd " + cells + "\\row}");

        ExternalRichTextClipboardPlanner.TryParseRtf(rtf).Should().BeNull();
    }

    [Fact]
    public void HyperlinkField_PreservesResultTextAndRejectsUnsafeTargets()
    {
        const string rtf =
            @"{\rtf1\ansi Before {\field{\*\fldinst HYPERLINK ""https://example.com/review""}{\fldrslt Click here}} "
            + @"{\field{\*\fldinst HYPERLINK ""javascript:alert(1)""}{\fldrslt Unsafe}} After}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Before Click here Unsafe After");
        payload.Body.Paragraphs.Single().Runs
            .Single(run => run.Text == "Click here")
            .Hyperlink!.Url.Should().Be("https://example.com/review");
        payload.Body.Paragraphs.Single().Runs
            .Single(run => run.Text.Contains("Unsafe", StringComparison.Ordinal))
            .Hyperlink.Should().BeNull();
    }

    [Fact]
    public void HyperlinkField_PreservesLocalFileTargetAndRejectsRemoteFileHost()
    {
        const string rtf =
            @"{\rtf1\ansi {\field{\*\fldinst HYPERLINK ""file:///C:/Reports/budget.xlsx""}{\fldrslt Open workbook}} "
            + @"{\field{\*\fldinst HYPERLINK ""file://server/share/budget.xlsx""}{\fldrslt Remote workbook}}}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        var runs = payload!.Body.Paragraphs.Single().Runs;
        runs.Select(run => run.Text).Should().Contain("Open workbook");
        runs.Single(run => run.Text == "Open workbook").Hyperlink!.Url
            .Should().Be("file:///C:/Reports/budget.xlsx");
        runs.Single(run => run.Text.Contains("Remote workbook", StringComparison.Ordinal))
            .Hyperlink.Should().BeNull();
    }

    [Fact]
    public void RtfField_PreservesNonHyperlinkTypeCachedResultAndClipboardRoundTrip()
    {
        const string rtf =
            @"{\rtf1\ansi Before {\field{\*\fldinst PAGE \\* MERGEFORMAT}{\fldrslt 2}} After}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        var fieldRun = payload!.Body.Paragraphs.Single().Runs
            .Single(run => run.Text == "2");
        fieldRun.Field.Should().NotBeNull();
        fieldRun.Field!.FieldType.Should().Be("PAGE");
        fieldRun.Field.CachedText.Should().Be("2");
        fieldRun.Hyperlink.Should().BeNull();

        var restored = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));
        restored.Should().NotBeNull();
        var restoredField = restored!.Body.Paragraphs.Single().Runs
            .Single(run => run.Text == "2");
        restoredField.Field!.FieldType.Should().Be("PAGE");
        restoredField.Field.CachedText.Should().Be("2");
    }

    [Fact]
    public void LegacyPnGroups_PreserveBulletLevelAndExplicitNumberRestart()
    {
        const string rtf =
            @"{\rtf1\ansi{\pn\pnlvlblt\pnseclvl2}\pard\li360\fi-360 Bullet\par
{\pn\pnlvlbody\pnstart4}\pard\li720\fi-360 Number}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.Body.Paragraphs.Should().HaveCount(2);
        payload.Body.Paragraphs[0].BulletKind.Should().Be(BulletKind.Char);
        payload.Body.Paragraphs[0].BulletChar.Should().Be("\u2022");
        payload.Body.Paragraphs[0].Level.Should().Be(1);
        payload.Body.Paragraphs[1].BulletKind.Should().Be(BulletKind.Auto);
        payload.Body.Paragraphs[1].AutoNumStartAt.Should().Be(4);
        payload.Body.Paragraphs[1].AutoNumStartAtSpecified.Should().BeTrue();
    }

    [Fact]
    public void LibreOfficeAndMalformedFragments_KeepEscapesBoundedAndDoNotLeakDestinations()
    {
        const string rtf =
            @"{\rtf1\ansi\uc1\b LibreOffice\b0 {\*\generator LibreOffice} \u233? {\object ignored} \{literal\}\par
\pard\qj\li360\sa80 Text {\field{\*\fldinst NOT_A_HYPERLINK}{\fldrslt field text}}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("LibreOffice \u00E9  {literal}\nText field text");
        payload.Body.Paragraphs.Should().HaveCount(2);
        payload.Body.Paragraphs[1].Align.Should().Be(TextAlign.Justify);
        payload.Body.Paragraphs[1].MarginLeftEmu.Should().Be(228600);
        payload.Body.Paragraphs[1].SpaceAfterPt.Should().Be(4);
        payload.Body.Paragraphs[1].Runs
            .Single(run => run.Text.Contains("field text", StringComparison.Ordinal))
            .Hyperlink.Should().BeNull();

        ExternalRichTextClipboardPlanner.TryParseRtf(
                Encoding.ASCII.GetBytes(@"{\rtf1\ansi {\field{\*\fldinst HYPERLINK ""https://example.com""}"))
            .Should().NotBeNull();
    }

    private static byte[] CreateXamlPackage(
        string xaml,
        params (string Name, byte[] Bytes)[] resources)
    {
        using var output = new MemoryStream();
        using (var package = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var resource in resources)
            {
                var entry = package.CreateEntry(resource.Name);
                using var stream = entry.Open();
                stream.Write(resource.Bytes);
            }

            using var writer = new StreamWriter(package.CreateEntry("Xaml/Document.xaml").Open(), Encoding.UTF8);
            writer.Write(xaml);
        }
        return output.ToArray();
    }
}
