using System.Xml;
using System.Xml.Xsl;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class SpreadsheetXmlFileAdapterTests
{
    [Fact]
    public void LoadTransformed_IgnoresGeneratedCommentsAndProcessingInstructions()
    {
        using var source = StreamFromString("<rows><row label=\"Alpha\" amount=\"42.5\" /></rows>");
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <xsl:processing-instruction name="freex">source="xslt"</xsl:processing-instruction>
                  <ss:Worksheet ss:Name="Noise">
                    <xsl:comment>generated worksheet metadata</xsl:comment>
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="row/@label" /></ss:Data></ss:Cell>
                        <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="row/@amount" /></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Noise");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(42.5));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromCommentAttributeValueTemplates()
    {
        using var source = StreamFromString("""
            <notes>
              <note label="Total" author="Finance" text="Check generated total"/>
            </notes>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/notes">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Comments">
                    <ss:Table>
                      <xsl:for-each select="note">
                        <ss:Row>
                          <ss:Cell>
                            <ss:Data ss:Type="String"><xsl:value-of select="@label"/></ss:Data>
                            <ss:Comment ss:Author="{@author}">
                              <ss:Data><xsl:value-of select="@text"/></ss:Data>
                            </ss:Comment>
                          </ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Comments");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.GetCell(address)!.Value.Should().Be(new TextValue("Total"));
        sheet.Comments[address].Should().Be("Check generated total");
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlHyperlinksAndComments()
    {
        using var source = StreamFromString("""
            <rows>
              <row name="Review" url="https://example.com/review" note="Check generated output"/>
            </rows>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/rows">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Generated">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <ss:Row>
                          <ss:Cell ss:HRef="{@url}" ss:HRefScreenTip="Open source">
                            <ss:Data ss:Type="String"><xsl:value-of select="@name"/></ss:Data>
                            <ss:Comment ss:Author="XSLT">
                              <ss:Data><xsl:value-of select="@note"/></ss:Data>
                            </ss:Comment>
                          </ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.GetCell(address)!.Value.Should().Be(new TextValue("Review"));
        sheet.Hyperlinks[address].Should().Be("https://example.com/review");
        sheet.HyperlinkMetadata[address].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open source",
            ""));
        sheet.Comments[address].Should().Be("Check generated output");
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromHyperlinkAttributeValueTemplates()
    {
        using var source = StreamFromString("""
            <links>
              <link label="Review" url="https://example.com/review" tip="Open review"/>
            </links>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/links">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Links">
                    <ss:Table>
                      <xsl:for-each select="link">
                        <ss:Row>
                          <ss:Cell ss:HRef="{@url}" ss:HRefScreenTip="{@tip}">
                            <ss:Data ss:Type="String"><xsl:value-of select="@label"/></ss:Data>
                          </ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Links");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.GetCell(address)!.Value.Should().Be(new TextValue("Review"));
        sheet.Hyperlinks[address].Should().Be("https://example.com/review");
        sheet.HyperlinkMetadata[address].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open review",
            ""));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromInternalHyperlinkAttributeValueTemplates()
    {
        using var source = StreamFromString("""
            <links>
              <link label="Jump to summary" target="#'Q1 Summary'!A1" tip="Open summary"/>
            </links>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/links">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Links">
                    <ss:Table>
                      <xsl:for-each select="link">
                        <ss:Row>
                          <ss:Cell ss:HRef="{@target}" ss:HRefScreenTip="{@tip}">
                            <ss:Data ss:Type="String"><xsl:value-of select="@label"/></ss:Data>
                          </ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Links");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.GetCell(address)!.Value.Should().Be(new TextValue("Jump to summary"));
        sheet.Hyperlinks[address].Should().Be("#'Q1 Summary'!A1");
        sheet.HyperlinkMetadata[address].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Open summary",
            "'Q1 Summary'!A1"));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromEmailHyperlinkAttributeValueTemplates()
    {
        using var source = StreamFromString("""
            <links>
              <link label="Email finance" target="mailto:finance@example.com" tip="Send email"/>
            </links>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/links">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="Links">
                    <ss:Table>
                      <xsl:for-each select="link">
                        <ss:Row>
                          <ss:Cell ss:HRef="{@target}" ss:HRefScreenTip="{@tip}">
                            <ss:Data ss:Type="String"><xsl:value-of select="@label"/></ss:Data>
                          </ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Links");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.GetCell(address)!.Value.Should().Be(new TextValue("Email finance"));
        sheet.Hyperlinks[address].Should().Be("mailto:finance@example.com");
        sheet.HyperlinkMetadata[address].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.EmailAddress,
            "Send email",
            ""));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlWorkbookAndSheetMetadata()
    {
        using var source = StreamFromString("""
            <report sheet="Generated">
              <row name="Alpha" amount="12.5"/>
              <row name="Beta" amount="7.25"/>
            </report>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet"
                xmlns:x="urn:schemas-microsoft-com:office:excel">
              <xsl:template match="/report">
                <ss:Workbook>
                  <ss:Names>
                    <ss:NamedRange ss:Name="GeneratedData" ss:RefersTo="=Generated!A1:B3"/>
                  </ss:Names>
                  <ss:Worksheet ss:Name="{@sheet}" ss:Visible="SheetHidden">
                    <ss:Table>
                      <ss:Column ss:Width="18.5"/>
                      <ss:Column ss:Index="3" ss:Hidden="1"/>
                      <ss:Row ss:Height="27.5">
                        <ss:Cell><ss:Data ss:Type="String">Name</ss:Data></ss:Cell>
                        <ss:Cell ss:Index="3"><ss:Data ss:Type="String">Amount</ss:Data></ss:Cell>
                      </ss:Row>
                      <xsl:for-each select="row">
                        <ss:Row ss:Index="{position() + 1}">
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@name"/></ss:Data></ss:Cell>
                          <ss:Cell ss:Index="3"><ss:Data ss:Type="Number"><xsl:value-of select="@amount"/></ss:Data></ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                      <ss:Row ss:Index="4" ss:Hidden="1">
                        <ss:Cell><ss:Data ss:Type="String">Hidden footer</ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                    <x:WorksheetOptions>
                      <x:DoNotDisplayGridlines/>
                      <x:Print>
                        <x:Gridlines/>
                      </x:Print>
                      <x:FreezePanes/>
                      <x:FrozenNoSplit/>
                      <x:SplitHorizontal>1</x:SplitHorizontal>
                      <x:TopRowBottomPane>1</x:TopRowBottomPane>
                      <x:SplitVertical>2</x:SplitVertical>
                      <x:LeftColumnRightPane>2</x:LeftColumnRightPane>
                    </x:WorksheetOptions>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Generated");
        sheet.IsHidden.Should().BeTrue();
        sheet.IsVeryHidden.Should().BeFalse();
        sheet.ShowGridlines.Should().BeFalse();
        sheet.PrintGridlines.Should().BeTrue();
        sheet.FrozenRows.Should().Be(1);
        sheet.FrozenCols.Should().Be(2);
        sheet.RowHeights[1].Should().Be(27.5);
        sheet.HiddenRows.Should().Contain(4u);
        sheet.ColumnWidths[1].Should().Be(18.5);
        sheet.HiddenCols.Should().Contain(3u);
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(3, 3)!.Value.Should().Be(new NumberValue(7.25));
        workbook.NamedRanges["GeneratedData"].Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2)));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromWorksheetVisibleAttributeValueTemplates()
    {
        using var source = StreamFromString("""
            <sheets>
              <sheet name="Hidden report" visible="SheetHidden"/>
              <sheet name="Audit stash" visible="SheetVeryHidden"/>
            </sheets>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/sheets">
                <ss:Workbook>
                  <xsl:for-each select="sheet">
                    <ss:Worksheet ss:Name="{@name}" ss:Visible="{@visible}">
                      <ss:Table>
                        <ss:Row><ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@name"/></ss:Data></ss:Cell></ss:Row>
                      </ss:Table>
                    </ss:Worksheet>
                  </xsl:for-each>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var hidden = workbook.GetSheetAt(0);
        var veryHidden = workbook.GetSheetAt(1);
        hidden.Name.Should().Be("Hidden report");
        hidden.IsHidden.Should().BeTrue();
        hidden.IsVeryHidden.Should().BeFalse();
        hidden.GetCell(1, 1)!.Value.Should().Be(new TextValue("Hidden report"));
        veryHidden.Name.Should().Be("Audit stash");
        veryHidden.IsHidden.Should().BeTrue();
        veryHidden.IsVeryHidden.Should().BeTrue();
        veryHidden.GetCell(1, 1)!.Value.Should().Be(new TextValue("Audit stash"));
    }

    [Fact]
    public void LoadTransformed_PreservesQuotedSpreadsheetMlNamedRanges()
    {
        using var source = StreamFromString("""
            <report sheet="Q1 Bob's Team">
              <row name="Alpha"/>
              <row name="Beta"/>
            </report>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/report">
                <ss:Workbook>
                  <ss:Names>
                    <ss:NamedRange ss:Name="TeamRows" ss:RefersTo="='Q1 Bob''s Team'!$A$1:$A$2"/>
                  </ss:Names>
                  <ss:Worksheet ss:Name="{@sheet}">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@name"/></ss:Data></ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Q1 Bob's Team");
        workbook.NamedRanges["TeamRows"].Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1)));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromNamedRangeAttributeValueTemplate()
    {
        using var source = StreamFromString("""
            <report sheet="Q1 Bob's Team" range="='Q1 Bob''s Team'!$A$1:$B$2">
              <row name="Alpha" amount="12.5"/>
              <row name="Beta" amount="7.25"/>
            </report>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
              <xsl:template match="/report">
                <ss:Workbook>
                  <ss:Names>
                    <ss:NamedRange ss:Name="GeneratedRows" ss:RefersTo="{@range}"/>
                  </ss:Names>
                  <ss:Worksheet ss:Name="{@sheet}">
                    <ss:Table>
                      <xsl:for-each select="row">
                        <ss:Row>
                          <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@name"/></ss:Data></ss:Cell>
                          <ss:Cell><ss:Data ss:Type="Number"><xsl:value-of select="@amount"/></ss:Data></ss:Cell>
                        </ss:Row>
                      </xsl:for-each>
                    </ss:Table>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Q1 Bob's Team");
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(7.25));
        workbook.NamedRanges["GeneratedRows"].Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2)));
    }

    [Fact]
    public void LoadTransformed_PreservesSpreadsheetMlGeneratedFromWorksheetOptionsDynamicValues()
    {
        using var source = StreamFromString("""
            <view sheet="Frozen report" rows="2" cols="3" showGridlines="false" printGridlines="true"/>
            """);
        using var stylesheet = StreamFromString("""
            <xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet"
                xmlns:x="urn:schemas-microsoft-com:office:excel">
              <xsl:template match="/view">
                <ss:Workbook>
                  <ss:Worksheet ss:Name="{@sheet}">
                    <ss:Table>
                      <ss:Row>
                        <ss:Cell><ss:Data ss:Type="String"><xsl:value-of select="@sheet"/></ss:Data></ss:Cell>
                      </ss:Row>
                    </ss:Table>
                    <x:WorksheetOptions>
                      <xsl:if test="@showGridlines = 'false'">
                        <x:DoNotDisplayGridlines/>
                      </xsl:if>
                      <xsl:if test="@printGridlines = 'true'">
                        <x:Print><x:Gridlines/></x:Print>
                      </xsl:if>
                      <x:FreezePanes/>
                      <x:FrozenNoSplit/>
                      <x:SplitHorizontal><xsl:value-of select="@rows"/></x:SplitHorizontal>
                      <x:TopRowBottomPane><xsl:value-of select="@rows"/></x:TopRowBottomPane>
                      <x:SplitVertical><xsl:value-of select="@cols"/></x:SplitVertical>
                      <x:LeftColumnRightPane><xsl:value-of select="@cols"/></x:LeftColumnRightPane>
                    </x:WorksheetOptions>
                  </ss:Worksheet>
                </ss:Workbook>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var workbook = SpreadsheetXmlFileAdapter.LoadTransformed(source, stylesheet);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Frozen report");
        sheet.ShowGridlines.Should().BeFalse();
        sheet.PrintGridlines.Should().BeTrue();
        sheet.FrozenRows.Should().Be(2);
        sheet.FrozenCols.Should().Be(3);
        sheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("Frozen report"));
    }

}
