using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class InCanvasRichClipboardTests
{
    [Fact]
    public void CaptureAndCodecRoundTrip_PreservesFieldDecorations()
    {
        var body = Body("2");
        body.Paragraphs[0].Runs[0].Field = new FieldRun
        {
            FieldType = "slidenum",
            CachedText = "2",
            Underline = true,
            UnderlineStyleToken = "wavyHeavy",
            Strikethrough = true,
            StrikeStyleToken = "dblStrike",
        };

        var payload = InCanvasRichClipboardPlanner.Capture(
            body,
            new InCanvasEditorTextSelection(0, 1));
        var decoded = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));
        var field = decoded!.Body.Paragraphs[0].Runs.Single().Field!;

        field.Underline.Should().BeTrue();
        field.UnderlineStyleToken.Should().Be("wavyHeavy");
        field.Strikethrough.Should().BeTrue();
        field.StrikeStyleToken.Should().Be("dblStrike");
    }

    [Fact]
    public void CaptureAndCodecRoundTrip_PreservesNativeRunMetadataSpacingAndDecoration()
    {
        var body = Body("Bonjour");
        body.Paragraphs[0].Runs[0].Language = "fr-FR";
        body.Paragraphs[0].Runs[0].AlternateLanguage = "en-US";
        body.Paragraphs[0].Runs[0].Kumimoji = true;
        body.Paragraphs[0].Runs[0].SmartTagClean = false;
        body.Paragraphs[0].Runs[0].NormalizeHeight = true;
        body.Paragraphs[0].Runs[0].CharacterSpacingHundredthsPt = -25;
        body.Paragraphs[0].Runs[0].KerningThresholdHundredthsPt = 1200;
        body.Paragraphs[0].Runs[0].Underline = true;
        body.Paragraphs[0].Runs[0].UnderlineStyleToken = "wavyHeavy";
        body.Paragraphs[0].Runs[0].Strikethrough = true;
        body.Paragraphs[0].Runs[0].StrikeStyleToken = "dblStrike";
        body.Paragraphs[0].Runs[0].Dirty = true;
        body.Paragraphs[0].Runs[0].NoProof = false;
        body.Paragraphs[0].Runs[0].Error = true;

        var payload = InCanvasRichClipboardPlanner.Capture(
            body,
            new InCanvasEditorTextSelection(0, 7));
        var decoded = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));

        decoded!.Body.Paragraphs[0].Runs.Single().Language.Should().Be("fr-FR");
        decoded.Body.Paragraphs[0].Runs.Single().AlternateLanguage.Should().Be("en-US");
        decoded.Body.Paragraphs[0].Runs.Single().Kumimoji.Should().BeTrue();
        decoded.Body.Paragraphs[0].Runs.Single().SmartTagClean.Should().BeFalse();
        decoded.Body.Paragraphs[0].Runs.Single().NormalizeHeight.Should().BeTrue();
        decoded.Body.Paragraphs[0].Runs.Single().CharacterSpacingHundredthsPt.Should().Be(-25);
        decoded.Body.Paragraphs[0].Runs.Single().KerningThresholdHundredthsPt.Should().Be(1200);
        decoded.Body.Paragraphs[0].Runs.Single().Underline.Should().BeTrue();
        decoded.Body.Paragraphs[0].Runs.Single().UnderlineStyleToken.Should().Be("wavyHeavy");
        decoded.Body.Paragraphs[0].Runs.Single().Strikethrough.Should().BeTrue();
        decoded.Body.Paragraphs[0].Runs.Single().StrikeStyleToken.Should().Be("dblStrike");
        decoded.Body.Paragraphs[0].Runs.Single().Dirty.Should().BeTrue();
        decoded.Body.Paragraphs[0].Runs.Single().NoProof.Should().BeFalse();
        decoded.Body.Paragraphs[0].Runs.Single().Error.Should().BeTrue();
    }

    [Fact]
    public void CaptureAndCodecRoundTrip_PreservesRunsListsSoftBreaksAndTypingStyle()
    {
        var source = RichBody();
        var typingRun = new Run
        {
            FontFamily = "Aptos Display",
            FontSizePt = 18,
            Bold = true,
            Underline = true,
            Color = new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79)),
        };

        var payload = InCanvasRichClipboardPlanner.Capture(
            source,
            new InCanvasEditorTextSelection(0, InCanvasTextEditPlanner.ExtractPlainText(source).Length),
            typingRun);
        var decoded = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));

        decoded.Should().NotBeNull();
        decoded!.PlainText.Should().Be("Alpha\nBeta\nGamma\nOmega");
        decoded.Body.Paragraphs.Should().HaveCount(3);
        decoded.Body.Paragraphs[0].BulletKind.Should().Be(BulletKind.Auto);
        decoded.Body.Paragraphs[0].Level.Should().Be(1);
        decoded.Body.Paragraphs[0].Runs.Select(run => run.Text)
            .Should().Equal("Alpha", "\n", "Beta");
        decoded.Body.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
        decoded.Body.Paragraphs[0].Runs[1].Italic.Should().BeTrue();
        decoded.Body.Paragraphs[1].BulletKind.Should().Be(BulletKind.Char);
        decoded.Body.Paragraphs[1].BulletChar.Should().Be("•");
        decoded.TypingRun!.FontFamily.Should().Be("Aptos Display");
        decoded.TypingRun.Underline.Should().BeTrue();
        decoded.TypingRun.Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
    }

    [Fact]
    public void Apply_PastesRichFragmentAtSelectionAndPreservesDestinationTypingStyle()
    {
        var source = RichBody();
        var payload = InCanvasRichClipboardPlanner.Capture(
            source,
            new InCanvasEditorTextSelection(0, InCanvasTextEditPlanner.ExtractPlainText(source).Length));
        var target = Body("BeforeAfter");
        var buffer = new InCanvasRichTextEditBuffer(target);
        buffer.SelectionAndApplyForTest(payload, 6, 6, out var caret);

        caret.Should().Be(6 + payload.PlainText.Length);
        buffer.PlainText.Should().Be("BeforeAlpha\nBeta\nGamma\nOmegaAfter");
        buffer.Body.Paragraphs.Should().HaveCount(3);
        buffer.Body.Paragraphs[0].BulletKind.Should().Be(BulletKind.Auto);
        buffer.Body.Paragraphs[0].Runs.Should().Contain(run => run.Bold);
        buffer.Body.Paragraphs[1].BulletKind.Should().Be(BulletKind.Char);
        buffer.Body.Paragraphs[1].Runs.Should().Contain(run => run.Text.Contains("Gamma"));
        buffer.Body.Paragraphs[2].Runs.Should().Contain(run => run.Text.Contains("Omega"));
    }

    [Fact]
    public void CaptureAndCodecRoundTrip_PreservesInlineTableAndNestedCellBodies()
    {
        var nested = new InlineTableInfo();
        nested.Table.ColumnWidthsEmu.Add(457200);
        nested.Table.Rows.Add(new TableRow
        {
            HorizontalAlignment = TableRowHorizontalAlignment.Right,
            Cells =
            {
                new TableCell
                {
                    TextBody = Body("Nested"),
                },
            },
        });
        var outer = new InlineTableInfo();
        outer.Table.ColumnWidthsEmu.AddRange([457200, 457200]);
        outer.Table.Rows.Add(new TableRow
        {
            HeightEmu = 304800,
            HeightRule = TableRowHeightRule.Exact,
            HorizontalAlignment = TableRowHorizontalAlignment.Center,
            Cells =
            {
                new TableCell { TextBody = Body("Outer") },
                new TableCell
                {
                    TextBody = new TextBody
                    {
                        Paragraphs =
                        {
                            new Paragraph
                            {
                                Runs =
                                {
                                    new Run { Text = "Cell " },
                                    new Run { Text = "\uFFFC", InlineTable = nested },
                                },
                            },
                        },
                    },
                },
            },
        });
        var source = new TextBody
        {
            Paragraphs =
            {
                new Paragraph
                {
                    Runs =
                    {
                        new Run { Text = "Before " },
                        new Run { Text = "\uFFFC", InlineTable = outer },
                        new Run { Text = " After" },
                    },
                },
            },
        };

        var payload = InCanvasRichClipboardPlanner.Capture(
            source,
            new InCanvasEditorTextSelection(0, source.Paragraphs[0].Runs.Sum(run => run.Text.Length)));
        var decoded = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));

        decoded.Should().NotBeNull();
        var decodedOuter = decoded!.Body.Paragraphs.Single().Runs[1].InlineTable;
        decodedOuter.Should().NotBeNull();
        decodedOuter!.Table.Rows[0].HeightRule.Should().Be(TableRowHeightRule.Exact);
        decodedOuter.Table.Rows[0].HorizontalAlignment.Should().Be(TableRowHorizontalAlignment.Center);
        decodedOuter!.Table.Rows[0].Cells[1].TextBody!.Paragraphs[0].Runs
            .Single(run => run.InlineTable is not null)
            .InlineTable!.Table.Rows[0].HorizontalAlignment.Should().Be(TableRowHorizontalAlignment.Right);
        decodedOuter.Table.Rows[0].Cells[1].TextBody!.Paragraphs[0].Runs
            .Single(run => run.InlineTable is not null)
            .InlineTable!.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text
            .Should().Be("Nested");
    }

    [Fact]
    public void Effects_RoundTripEveryModeledInlineEffectAndFillVariant()
    {
        var themed = new ThemeAwareColor(
            SrgbColor.FromRgb(0x336699),
            new SchemeColorRef
            {
                RoleName = "accent2",
                Slot = ThemeColorSlot.Accent2,
                LumMod = 0.72,
                LumOff = 0.08,
                Tint = 0.91,
                Shade = 0.83,
            },
            alpha: 0xC4);
        var gradient = new ShapeFill.Gradient(
            [
                new GradientStop(0.0, themed),
                new GradientStop(0.42, new ThemeAwareColor(SrgbColor.FromRgb(0xCC5500), 0xA0)),
                new GradientStop(1.0, ThemeAwareColor.White),
            ],
            GradientKind.Radial,
            angleDegrees: 37.5);
        var outlineGradient = new ShapeFill.Gradient(
            new ThemeAwareColor(SrgbColor.FromRgb(0x102030)),
            new ThemeAwareColor(SrgbColor.FromRgb(0x908070)),
            angleDegrees: 123.25);
        var allEffects = new Run
        {
            Text = "all-effects",
            TextFill = gradient,
            TextOutline = new ShapeOutline.GradientVisible(
                outlineGradient,
                widthPt: 2.25,
                dash: OutlineDash.LongDashDot,
                beginLineEnd: new ShapeLineEnd(ShapeLineEndKind.Triangle),
                endLineEnd: new ShapeLineEnd(ShapeLineEndKind.Triangle)),
            TextShadow = new RunTextShadow
            {
                Color = themed,
                Alpha = 0x71,
                BlurPt = 4.25,
                DistPt = 3.5,
                DirDeg = 217.0,
            },
            TextReflection = new RunTextReflection
            {
                Alpha = 0x63,
                BlurPt = 1.75,
                DistPt = 2.5,
                DirDeg = 89.0,
                ScaleY = -0.64,
                EndPos = 0.81,
            },
            TextGlow = new RunTextGlow
            {
                Color = new ThemeAwareColor(SrgbColor.FromRgb(0xF0C000), 0xB2),
                Alpha = 0x92,
                RadiusPt = 7.25,
            },
            TextSoftEdge = new RunTextSoftEdge { RadiusPt = 2.75 },
        };
        var solid = new Run
        {
            Text = "solid",
            TextFill = new ShapeFill.Solid(themed),
            TextOutline = new ShapeOutline.Visible(
                themed,
                widthPt: 1.2,
                dash: OutlineDash.DashDot,
                beginLineEnd: new ShapeLineEnd(ShapeLineEndKind.Triangle)),
        };
        var picture = new Run
        {
            Text = "picture",
            TextFill = new ShapeFill.Picture([1, 2, 3, 4], "image/png", tile: true),
        };
        var pattern = new Run
        {
            Text = "pattern",
            TextFill = new ShapeFill.Pattern(
                "diagStripe",
                themed,
                new ThemeAwareColor(SrgbColor.FromRgb(0xEFEFEF), 0x55)),
        };
        var none = new Run
        {
            Text = "none",
            TextFill = ShapeFill.None.Instance,
            TextOutline = ShapeOutline.None.Instance,
        };
        var source = new TextBody();
        source.Paragraphs.Add(new Paragraph { Runs = { allEffects, solid, picture, pattern, none } });

        var payload = InCanvasRichClipboardPlanner.Capture(
            source,
            new InCanvasEditorTextSelection(0, InCanvasTextEditPlanner.ExtractPlainText(source).Length));
        var decoded = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));

        decoded.Should().NotBeNull();
        var runs = decoded!.Body.Paragraphs.Single().Runs;
        var decodedAll = runs.Single(run => run.Text == "all-effects");
        var decodedFill = decodedAll.TextFill.Should().BeOfType<ShapeFill.Gradient>().Which;
        decodedFill.Kind.Should().Be(GradientKind.Radial);
        decodedFill.AngleDegrees.Should().BeApproximately(37.5, 0.0001);
        decodedFill.Stops.Should().HaveCount(3);
        decodedFill.Stops[0].Position.Should().Be(0.0);
        decodedFill.Stops[0].Color.Alpha.Should().Be(0xC4);
        var decodedScheme = decodedFill.Stops[0].Color.SchemeColor;
        decodedScheme.Should().NotBeNull();
        decodedScheme!.RoleName.Should().Be("accent2");
        decodedScheme.Tint.Should().BeApproximately(0.91, 0.0001);

        var decodedOutline = decodedAll.TextOutline
            .Should().BeOfType<ShapeOutline.GradientVisible>().Which;
        decodedOutline.WidthPt.Should().BeApproximately(2.25, 0.0001);
        decodedOutline.Dash.Should().Be(OutlineDash.LongDashDot);
        decodedOutline.Gradient.Kind.Should().Be(GradientKind.Linear);
        decodedOutline.BeginLineEnd!.Kind.Should().Be(ShapeLineEndKind.Triangle);
        decodedOutline.EndLineEnd!.Kind.Should().Be(ShapeLineEndKind.Triangle);

        decodedAll.TextShadow.Should().BeEquivalentTo(new RunTextShadow
        {
            Color = themed,
            Alpha = 0x71,
            BlurPt = 4.25,
            DistPt = 3.5,
            DirDeg = 217.0,
        });
        decodedAll.TextReflection.Should().BeEquivalentTo(new RunTextReflection
        {
            Alpha = 0x63,
            BlurPt = 1.75,
            DistPt = 2.5,
            DirDeg = 89.0,
            ScaleY = -0.64,
            EndPos = 0.81,
        });
        decodedAll.TextGlow.Should().BeEquivalentTo(new RunTextGlow
        {
            Color = new ThemeAwareColor(SrgbColor.FromRgb(0xF0C000), 0xB2),
            Alpha = 0x92,
            RadiusPt = 7.25,
        });
        decodedAll.TextSoftEdge!.RadiusPt.Should().BeApproximately(2.75, 0.0001);

        runs.Single(run => run.Text == "solid").TextOutline
            .Should().BeOfType<ShapeOutline.Visible>().Which.BeginLineEnd!.Kind
            .Should().Be(ShapeLineEndKind.Triangle);
        var decodedPicture = runs.Single(run => run.Text == "picture").TextFill
            .Should().BeOfType<ShapeFill.Picture>().Which;
        decodedPicture.ImageBytes.Should().Equal(1, 2, 3, 4);
        decodedPicture.ContentType.Should().Be("image/png");
        decodedPicture.Tile.Should().BeTrue();
        runs.Single(run => run.Text == "pattern").TextFill
            .Should().BeOfType<ShapeFill.Pattern>().Which.Preset.Should().Be("diagStripe");
        runs.Single(run => run.Text == "none").TextFill.Should().BeSameAs(ShapeFill.None.Instance);
        runs.Single(run => run.Text == "none").TextOutline.Should().BeSameAs(ShapeOutline.None.Instance);

        ((ShapeFill.Picture)picture.TextFill!).ImageBytes[0] = 99;
        decodedPicture.ImageBytes[0].Should().Be(1);
    }

    [Fact]
    public void Version1Payload_RemainsReadableWithoutEffectFields()
    {
        var payload = InCanvasRichClipboardPayload.FromPlainText("legacy");
        var json = System.Text.Json.Nodes.JsonNode.Parse(
            InCanvasRichClipboardPlanner.Serialize(payload))!.AsObject();
        json["Version"] = 1;

        var decoded = InCanvasRichClipboardPlanner.Deserialize(
            System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));

        decoded.Should().NotBeNull();
        decoded!.PlainText.Should().Be("legacy");
        decoded.Body.Paragraphs.Single().Runs.Single().Text.Should().Be("legacy");
    }

    [Fact]
    public void TableGeometryAndStyles_SurviveInternalClipboardCodec()
    {
        var payload = new InCanvasRichClipboardPayload(
            new TextBody
            {
                Paragraphs =
                {
                    new Paragraph { Runs = { new Run { Text = "A\tB" } } },
                },
            },
            "A\tB",
            ContainsTable: true,
            TableColumnWidthsEmu: [914400L, 1828800L],
            TableCellStyles:
            [
                new InCanvasRichClipboardTableCellStyle(
                    FillRgb: 0xFFFF00,
                    Left: new InCanvasRichClipboardTableBorder(0x1F4E79, 0.5),
                    Anchor: TableCellAnchor.Middle,
                    InsetLeftPt: 6,
                    InsetRightPt: 12,
                    HorizontalMergeStart: true,
                    VerticalMergeContinuation: true,
                    FillPattern: "horzStripe",
                    FillForegroundRgb: 0x1F4E79,
                    FillBackgroundRgb: 0xFFFFFF),
                new InCanvasRichClipboardTableCellStyle(),
            ]);

        var decoded = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));

        decoded.Should().NotBeNull();
        decoded!.ContainsTable.Should().BeTrue();
        decoded.TableColumnWidthsEmu.Should().Equal(914400L, 1828800L);
        decoded.TableCellStyles.Should().HaveCount(2);
        decoded.TableCellStyles![0].FillRgb.Should().Be(0xFFFF00);
        decoded.TableCellStyles[0].Left!.WidthPt.Should().Be(0.5);
        decoded.TableCellStyles[0].Anchor.Should().Be(TableCellAnchor.Middle);
        decoded.TableCellStyles[0].InsetLeftPt.Should().Be(6);
        decoded.TableCellStyles[0].InsetRightPt.Should().Be(12);
        decoded.TableCellStyles[0].HorizontalMergeStart.Should().BeTrue();
        decoded.TableCellStyles[0].VerticalMergeContinuation.Should().BeTrue();
        decoded.TableCellStyles[0].FillPattern.Should().Be("horzStripe");
        decoded.TableCellStyles[0].FillForegroundRgb.Should().Be(0x1F4E79);
        decoded.TableCellStyles[0].FillBackgroundRgb.Should().Be(0xFFFFFF);
    }

    [Fact]
    public void PlainTextFallback_CreatesParagraphsAndUsesTypingStyle()
    {
        var payload = InCanvasRichClipboardPayload.FromPlainText(
            "one\r\ntwo",
            new InCanvasEditorTextStyleState(
                "Calibri", 12, true, false, false, false, null));

        payload.PlainText.Should().Be("one\ntwo");
        payload.Body.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Text)
            .Should().Equal("one", "two");
        payload.TypingRun!.FontFamily.Should().Be("Calibri");
        payload.TypingRun.Bold.Should().BeTrue();
    }

    private static TextBody RichBody()
    {
        var body = new TextBody { DefaultParaAlign = TextAlign.Left };
        body.Paragraphs.Add(new Paragraph
        {
            Level = 1,
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.RomanUcPeriod,
            AutoNumStartAt = 3,
            AutoNumStartAtSpecified = true,
            Runs =
            {
                new Run
                {
                    Text = "Alpha",
                    FontFamily = "Aptos",
                    FontSizePt = 16,
                    Bold = true,
                    BoldSet = true,
                    Color = new ThemeAwareColor(SrgbColor.FromRgb(0xC00000)),
                },
                new Run { Text = "\n", Italic = true, ItalicSet = true },
                new Run { Text = "Beta", Underline = true, Strikethrough = true },
            },
        });
        body.Paragraphs.Add(new Paragraph
        {
            Level = 2,
            BulletKind = BulletKind.Char,
            BulletChar = "•",
            Align = TextAlign.Right,
            Runs = { new Run { Text = "Gamma", Hyperlink = new Hyperlink { Url = "https://example.test" } } },
        });
        body.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Omega" } } });
        return body;
    }

    private static TextBody Body(string text) =>
        InCanvasRichClipboardPayload.FromPlainText(text).Body;
}

internal static class InCanvasRichTextEditBufferTestExtensions
{
    internal static void SelectionAndApplyForTest(
        this InCanvasRichTextEditBuffer buffer,
        InCanvasRichClipboardPayload payload,
        int start,
        int end,
        out int caret) =>
        buffer.ApplyClipboardPayload(
            payload,
            new InCanvasEditorTextSelection(start, end),
            out caret);
}
