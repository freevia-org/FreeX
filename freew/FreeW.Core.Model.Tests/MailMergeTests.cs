namespace FreeW.Core.Model.Tests;

public class MailMergeTests
{
    [Theory]
    [InlineData("FirstName", " MERGEFIELD FirstName \\* MERGEFORMAT ")]
    [InlineData("Postal Code", " MERGEFIELD \"Postal Code\" \\* MERGEFORMAT ")]
    [InlineData("«City»", " MERGEFIELD City \\* MERGEFORMAT ")]
    public void BuildMergeFieldInstruction_UsesNativeWordSyntax(string name, string expected)
    {
        MailMerge.BuildMergeFieldInstruction(name).Should().Be(expected);
    }

    [Fact]
    public void FieldNames_FromText_AreDistinctInFirstAppearanceOrder()
    {
        const string text = "Dear «First» «Last», your code is «First».";

        var names = MailMerge.FieldNames(text);

        names.Should().Equal("First", "Last");
    }

    [Fact]
    public void FieldNames_AreDistinctCaseInsensitively_FirstSpellingWins()
    {
        const string text = "«City» then «city» then «CITY»";

        MailMerge.FieldNames(text).Should().Equal("City");
    }

    [Fact]
    public void FieldNames_TrimsWhitespaceAndIgnoresEmptyPlaceholders()
    {
        const string text = "«  Name  » and «» and «   »";

        MailMerge.FieldNames(text).Should().Equal("Name");
    }

    [Fact]
    public void FieldNames_FromDocument_ScansParagraphsAndTableCells()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("Hello «Name», "));
        para.Runs.Add(new Run("from «Company»"));
        doc.Blocks.Add(para);

        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("«Item»"));
        table.Rows[0].Cells[1].Paragraphs[0].Runs.Add(new Run("«Name»")); // duplicate, dropped
        doc.Blocks.Add(table);

        MailMerge.FieldNames(doc).Should().Equal("Name", "Company", "Item");
    }

    [Fact]
    public void FieldNames_FromDocument_CombinesNativeAndLegacyMergeFields()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(" MERGEFIELD FirstName \\* MERGEFORMAT ", "«FirstName»"),
                new Run(" «City» "),
                Run.ComplexFieldRun(" MERGEFIELD \"Postal Code\" ", "«Postal Code»")
            }
        });

        MailMerge.FieldNames(doc).Should().Equal("FirstName", "City", "Postal Code");
    }

    [Fact]
    public void FieldNames_NativeInstructionOverridesStaleCachedLabel()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" MERGEFIELD FirstName ", "«OldName»") }
        });

        MailMerge.FieldNames(doc).Should().Equal("FirstName");
    }

    [Fact]
    public void FieldNames_FromDocument_ScansDrawingAndAnnotationStories()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph("Body «BodyField»");
        paragraph.Runs.Add(Run.FromShape(Shape.TextBoxWith("Shape «ShapeField»", 80, 30)));
        paragraph.Runs.Add(Run.FromWordArt(new WordArt("Art «ArtField»")));
        var chart = Chart.Create(
            ChartKind.Column,
            ["«CategoryField»"],
            [1d],
            seriesName: "«SeriesField»",
            title: "«ChartTitleField»");
        chart.CategoryAxisTitle = "«CategoryAxisField»";
        chart.ValueAxisTitle = "«ValueAxisField»";
        paragraph.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(paragraph);
        doc.Header = new HeaderFooter("Header «HeaderField»");
        doc.Footnotes[1] = new Footnote(1, "Foot «FootField»");
        doc.Endnotes[2] = new Endnote(2, "End «EndField»");
        doc.Comments[3] = new Comment(3, "Comment «CommentField»");

        MailMerge.FieldNames(doc).Should().Equal(
            "BodyField",
            "ShapeField",
            "ArtField",
            "ChartTitleField",
            "CategoryAxisField",
            "ValueAxisField",
            "CategoryField",
            "SeriesField",
            "HeaderField",
            "FootField",
            "EndField",
            "CommentField");
    }

    [Fact]
    public void Substitute_ReplacesPresentField()
    {
        var row = new Dictionary<string, string> { ["Name"] = "Ada" };

        MailMerge.Substitute("Hello «Name»!", row).Should().Be("Hello Ada!");
    }

    [Fact]
    public void Substitute_MissingField_BecomesEmptyString()
    {
        var row = new Dictionary<string, string> { ["Name"] = "Ada" };

        MailMerge.Substitute("Hi «Name» «Title»!", row).Should().Be("Hi Ada !");
    }

    [Fact]
    public void Substitute_IsCaseInsensitive_WhenDictionarySupportsIt()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["name"] = "Ada" };

        MailMerge.Substitute("Hello «Name»!", row).Should().Be("Hello Ada!");
    }

    [Fact]
    public void Substitute_UnterminatedDelimiter_NoClosing_IsLeftLiteral()
    {
        var row = new Dictionary<string, string> { ["Name"] = "Ada" };

        // No closing » anywhere → the opening « and the rest are emitted verbatim.
        MailMerge.Substitute("Hello «Name and the rest", row)
            .Should().Be("Hello «Name and the rest");
    }

    [Fact]
    public void Substitute_NoPlaceholders_ReturnsInputUnchanged()
    {
        MailMerge.Substitute("plain text", new Dictionary<string, string>())
            .Should().Be("plain text");
    }

    [Fact]
    public void MergeRecord_SubstitutesEveryRun_AndDoesNotMutateTemplate()
    {
        var template = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("Dear ", new RunFormatting { Bold = true }));
        para.Runs.Add(new Run("«First» «Last»"));
        template.Blocks.Add(para);

        var row = new Dictionary<string, string> { ["First"] = "Grace", ["Last"] = "Hopper" };
        var merged = MailMerge.MergeRecord(template, row);

        merged.PlainText.Should().Be("Dear Grace Hopper");
        // The bold formatting on the leading run is preserved.
        var firstRun = ((Paragraph)merged.Blocks[0]).Runs[0];
        firstRun.Formatting.Bold.Should().BeTrue();
        // Template is untouched.
        template.PlainText.Should().Be("Dear «First» «Last»");
    }

    [Fact]
    public void MergeRecord_ResolvesNativeMergeFieldToPlainResult()
    {
        var native = Run.ComplexFieldRun(
            " MERGEFIELD \"First Name\" \\* MERGEFORMAT ",
            "«First Name»");
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph { Runs = { new Run("Dear "), native } });

        var merged = MailMerge.MergeRecord(
            template,
            new Dictionary<string, string> { ["First Name"] = "Ada" });

        var mergedRun = ((Paragraph)merged.Blocks[0]).Runs[1];
        merged.PlainText.Should().Be("Dear Ada");
        mergedRun.ComplexField.Should().BeNull("Finish & Merge materializes recipient values as text");
        native.ComplexField.Should().NotBeNull("the editable template remains a native MERGEFIELD");
    }

    [Fact]
    public void MergeRecord_DoesNotInterpretGuillemetsInsideNativeRecipientValue()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" MERGEFIELD Note ", "«Note»") }
        });
        var row = new Dictionary<string, string>
        {
            ["Note"] = "Use «City»",
            ["City"] = "London"
        };

        MailMerge.MergeRecord(template, row).PlainText.Should().Be("Use «City»");
        MailMerge.MergeRecordWithRules(template, row, new MergeState(), recordIndex: 1)
            .PlainText.Should().Be("Use «City»");
    }

    [Theory]
    [InlineData("Ada", "[Ada]")]
    [InlineData("", "")]
    public void MergeRecord_AppliesNativeConditionalBeforeAndAfterText(string value, string expected)
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" MERGEFIELD Name \\b \"[\" \\f \"]\" ", "«Name»") }
        });
        var row = new Dictionary<string, string> { ["Name"] = value };

        MailMerge.MergeRecord(template, row).PlainText.Should().Be(expected);
        MailMerge.MergeRecordWithRules(template, row, new MergeState(), recordIndex: 1)
            .PlainText.Should().Be(expected);
    }

    [Theory]
    [InlineData("Upper", "ADA LOVELACE")]
    [InlineData("Lower", "ada lovelace")]
    [InlineData("FirstCap", "Ada LOVELACE")]
    [InlineData("Caps", "Ada LOVELACE")]
    public void MergeRecord_AppliesNativeGeneralTextFormat(string format, string expected)
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(
                    $" MERGEFIELD Name \\* {format} \\* MERGEFORMAT ",
                    "«Name»")
            }
        });
        var row = new Dictionary<string, string> { ["Name"] = "ada LOVELACE" };

        MailMerge.MergeRecord(template, row).PlainText.Should().Be(expected);
        MailMerge.MergeRecordWithRules(template, row, new MergeState(), recordIndex: 1)
            .PlainText.Should().Be(expected);
    }

    [Fact]
    public void MergeRecord_GeneralFormatIncludesConditionalText()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(
                    " MERGEFIELD Name \\b \"pre-\" \\f \"-post\" \\* Upper \\* MERGEFORMAT ",
                    "«Name»")
            }
        });

        var merged = MailMerge.MergeRecord(
            template,
            new Dictionary<string, string> { ["Name"] = "ada LOVELACE" });

        merged.PlainText.Should().Be("PRE-ADA LOVELACE-POST");
    }

    [Theory]
    [InlineData("27", "Arabic", "27")]
    [InlineData("12.5", "Arabic", "13")]
    [InlineData("27", "ROMAN", "XXVII")]
    [InlineData("27", "roman", "xxvii")]
    [InlineData("4000", "ROMAN", "MMMM")]
    [InlineData("0", "ROMAN", "")]
    [InlineData("-3", "ROMAN", "Error! Number cannot be represented in specified format.")]
    [InlineData("32768", "ROMAN", "Error! Number cannot be represented in specified format.")]
    [InlineData("27", "ALPHABETIC", "AA")]
    [InlineData("27", "alphabetic", "aa")]
    [InlineData("0", "ALPHABETIC", "")]
    [InlineData("-3", "ALPHABETIC", "Error! Number cannot be represented in specified format.")]
    [InlineData("703", "ALPHABETIC", "AAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("781", "ALPHABETIC", "Error! Number cannot be represented in specified format.")]
    [InlineData("27", "Hex", "1B")]
    [InlineData("0", "Hex", "0")]
    [InlineData("-3", "Hex", "Error! Number cannot be represented in specified format.")]
    [InlineData("65535", "Hex", "FFFF")]
    [InlineData("65536", "Hex", "Error! Number cannot be represented in specified format.")]
    [InlineData("-21", "Ordinal", "-21st")]
    [InlineData("12.5", "Ordinal", "13th")]
    [InlineData("0", "OrdText", "zeroth")]
    [InlineData("-3", "OrdText", "Error! Number cannot be represented in specified format.")]
    [InlineData("1234", "OrdText", "one thousand two hundred thirty-fourth")]
    [InlineData("0", "CardText", "zero")]
    [InlineData("999999", "CardText", "nine hundred ninety-nine thousand nine hundred ninety-nine")]
    [InlineData("1000000", "CardText", "Error! Number cannot be represented in specified format.")]
    [InlineData("0", "DollarText", "zero and 00/100")]
    [InlineData("999999.5", "DollarText", "nine hundred ninety-nine thousand nine hundred ninety-nine and 50/100")]
    [InlineData("12.005", "DollarText", "twelve and 01/100")]
    [InlineData("12.995", "DollarText", "twelve and 00/100")]
    [InlineData("abc", "CardText", "abc")]
    public void MergeRecord_AppliesNativeGeneralNumericFormat(
        string value,
        string format,
        string expected)
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(
                    $" MERGEFIELD Value \\* {format} \\* MERGEFORMAT ",
                    "Â«ValueÂ»")
            }
        });
        var row = new Dictionary<string, string> { ["Value"] = value };

        MailMerge.MergeRecord(template, row).PlainText.Should().Be(expected);
        MailMerge.MergeRecordWithRules(template, row, new MergeState(), recordIndex: 1)
            .PlainText.Should().Be(expected);
    }

    [Fact]
    public void MergeRecord_LoneGeneralNumericFormatSuppressesConditionalTextLikeWord()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(
                    " MERGEFIELD Value \\b \"[\" \\f \"]\" \\* Roman ",
                    "Â«ValueÂ»")
            }
        });
        var row = new Dictionary<string, string> { ["Value"] = "27" };

        MailMerge.MergeRecord(template, row).PlainText.Should().Be("XXVII");
        MailMerge.MergeRecordWithRules(template, row, new MergeState(), recordIndex: 1)
            .PlainText.Should().Be("XXVII");
    }

    [Fact]
    public void MergeRecord_NonnumericGeneralNumericFormatKeepsConditionalText()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(
                    " MERGEFIELD Value \\b \"[\" \\f \"]\" \\* Roman ",
                    "Â«ValueÂ»")
            }
        });
        var row = new Dictionary<string, string> { ["Value"] = "abc" };

        MailMerge.MergeRecord(template, row).PlainText.Should().Be("[abc]");
        MailMerge.MergeRecordWithRules(template, row, new MergeState(), recordIndex: 1)
            .PlainText.Should().Be("[abc]");
    }

    [Fact]
    public void MergeRecord_CombinedGeneralFormatsSuppressPunctuationOnlyConditionalText()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(
                    " MERGEFIELD Value \\b \"[\" \\f \"]\" \\* Roman \\* Upper ",
                    "Â«ValueÂ»")
            }
        });
        var row = new Dictionary<string, string> { ["Value"] = "27" };

        MailMerge.MergeRecord(template, row).PlainText.Should().Be("XXVII");
        MailMerge.MergeRecordWithRules(template, row, new MergeState(), recordIndex: 1)
            .PlainText.Should().Be("XXVII");
    }

    [Fact]
    public void MergeRecord_CombinedGeneralFormatsProcessConditionalTextInOrder()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(
                    " MERGEFIELD Value \\b \"pre-\" \\f \"-post\" \\* CardText \\* Upper ",
                    "Â«ValueÂ»")
            }
        });
        var row = new Dictionary<string, string> { ["Value"] = "27" };

        MailMerge.MergeRecord(template, row).PlainText.Should().Be("PRE-27-POST");
        MailMerge.MergeRecordWithRules(template, row, new MergeState(), recordIndex: 1)
            .PlainText.Should().Be("PRE-27-POST");
    }

    [Fact]
    public void MergeRecord_CapsUsesWordPunctuationBoundariesButKeepsApostrophesInternal()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" MERGEFIELD Name \\* Caps ", "«Name»") }
        });

        var merged = MailMerge.MergeRecord(
            template,
            new Dictionary<string, string>
            {
                ["Name"] = "ada-lovelace ada/lovelace o'connor"
            });

        merged.PlainText.Should().Be("Ada-Lovelace Ada/Lovelace O'connor");
    }

    [Theory]
    [InlineData("1234.5", "$#,##0.00", "$1,234.50")]
    [InlineData("12.5", "$#,##0.00", "$  12.50")]
    [InlineData("0.125", "0.0%", "0.1%")]
    [InlineData("abc", "0.00", "abc")]
    [InlineData("1234.5", "x##", "1234.5")]
    [InlineData("-12.5", "$#,##0.00", "-12.5")]
    [InlineData("1234.5", "0", "1235")]
    [InlineData("1234.5", "0.00", "1234.50")]
    [InlineData("1234.5", "#,##0", "1,235")]
    [InlineData("1234.5", "#,##0.00", "1,234.50")]
    [InlineData("1234.5", "000000", "001235")]
    [InlineData("-1234.5", "#,##0.00", "-1,234.50")]
    [InlineData("1234.5", "$#,##0.00;($#,##0.00)", "$1,234.50")]
    [InlineData("-1234.5", "$#,##0.00;($#,##0.00)", "($1,234.50)")]
    [InlineData("1234.5", "0.00;-0.00;ZERO", "1234.50")]
    [InlineData("-1234.5", "0.00;-0.00;ZERO", "-1234.50")]
    [InlineData("0", "0.00;-0.00;ZERO", "ZERO")]
    public void MergeRecord_AppliesNativeNumericPicture(string value, string picture, string expected)
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(
                    $" MERGEFIELD Value \\# \"{picture}\" \\* MERGEFORMAT ",
                    "«Value»")
            }
        });

        var row = new Dictionary<string, string> { ["Value"] = value };

        MailMerge.MergeRecord(template, row).PlainText.Should().Be(expected);
        MailMerge.MergeRecordWithRules(template, row, new MergeState(), recordIndex: 1)
            .PlainText.Should().Be(expected);
    }

    [Theory]
    [InlineData("8/6/2026 2:05 PM", "MMMM d, yyyy", "August 6, 2026")]
    [InlineData("8/6/2026 2:05 PM", "MM/dd/yyyy", "08/06/2026")]
    [InlineData("8/6/2026 2:05 PM", "yyyy-MM-dd", "2026-08-06")]
    [InlineData("8/6/2026 2:05 PM", "h:mm AM/PM", "2:05 PM")]
    [InlineData("8/6/2026 2:05 PM", "M/d/yyyy", "8/6/2026")]
    [InlineData("8/6/2026 2:05 PM", "dddd", "Thursday")]
    [InlineData("8/6/2026 2:05 PM", "h:mm am/pm", "2:05 PM")]
    [InlineData("8/6/2026 2:05 PM", "dd.MM.yyyy", "06.08.2026")]
    [InlineData("8/6/2026 2:05 PM", "d", "6")]
    [InlineData("8/6/2026 2:05 PM", "m", "5")]
    [InlineData("8/6/2026 2:05 PM", "h", "2")]
    [InlineData("8/6/2026 2:05 PM", "MMMM d, yyyy 'at' h:mm AM/PM", "August 6, 2026 at 2:05 PM")]
    [InlineData("not-a-date", "yyyy-MM-dd", "not-a-date")]
    public void MergeRecord_AppliesCalibratedNativeDatePicture(
        string value,
        string picture,
        string expected)
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(
                    $" MERGEFIELD When \\@ \"{picture}\" \\* MERGEFORMAT ",
                    "«When»",
                    formatting: new RunFormatting { LanguageTag = "en-US" })
            }
        });
        var row = new Dictionary<string, string> { ["When"] = value };

        MailMerge.MergeRecord(template, row).PlainText.Should().Be(expected);
        MailMerge.MergeRecordWithRules(template, row, new MergeState(), recordIndex: 1)
            .PlainText.Should().Be(expected);
    }

    [Fact]
    public void MergeRecord_DatePictureUsesRunLanguageForParsingAndNames()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(
                    " MERGEFIELD When \\@ \"dddd, d. MMMM yyyy\" ",
                    "«When»",
                    formatting: new RunFormatting { LanguageTag = "de-DE" })
            }
        });

        var merged = MailMerge.MergeRecord(
            template,
            new Dictionary<string, string> { ["When"] = "06.08.2026" });

        merged.PlainText.Should().Be("Donnerstag, 6. August 2026");
    }

    [Fact]
    public void NativeCompositeFields_AutoMapAndResolveInBothMergePaths()
    {
        var template = TextDocument.CreateEmpty();
        template.Blocks.Clear();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(MailMerge.AddressBlockInstruction, "«AddressBlock»"),
                new Run("|"),
                Run.ComplexFieldRun(MailMerge.GreetingLineInstruction, "«GreetingLine»")
            }
        });
        var rows = new[]
        {
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Title"] = "Dr.", ["FirstName"] = "Ada", ["MiddleName"] = "M.",
                ["LastName"] = "Lovelace", ["Suffix"] = "PhD", ["Company"] = "Analytical Engines",
                ["Address1"] = "1 Algorithm Way", ["Address2"] = "Suite 2", ["City"] = "London",
                ["State"] = "CA", ["PostalCode"] = "12345", ["Country"] = "United Kingdom"
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["FirstName"] = "Grace", ["LastName"] = "Hopper", ["Address1"] = "2 Compiler Rd",
                ["City"] = "Arlington", ["State"] = "VA", ["PostalCode"] = "22201",
                ["Country"] = "United States"
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Address1"] = "3 Anonymous Ave", ["City"] = "Nowhere", ["State"] = "NY",
                ["PostalCode"] = "10000", ["Country"] = "United States"
            }
        };
        var expected = new[]
        {
            "Dr. Ada Lovelace PhD\nAnalytical Engines\n1 Algorithm Way\nSuite 2\nLondon, CA 12345\nUnited Kingdom|Dear Dr. Lovelace,",
            "Grace Hopper\n2 Compiler Rd\nArlington, VA 22201\nUnited States|Dear Grace Hopper,",
            "\n3 Anonymous Ave\nNowhere, NY 10000\nUnited States|Dear Sir or Madam,"
        };

        for (var i = 0; i < rows.Length; i++)
        {
            MailMerge.MergeRecord(template, rows[i]).PlainText.Should().Be(expected[i]);
            MailMerge.MergeRecordWithRules(template, rows[i], new MergeState(), recordIndex: i + 1)
                .PlainText.Should().Be(expected[i]);
        }
    }

    [Fact]
    public void NativeCompositeFields_PreferExplicitSessionValues()
    {
        var template = TextDocument.CreateEmpty();
        template.Blocks.Clear();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(MailMerge.AddressBlockInstruction, "«AddressBlock»"),
                new Run("|"),
                Run.ComplexFieldRun(MailMerge.GreetingLineInstruction, "«GreetingLine»")
            }
        });
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FirstName"] = "Ignored",
            ["AddressBlock"] = "Mapped address",
            ["GreetingLine"] = "Hello mapped recipient!"
        };

        MailMerge.MergeRecord(template, row).PlainText
            .Should().Be("Mapped address|Hello mapped recipient!");
    }

    [Theory]
    [InlineData(" ADDRESSBLOCK \\f \"custom\" \\* MERGEFORMAT ", "«AddressBlock»")]
    [InlineData(" GREETINGLINE \\f \"<<_BEFORE_ Hello >><<_LAST0_>>\" \\e \"Hello!\" \\l 1033 ", "«GreetingLine»")]
    [InlineData(" GREETINGLINE \\f \"<<_BEFORE_ Dear >><<_TITLE0_ >><<_LAST0_>><<_AFTER_ ,>>\" \\e \"Dear Sir or Madam,\" \\l 1033 \\x extra ", "«GreetingLine»")]
    [InlineData(" GREETINGLINE \\f \"<<_BEFORE_ DEAR >><<_TITLE0_ >><<_LAST0_>><<_AFTER_ ,>>\" \\e \"dear sir or madam,\" \\l 1033 ", "«GreetingLine»")]
    public void NativeCompositeFields_PreserveUnsupportedCustomInstructions(
        string instruction,
        string cached)
    {
        var template = TextDocument.CreateEmpty();
        template.Blocks.Clear();
        template.Blocks.Add(new Paragraph { Runs = { Run.ComplexFieldRun(instruction, cached) } });

        var merged = MailMerge.MergeRecord(
            template,
            new Dictionary<string, string> { ["LastName"] = "Lovelace" });

        var run = merged.Blocks.OfType<Paragraph>().Single().Runs.Single();
        run.Text.Should().Be(cached);
        run.ComplexField.Should().NotBeNull();
    }

    [Fact]
    public void MergeRecordWithRules_ResolvesNativeMergeFieldAlongsideRules()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(" MERGEFIELD City \\* MERGEFORMAT ", "«City»"),
                new Run(" / «If City = \"London\" Then \"UK\" Else \"Other\"»")
            }
        });

        var merged = MailMerge.MergeRecordWithRules(
            template,
            new Dictionary<string, string> { ["City"] = "London" },
            new MergeState(),
            recordIndex: 1);

        merged.PlainText.Should().Be("London / UK");
        ((Paragraph)merged.Blocks[0]).Runs[0].ComplexField.Should().BeNull();
    }

    [Fact]
    public void MergeRecord_SubstitutesInsideTableCells()
    {
        var template = new TextDocument();
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("«Item»"));
        template.Blocks.Add(table);

        var merged = MailMerge.MergeRecord(template,
            new Dictionary<string, string> { ["Item"] = "Widget" });

        var cell = ((Table)merged.Blocks[0]).Rows[0].Cells[0];
        cell.PlainText.Should().Be("Widget");
    }

    [Fact]
    public void MergeRecord_SubstitutesAllSectionHeaderFooterStoriesAndPreservesPageSettings()
    {
        var template = new TextDocument();
        template.Page.DifferentFirstPage = true;
        template.Page.DifferentOddEvenPages = true;
        template.Page.HeaderDistancePt = 24;
        template.Page.FooterDistancePt = 18;
        template.FirstHeader = new HeaderFooter("Final first «Name»");
        template.EvenFooter = new HeaderFooter("Final even «Name»");

        var firstSection = new Section(
            new PageSettings
            {
                DifferentFirstPage = true,
                DifferentOddEvenPages = true,
                HeaderDistancePt = 30
            },
            SectionBreakKind.OddPage)
        {
            HeadersFooters = new SectionHeadersFooters
            {
                Header = new HeaderFooter("Section default «Name»"),
                EvenHeader = new HeaderFooter("Section even «Name»"),
                FirstFooter = new HeaderFooter("Section first «Name»")
            }
        };
        template.Blocks.Add(new Paragraph("Body «Name»") { SectionBreak = firstSection });
        template.Blocks.Add(new Paragraph("Final body «Name»"));

        var merged = MailMerge.MergeRecord(template,
            new Dictionary<string, string> { ["Name"] = "Ada" });

        merged.Page.DifferentFirstPage.Should().BeTrue();
        merged.Page.DifferentOddEvenPages.Should().BeTrue();
        merged.Page.HeaderDistancePt.Should().Be(24);
        merged.Page.FooterDistancePt.Should().Be(18);
        merged.FirstHeader!.PlainText.Should().Be("Final first Ada");
        merged.EvenFooter!.PlainText.Should().Be("Final even Ada");
        var mergedSection = ((Paragraph)merged.Blocks[0]).SectionBreak!;
        mergedSection.Should().NotBeSameAs(firstSection);
        mergedSection.Page.Should().NotBeSameAs(firstSection.Page);
        mergedSection.BreakKind.Should().Be(SectionBreakKind.OddPage);
        mergedSection.HeadersFooters.Header!.PlainText.Should().Be("Section default Ada");
        mergedSection.HeadersFooters.EvenHeader!.PlainText.Should().Be("Section even Ada");
        mergedSection.HeadersFooters.FirstFooter!.PlainText.Should().Be("Section first Ada");
        template.FirstHeader!.PlainText.Should().Contain("«Name»");
        firstSection.HeadersFooters.Header!.PlainText.Should().Contain("«Name»");
    }

    [Fact]
    public void MergeRecordWithRules_EvaluatesRulesInFirstEvenAndNonFinalSectionStories()
    {
        var template = new TextDocument();
        var ifInstruction = MergeRuleEvaluator.BuildIfInstruction(
            "City", MergeConditionOperator.Equal, "London", "Local", "Remote");
        template.FirstHeader = new HeaderFooter(
            $"{MailMerge.FieldOpen}{ifInstruction}{MailMerge.FieldClose}");
        template.EvenFooter = new HeaderFooter($"Even {MailMerge.FieldOpen}Name{MailMerge.FieldClose}");
        var sectionParagraph = new Paragraph("Section end")
        {
            SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage)
            {
                HeadersFooters = new SectionHeadersFooters
                {
                    FirstFooter = new HeaderFooter(
                        $"Section {MailMerge.FieldOpen}Name{MailMerge.FieldClose}")
                }
            }
        };
        sectionParagraph.Runs.Add(Run.FromShape(Shape.TextBoxWith(
            $"Nested {MailMerge.FieldOpen}{ifInstruction}{MailMerge.FieldClose}", 100, 30)));
        template.Blocks.Add(sectionParagraph);

        var merged = MailMerge.MergeRecordWithRules(
            template,
            new Dictionary<string, string> { ["Name"] = "Ada", ["City"] = "London" },
            new MergeState(),
            recordIndex: 1);

        merged.FirstHeader!.PlainText.Should().Be("Local");
        merged.EvenFooter!.PlainText.Should().Be("Even Ada");
        ((Paragraph)merged.Blocks[0]).SectionBreak!.HeadersFooters.FirstFooter!.PlainText
            .Should().Be("Section Ada");
        ((Paragraph)merged.Blocks[0]).Runs[1].Shape!.PlainText.Should().Be("Nested Local");
    }

    [Fact]
    public void MergeRecord_DeepClonesRichRunPayloadsAndSubstitutesNestedText()
    {
        var shape = Shape.TextBoxWith("Shape «Name»", 120, 40);
        var wordArt = new WordArt("Art «Name»");
        var smartArt = new SmartArt { Kind = SmartArtKind.Hierarchy };
        smartArt.Nodes.Add(new SmartArtNode("Root «Name»", [new SmartArtNode("Child «Name»")]));
        var ruby = new RubyAnnotation();
        ruby.BaseFragments.Add(new RubyTextFragment("Ruby «Name»", RunFormatting.Default));
        ruby.PhoneticFragments.Add(new RubyTextFragment("Guide «Name»", RunFormatting.Default));
        var groupShape = Shape.TextBoxWith("Group shape «Name»", 80, 30);
        var groupWordArt = new WordArt("Group art «Name»");
        var group = new DrawingGroup();
        group.Children.Add(groupShape);
        group.Children.Add(groupWordArt);
        group.ChildOffsets.Add((0, 0));
        group.ChildOffsets.Add((82, 0));

        var paragraph = new Paragraph("Dear «Name»");
        paragraph.Runs.Add(Run.FromEquation(Equation.FromText("x+1")));
        paragraph.Runs.Add(Run.FromShape(shape));
        paragraph.Runs.Add(Run.FromWordArt(wordArt));
        paragraph.Runs.Add(Run.FromSmartArt(smartArt));
        paragraph.Runs.Add(Run.FromRuby(ruby));
        paragraph.Runs.Add(Run.FromDrawingGroup(group));
        var template = new TextDocument { Blocks = { paragraph } };

        var merged = MailMerge.MergeRecord(
            template,
            new Dictionary<string, string> { ["Name"] = "Ada" });

        var runs = merged.Paragraphs.Single().Runs;
        runs[0].Text.Should().Be("Dear Ada");
        runs[1].Equation.Should().NotBeNull().And.NotBeSameAs(paragraph.Runs[1].Equation);
        runs[2].Shape.Should().NotBeSameAs(shape);
        runs[2].Shape!.PlainText.Should().Be("Shape Ada");
        runs[3].WordArt.Should().NotBeSameAs(wordArt);
        runs[3].WordArt!.Text.Should().Be("Art Ada");
        runs[4].SmartArt.Should().NotBeSameAs(smartArt);
        var mergedSmartArt = runs[4].SmartArt!;
        mergedSmartArt.Nodes[0].Text.Should().Be("Root Ada");
        mergedSmartArt.Nodes[0].Children[0].Text.Should().Be("Child Ada");
        runs[5].Ruby.Should().NotBeSameAs(ruby);
        var mergedRuby = runs[5].Ruby!;
        mergedRuby.BaseText.Should().Be("Ruby Ada");
        mergedRuby.PhoneticFragments.Single().Text.Should().Be("Guide Ada");
        runs[6].DrawingGroup.Should().NotBeSameAs(group);
        var mergedGroup = runs[6].DrawingGroup!;
        ((Shape)mergedGroup.Children[0]).PlainText.Should().Be("Group shape Ada");
        ((WordArt)mergedGroup.Children[1]).Text.Should().Be("Group art Ada");

        shape.PlainText.Should().Contain("«Name»");
        wordArt.Text.Should().Contain("«Name»");
        smartArt.Nodes[0].Text.Should().Contain("«Name»");
    }

    [Fact]
    public void MergeRecord_SubstitutesEveryVisibleChartTextSurface()
    {
        var chart = Chart.Create(
            ChartKind.Column,
            ["Category «Name»"],
            [1d],
            seriesName: "Series «Name»",
            title: "Title «Name»");
        chart.CategoryAxisTitle = "Category axis «Name»";
        chart.ValueAxisTitle = "Value axis «Name»";
        var groupedChart = Chart.Create(
            ChartKind.Line,
            ["Grouped category «Name»"],
            [2d],
            seriesName: "Grouped series «Name»",
            title: "Grouped title «Name»");
        var group = new DrawingGroup();
        group.Children.Add(groupedChart);
        group.ChildOffsets.Add((0, 0));
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        paragraph.Runs.Add(Run.FromDrawingGroup(group));
        var template = new TextDocument { Blocks = { paragraph } };

        var merged = MailMerge.MergeRecord(
            template,
            new Dictionary<string, string> { ["Name"] = "Ada" });

        var mergedChart = merged.Paragraphs.Single().Runs[0].Chart!;
        mergedChart.Should().NotBeSameAs(chart);
        mergedChart.Title.Should().Be("Title Ada");
        mergedChart.CategoryAxisTitle.Should().Be("Category axis Ada");
        mergedChart.ValueAxisTitle.Should().Be("Value axis Ada");
        mergedChart.Categories.Should().Equal("Category Ada");
        mergedChart.Series.Single().Name.Should().Be("Series Ada");
        var mergedGroupedChart = (Chart)merged.Paragraphs.Single().Runs[1].DrawingGroup!.Children.Single();
        mergedGroupedChart.Title.Should().Be("Grouped title Ada");
        mergedGroupedChart.Categories.Should().Equal("Grouped category Ada");
        mergedGroupedChart.Series.Single().Name.Should().Be("Grouped series Ada");
        chart.Title.Should().Contain("«Name»");
        groupedChart.Title.Should().Contain("«Name»");
    }

    [Fact]
    public void MergeRecord_PreservesDocumentStateAndDeepClonesAnnotationStories()
    {
        var template = new TextDocument
        {
            UseWordApplicationDefaultLineSpacing = true,
            UseWordApplicationDefaultRunFormatting = true,
            Protection = new ProtectionSettings(ProtectionMode.ReadOnly),
            HideSpellingErrors = true,
            TrackRevisions = true,
            MarkedAsFinal = true,
            Theme = DocumentTheme.Catalog[1]
        };
        template.Properties.Title = "Merge template";
        template.Properties.Author = "FreeW";
        template.MultiLevelList.SetNumberFormat(1, ListNumberFormat.LowerLetter);
        template.FootnoteNumbering.NumberFormat = NoteNumberFormat.LowerRoman;
        template.FootnoteNumbering.StartAt = 3;
        template.EndnoteNumbering.NumberRestart = NoteNumberRestart.EachSection;
        template.Footnotes[1] = new Footnote(1, "Foot «Name»");
        template.Endnotes[2] = new Endnote(2, "End «Name»");
        var comment = new Comment(3, "Comment «Name»", "Reviewer", "RV")
        {
            Resolved = true
        };
        comment.Replies.Add(new Comment(4, "Reply «Name»", "Reply Author", "RA"));
        template.Comments[3] = comment;
        template.EmbeddedFonts.Add(new EmbeddedFont("Test Font", Regular: [1, 2, 3]));
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FootnoteReference(1));
        paragraph.Runs.Add(Run.EndnoteReference(2));
        paragraph.Runs.Add(Run.CommentReference(3));
        template.Blocks.Add(paragraph);

        var merged = MailMerge.MergeRecord(
            template,
            new Dictionary<string, string> { ["Name"] = "Ada" });

        merged.Properties.Title.Should().Be("Merge template");
        merged.Properties.Author.Should().Be("FreeW");
        merged.UseWordApplicationDefaultLineSpacing.Should().BeTrue();
        merged.UseWordApplicationDefaultRunFormatting.Should().BeTrue();
        merged.Protection.Mode.Should().Be(ProtectionMode.ReadOnly);
        merged.HideSpellingErrors.Should().BeTrue();
        merged.TrackRevisions.Should().BeTrue();
        merged.MarkedAsFinal.Should().BeTrue();
        merged.Theme.Should().Be(DocumentTheme.Catalog[1]);
        merged.MultiLevelList.GetNumberFormat(1).Should().Be(ListNumberFormat.LowerLetter);
        merged.FootnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.LowerRoman);
        merged.FootnoteNumbering.StartAt.Should().Be(3);
        merged.EndnoteNumbering.NumberRestart.Should().Be(NoteNumberRestart.EachSection);
        merged.Footnotes[1].Should().NotBeSameAs(template.Footnotes[1]);
        merged.Footnotes[1].PlainText.Should().Be("Foot Ada");
        merged.Endnotes[2].Should().NotBeSameAs(template.Endnotes[2]);
        merged.Endnotes[2].PlainText.Should().Be("End Ada");
        merged.Comments[3].Should().NotBeSameAs(comment);
        merged.Comments[3].PlainText.Should().Be("Comment Ada");
        merged.Comments[3].Replies.Single().PlainText.Should().Be("Reply Ada");
        merged.EmbeddedFonts.Single().Regular.Should().Equal(1, 2, 3);
        merged.EmbeddedFonts.Single().Regular.Should().NotBeSameAs(template.EmbeddedFonts.Single().Regular);
        template.Footnotes[1].PlainText.Should().Contain("«Name»");
        template.Comments[3].PlainText.Should().Contain("«Name»");
    }

    [Fact]
    public void MergeRecord_PreservesBlockContentControlRegion()
    {
        var control = BlockContentControl.BibliographyRegion();
        var template = new TextDocument();
        var paragraph = new Paragraph
        {
            BlockContentControl = control,
        };
        paragraph.Runs.Add(new Run($"{MailMerge.FieldOpen}Name{MailMerge.FieldClose}"));
        template.Blocks.Add(paragraph);

        var merged = MailMerge.MergeRecord(template, new Dictionary<string, string> { ["Name"] = "Ada" });

        merged.PlainText.Should().Be("Ada");
        merged.Blocks[0].BlockContentControl.Should().Be(control);
    }

    [Fact]
    public void MergeAll_ProducesOneDocumentPerRow_InOrder()
    {
        var template = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("Hello «Name»"));
        template.Blocks.Add(para);

        var data = new MergeData(
            ["Name"],
            [["Ada"], ["Grace"], ["Linus"]]);

        var merged = MailMerge.MergeAll(template, data);

        merged.Should().HaveCount(3);
        merged[0].PlainText.Should().Be("Hello Ada");
        merged[1].PlainText.Should().Be("Hello Grace");
        merged[2].PlainText.Should().Be("Hello Linus");
    }

    [Fact]
    public void MergeAll_EmptyData_YieldsEmptyList()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph("«Name»"));

        MailMerge.MergeAll(template, MergeData.FromCsv(string.Empty)).Should().BeEmpty();
    }

    [Fact]
    public void CombineMergedRecords_Letters_GivesEachRecordItsOwnNextPageSection()
    {
        var ada = new TextDocument { Blocks = { new Paragraph("Ada") } };
        ada.Page.MarginLeftPt = 72;
        ada.Header = new HeaderFooter("Recipient Ada");
        var grace = new TextDocument { Blocks = { new Paragraph("Grace") } };
        grace.Page.MarginLeftPt = 90;
        grace.Header = new HeaderFooter("Recipient Grace");

        var combined = MailMerge.CombineMergedRecords([ada, grace], MailMergeOutputMode.Letters);

        combined.Blocks.Should().HaveCount(2);
        ((Paragraph)combined.Blocks[0]).SectionBreak!.BreakKind.Should().Be(SectionBreakKind.NextPage);
        ((Paragraph)combined.Blocks[0]).Formatting.PageBreakBefore.Should().BeFalse();
        ((Paragraph)combined.Blocks[1]).Formatting.PageBreakBefore.Should().BeFalse();
        combined.Sections.Should().HaveCount(2);
        combined.Sections[0].Page.MarginLeftPt.Should().Be(72);
        combined.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("Recipient Ada");
        combined.Sections[1].Page.MarginLeftPt.Should().Be(90);
        combined.Sections[1].HeadersFooters.Header!.PlainText.Should().Be("Recipient Grace");
        combined.PlainText.Should().Be("Ada\nGrace");
    }

    [Fact]
    public void CombineMergedRecords_Letters_UsesDedicatedBoundaryAfterNonParagraphContent()
    {
        var first = new TextDocument { Blocks = { Table.Create(1, 1) } };
        var second = new TextDocument { Blocks = { new Paragraph("Next") } };

        var combined = MailMerge.CombineMergedRecords([first, second], MailMergeOutputMode.Letters);

        combined.Blocks.Should().HaveCount(3);
        combined.Blocks[1].Should().BeOfType<Paragraph>()
            .Which.SectionBreak!.BreakKind.Should().Be(SectionBreakKind.NextPage);
        combined.Blocks[2].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("Next");
    }

    [Fact]
    public void CombineMergedRecords_Directory_AppendsRecordsContinuously()
    {
        var docs = new[]
        {
            new TextDocument { Blocks = { new Paragraph("Ada") } },
            new TextDocument { Blocks = { new Paragraph("Grace") } }
        };

        var combined = MailMerge.CombineMergedRecords(docs, MailMergeOutputMode.Directory);

        combined.Blocks.Should().HaveCount(2);
        ((Paragraph)combined.Blocks[1]).Formatting.PageBreakBefore.Should().BeFalse();
        combined.PlainText.Should().Be("Ada\nGrace");
    }

    [Fact]
    public void CombineMergedRecords_RemapsAnnotationIdsAndKeepsRecipientSpecificStories()
    {
        var template = new TextDocument();
        var paragraph = new Paragraph("Dear «Name»");
        paragraph.Runs.Add(Run.FootnoteReference(1));
        template.Blocks.Add(paragraph);
        template.Footnotes[1] = new Footnote(1, "Private note for «Name»");
        var records = MailMerge.MergeAll(
            template,
            new MergeData(["Name"], [["Ada"], ["Grace"]]));

        var combined = MailMerge.CombineMergedRecords(records, MailMergeOutputMode.Letters);

        var references = combined.Paragraphs
            .SelectMany(item => item.Runs)
            .Where(run => run.FootnoteId is not null)
            .ToList();
        references.Select(run => run.FootnoteId).Should().Equal(1, 2);
        combined.Footnotes.Keys.Should().BeEquivalentTo([1, 2]);
        combined.Footnotes[1].PlainText.Should().Be("Private note for Ada");
        combined.Footnotes[2].PlainText.Should().Be("Private note for Grace");
    }

    [Fact]
    public void SuggestEmailAddressField_PrefersCommonEmailHeaders()
    {
        MailMerge.SuggestEmailAddressField(["Name", "E-mail Address", "City"])
            .Should().Be("E-mail Address");
    }

    [Fact]
    public void CreateEmailDeliveryPlan_AllRecords_ValidatesDeliverableRows()
    {
        var data = new MergeData(
            ["Name", "Email"],
            [["Ada", "ada@example.test"], ["Grace", ""], ["Linus", "linus@example.test"]]);
        var intent = new MailMergeEmailDeliveryIntent(
            "Email",
            "Newsletter",
            MailMergeEmailOutputFormat.MessageBody,
            MailMergeEmailBodyFormat.Html,
            MailMergeEmailRecordScope.AllRecords);

        var plan = MailMerge.CreateEmailDeliveryPlan(data, intent);

        plan.IsReady.Should().BeTrue();
        plan.RecordIndexes.Should().Equal(0, 1, 2);
        plan.DeliverableRecordIndexes.Should().Equal(0, 2);
        plan.Warnings.Should().ContainSingle().Which.Should().Contain("Record 2");
    }

    [Fact]
    public void CreateEmailDeliveryPlan_CurrentRecord_ClampsToRecipientRange()
    {
        var data = new MergeData(["Email"], [["a@example.test"], ["b@example.test"]]);
        var intent = new MailMergeEmailDeliveryIntent(
            "Email",
            "Subject",
            MailMergeEmailOutputFormat.Attachment,
            MailMergeEmailBodyFormat.PlainText,
            MailMergeEmailRecordScope.CurrentRecord,
            CurrentRecordIndex: 99);

        var plan = MailMerge.CreateEmailDeliveryPlan(data, intent);

        plan.RecordIndexes.Should().Equal(1);
        plan.DeliverableRecordIndexes.Should().Equal(1);
        plan.Intent.OutputFormat.Should().Be(MailMergeEmailOutputFormat.Attachment);
        plan.Intent.BodyFormat.Should().Be(MailMergeEmailBodyFormat.PlainText);
    }

    [Fact]
    public void CreateEmailDeliveryPlan_SelectedRecords_DeduplicatesAndWarnsForInvalidIndexes()
    {
        var data = new MergeData(["Email"], [["a@example.test"], ["b@example.test"], ["c@example.test"]]);
        var intent = new MailMergeEmailDeliveryIntent(
            "Email",
            "Subject",
            MailMergeEmailOutputFormat.MessageBody,
            MailMergeEmailBodyFormat.Html,
            MailMergeEmailRecordScope.SelectedRecords,
            SelectedRecordIndexes: [2, 0, 2, 5]);

        var plan = MailMerge.CreateEmailDeliveryPlan(data, intent);

        plan.RecordIndexes.Should().Equal(2, 0);
        plan.DeliverableRecordIndexes.Should().Equal(2, 0);
        plan.Warnings.Should().Contain(message => message.Contains("outside the recipient list"));
    }

    [Fact]
    public void CreateEmailDeliveryPlan_MissingEmailField_IsBlockingValidation()
    {
        var data = new MergeData(["Name"], [["Ada"]]);
        var intent = new MailMergeEmailDeliveryIntent(
            "Email",
            "",
            MailMergeEmailOutputFormat.MessageBody,
            MailMergeEmailBodyFormat.Html,
            MailMergeEmailRecordScope.AllRecords);

        var plan = MailMerge.CreateEmailDeliveryPlan(data, intent);

        plan.IsReady.Should().BeFalse();
        plan.Errors.Should().Contain(message => message.Contains("not in the recipient data source"));
        plan.Warnings.Should().Contain("Subject line is blank.");
    }

    [Fact]
    public void FromCsv_ParsesHeaderAndRows()
    {
        const string csv = "First,Last\nAda,Lovelace\nGrace,Hopper";

        var data = MergeData.FromCsv(csv);

        data.Header.Should().Equal("First", "Last");
        data.Count.Should().Be(2);
        data.Rows[0]["First"].Should().Be("Ada");
        data.Rows[0]["Last"].Should().Be("Lovelace");
        data.Rows[1]["First"].Should().Be("Grace");
    }

    [Fact]
    public void FromCsv_HonoursQuotedFields_WithEmbeddedCommasAndQuotes()
    {
        const string csv = "Name,Note\n\"Doe, Jane\",\"She said \"\"hi\"\"\"";

        var data = MergeData.FromCsv(csv);

        data.Rows.Should().HaveCount(1);
        data.Rows[0]["Name"].Should().Be("Doe, Jane");
        data.Rows[0]["Note"].Should().Be("She said \"hi\"");
    }

    [Fact]
    public void FromCsv_LookupIsCaseInsensitive()
    {
        var data = MergeData.FromCsv("Name\nAda");

        data.Rows[0]["name"].Should().Be("Ada");
        data.Rows[0]["NAME"].Should().Be("Ada");
    }

    [Fact]
    public void FromCsv_ShortRow_PadsMissingCellsWithEmpty()
    {
        var data = MergeData.FromCsv("A,B,C\n1,2");

        data.Rows[0]["A"].Should().Be("1");
        data.Rows[0]["B"].Should().Be("2");
        data.Rows[0]["C"].Should().Be(string.Empty);
    }

    [Fact]
    public void FromCsv_HandlesCrlfLineEndings()
    {
        var data = MergeData.FromCsv("First,Last\r\nAda,Lovelace\r\n");

        data.Count.Should().Be(1);
        data.Rows[0]["First"].Should().Be("Ada");
    }

    [Fact]
    public void EndToEnd_MergeAll_OverCsv_FillsTemplate()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph("Dear «First» «Last»,"));

        var data = MergeData.FromCsv("First,Last\nAda,Lovelace\nGrace,Hopper");
        var docs = MailMerge.MergeAll(template, data);

        docs.Should().HaveCount(2);
        docs[0].PlainText.Should().Be("Dear Ada Lovelace,");
        docs[1].PlainText.Should().Be("Dear Grace Hopper,");
    }

    [Fact]
    public void MergeRecord_PreservesEndnoteAndCellMergeMarks()
    {
        // Regression: the clone path used to drop EndnoteId/HyperlinkTooltip from runs and
        // GridSpan/VerticalMerge from cells, orphaning endnotes and collapsing merged cells.
        var template = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.EndnoteReference(1));
        template.Blocks.Add(para);
        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0] = new TableCell("m") { GridSpan = 2, VerticalMerge = VerticalMergeState.Restart };
        template.Blocks.Add(table);

        var merged = MailMerge.MergeRecord(template, new Dictionary<string, string>());

        merged.Paragraphs.First().Runs.Single().EndnoteId.Should().Be(1);
        var mergedTable = merged.Blocks.OfType<Table>().Single();
        mergedTable.Rows[0].Cells[0].GridSpan.Should().Be(2);
        mergedTable.Rows[0].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Restart);
    }

    // ── FieldMapping / AutoMatchFields ───────────────────────────────────────────────────────────────

    [Fact]
    public void AutoMatchFields_MatchesFirstNameAndLastName_CaseInsensitive()
    {
        var mapping = MailMerge.AutoMatchFields(["first name", "LAST NAME", "Company"]);

        mapping[FieldRole.FirstName].Should().Be("first name");
        mapping[FieldRole.LastName].Should().Be("LAST NAME");
        mapping[FieldRole.Company].Should().Be("Company");
    }

    [Fact]
    public void AutoMatchFields_MatchesConcatenatedVariants()
    {
        var mapping = MailMerge.AutoMatchFields(["FirstName", "LastName", "PostalCode"]);

        mapping[FieldRole.FirstName].Should().Be("FirstName");
        mapping[FieldRole.LastName].Should().Be("LastName");
        mapping[FieldRole.PostalCode].Should().Be("PostalCode");
    }

    [Fact]
    public void AutoMatchFields_UnmatchedRole_IsNull()
    {
        var mapping = MailMerge.AutoMatchFields(["Name"]);

        // "Name" alone matches neither FirstName nor LastName synonyms exactly.
        mapping[FieldRole.MiddleName].Should().BeNull();
        mapping[FieldRole.Suffix].Should().BeNull();
    }

    [Fact]
    public void AutoMatchFields_EmptyHeader_AllRolesNull()
    {
        var mapping = MailMerge.AutoMatchFields([]);

        foreach (FieldRole role in Enum.GetValues(typeof(FieldRole)))
            mapping[role].Should().BeNull($"role {role} should be unmatched for an empty header");
    }

    [Fact]
    public void AutoMatchFields_ZipSynonym_MatchesPostalCode()
    {
        var mapping = MailMerge.AutoMatchFields(["Zip"]);

        mapping[FieldRole.PostalCode].Should().Be("Zip");
    }

    [Fact]
    public void AutoMatchFields_AddressSynonym_MatchesAddress1()
    {
        var mapping = MailMerge.AutoMatchFields(["Address", "City", "State"]);

        mapping[FieldRole.Address1].Should().Be("Address");
        mapping[FieldRole.City].Should().Be("City");
        mapping[FieldRole.State].Should().Be("State");
    }

    // ── ComposeAddressBlock ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComposeAddressBlock_FullRecord_FormatsCorrectly()
    {
        var mapping = MailMerge.AutoMatchFields(["FirstName", "LastName", "Company", "Address1", "City", "State", "PostalCode"]);
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FirstName"] = "Ada", ["LastName"] = "Lovelace", ["Company"] = "Babbage Inc.",
            ["Address1"] = "1 Engine Way", ["City"] = "London", ["State"] = "England", ["PostalCode"] = "EC1A 1BB"
        };

        var block = MailMerge.ComposeAddressBlock(row, mapping);

        block.Should().Be("Ada Lovelace\nBabbage Inc.\n1 Engine Way\nLondon, England EC1A 1BB");
    }

    [Fact]
    public void ComposeAddressBlock_MissingCity_OmitsCityStateSeparator()
    {
        var mapping = MailMerge.AutoMatchFields(["FirstName", "LastName", "State", "PostalCode"]);
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FirstName"] = "Grace", ["LastName"] = "Hopper",
            ["State"] = "CT", ["PostalCode"] = "06830"
        };

        var block = MailMerge.ComposeAddressBlock(row, mapping);

        // No city → state is used alone on the city-state line (no leading comma).
        block.Should().Contain("CT 06830");
        block.Should().NotContain(", CT");
    }

    [Fact]
    public void ComposeAddressBlock_AllFieldsUnmapped_ReturnsEmpty()
    {
        var block = MailMerge.ComposeAddressBlock(
            new Dictionary<string, string>(),
            new FieldMapping());

        block.Should().BeEmpty();
    }

    [Fact]
    public void ComposeAddressBlock_WithCountry_AppendsCountryOnLastLine()
    {
        var mapping = new FieldMapping();
        mapping[FieldRole.FirstName] = "F";
        mapping[FieldRole.LastName]  = "L";
        mapping[FieldRole.Country]   = "Country";
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["F"] = "Marie", ["L"] = "Curie", ["Country"] = "France" };

        var block = MailMerge.ComposeAddressBlock(row, mapping);

        block.Should().EndWith("\nFrance");
    }

    [Fact]
    public void ComposeAddressBlock_DefaultWordLayoutOmitsMiddleName()
    {
        var mapping = MailMerge.AutoMatchFields(["Title", "FirstName", "MiddleName", "LastName", "Suffix"]);
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = "Dr.", ["FirstName"] = "Ada", ["MiddleName"] = "M.",
            ["LastName"] = "Lovelace", ["Suffix"] = "PhD"
        };

        MailMerge.ComposeAddressBlock(row, mapping).Should().Be("Dr. Ada Lovelace PhD");
    }

    // ── ComposeGreetingLine ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComposeGreetingLine_TitleAndLastName_UsesTitleLastNameForm()
    {
        var mapping = MailMerge.AutoMatchFields(["Title", "FirstName", "LastName"]);
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["Title"] = "Dr.", ["FirstName"] = "Ada", ["LastName"] = "Lovelace" };

        MailMerge.ComposeGreetingLine(row, mapping).Should().Be("Dear Dr. Lovelace,");
    }

    [Fact]
    public void ComposeGreetingLine_NoTitle_UsesFirstLastForm()
    {
        var mapping = MailMerge.AutoMatchFields(["FirstName", "LastName"]);
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["FirstName"] = "Grace", ["LastName"] = "Hopper" };

        MailMerge.ComposeGreetingLine(row, mapping).Should().Be("Dear Grace Hopper,");
    }

    [Fact]
    public void ComposeGreetingLine_NoNameFields_FallsBackToSirOrMadam()
    {
        MailMerge.ComposeGreetingLine(
            new Dictionary<string, string>(),
            new FieldMapping())
            .Should().Be("Dear Sir or Madam,");
    }

    [Fact]
    public void ComposeGreetingLine_CustomGreetingPrefix()
    {
        var mapping = MailMerge.AutoMatchFields(["LastName"]);
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["LastName"] = "Turing" };

        MailMerge.ComposeGreetingLine(row, mapping, greetingFormat: "Hello").Should().Be("Hello Turing,");
    }

    [Fact]
    public void ComposeGreetingLine_OnlyFirstName_UsesFirstName()
    {
        var mapping = MailMerge.AutoMatchFields(["FirstName"]);
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["FirstName"] = "Linus" };

        MailMerge.ComposeGreetingLine(row, mapping).Should().Be("Dear Linus,");
    }

    // ── SubstituteSpecial (Next Record / Merge Record #) ────────────────────────────────────────────

    [Fact]
    public void SubstituteSpecial_MergeRecordNumber_InjectsOneBasedIndex()
    {
        var row = new Dictionary<string, string>();
        var result = MailMerge.SubstituteSpecial(
            $"Record {MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose}",
            row, recordIndex: 3, out var advance);

        result.Should().Be("Record 3");
        advance.Should().BeFalse();
    }

    [Fact]
    public void SubstituteSpecial_NextRecord_SetsAdvanceFlagAndProducesNoOutput()
    {
        var row = new Dictionary<string, string>();
        var result = MailMerge.SubstituteSpecial(
            $"A{MailMerge.FieldOpen}{MailMerge.NextRecordField}{MailMerge.FieldClose}B",
            row, recordIndex: 1, out var advance);

        // «Next Record» emits nothing (only the surrounding literal text remains).
        result.Should().Be("AB");
        advance.Should().BeTrue();
    }

    [Fact]
    public void SubstituteSpecial_NextRecord_CaseInsensitive()
    {
        var row = new Dictionary<string, string>();
        MailMerge.SubstituteSpecial(
            $"{MailMerge.FieldOpen}NEXT RECORD{MailMerge.FieldClose}",
            row, recordIndex: 1, out var advance);

        advance.Should().BeTrue();
    }

    [Fact]
    public void SubstituteSpecial_MergeRecordNumber_CaseInsensitive()
    {
        var row = new Dictionary<string, string>();
        var result = MailMerge.SubstituteSpecial(
            $"{MailMerge.FieldOpen}merge record #{MailMerge.FieldClose}",
            row, recordIndex: 7, out _);

        result.Should().Be("7");
    }

    [Fact]
    public void SubstituteSpecial_RegularField_StillSubstituted()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "Ada" };
        var result = MailMerge.SubstituteSpecial(
            $"Hi {MailMerge.FieldOpen}Name{MailMerge.FieldClose}",
            row, recordIndex: 1, out _);

        result.Should().Be("Hi Ada");
    }

    [Fact]
    public void SubstituteSpecial_NoPlaceholders_ReturnsSameString()
    {
        var row = new Dictionary<string, string>();
        var result = MailMerge.SubstituteSpecial("plain text", row, recordIndex: 1, out var advance);

        result.Should().Be("plain text");
        advance.Should().BeFalse();
    }

    // ── FieldMapping accessors ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void FieldMapping_SetAndGetRoundTrips()
    {
        var m = new FieldMapping();
        m[FieldRole.City] = "MyCityColumn";

        m[FieldRole.City].Should().Be("MyCityColumn");
        m[FieldRole.State].Should().BeNull("unmapped role returns null");
    }

    [Fact]
    public void FieldMapping_SetToNull_UnmapsRole()
    {
        var m = new FieldMapping();
        m[FieldRole.Country] = "Country";
        m[FieldRole.Country] = null;

        m[FieldRole.Country].Should().BeNull();
    }

    // ── MergeRuleEvaluator — If…Then…Else ───────────────────────────────────────────────────────

    [Fact]
    public void MergeRuleEvaluator_IfEqual_TrueCondition_EmitsTrueText()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Status"] = "VIP" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Status", MergeConditionOperator.Equal, "VIP", "Gold", "Standard");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result.Should().NotBeNull();
        result!.Value.Text.Should().Be("Gold");
        result.Value.SkipRecord.Should().BeFalse();
        result.Value.AdvanceRecord.Should().BeFalse();
    }

    [Fact]
    public void MergeRuleEvaluator_IfEqual_FalseCondition_EmitsFalseText()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Status"] = "Regular" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Status", MergeConditionOperator.Equal, "VIP", "Gold", "Standard");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("Standard");
    }

    [Fact]
    public void MergeRuleEvaluator_IfNotEqual_EmitsCorrectBranch()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Country"] = "UK" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Country", MergeConditionOperator.NotEqual, "US", "International", "Domestic");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("International");
    }

    [Fact]
    public void MergeRuleEvaluator_IfLessThan_NumericComparison()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Score"] = "45" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Score", MergeConditionOperator.LessThan, "50", "Low", "High");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("Low");
    }

    [Fact]
    public void MergeRuleEvaluator_IfGreaterThanOrEqual_NumericComparison()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Score"] = "100" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Score", MergeConditionOperator.GreaterThanOrEqual, "100", "Perfect", "Not perfect");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("Perfect");
    }

    [Fact]
    public void MergeRuleEvaluator_IfIsBlank_TrueWhenFieldEmpty()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["MiddleName"] = "" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("MiddleName", MergeConditionOperator.IsBlank, "", "No middle name", "Has middle name");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("No middle name");
    }

    [Fact]
    public void MergeRuleEvaluator_IfIsNotBlank_TrueWhenFieldPopulated()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Title"] = "Dr." };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Title", MergeConditionOperator.IsNotBlank, "", "Dr. ", "");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("Dr. ");
    }

    [Fact]
    public void MergeRuleEvaluator_IfContains_CaseInsensitiveSubstring()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Notes"] = "Premium subscriber since 2020" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Notes", MergeConditionOperator.Contains, "premium", "VIP", "Regular");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("VIP");
    }

    [Fact]
    public void MergeRuleEvaluator_IfMissingField_TreatedAsBlank()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("NoSuchField", MergeConditionOperator.IsBlank, "", "Blank", "NotBlank");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("Blank");
    }

    // ── MergeRuleEvaluator — Skip Record If ─────────────────────────────────────────────────────

    [Fact]
    public void MergeRuleEvaluator_SkipRecordIf_ConditionTrue_MarksSkipAndSetsFlag()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Opted Out"] = "Yes" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildSkipRecordIfInstruction("Opted Out", MergeConditionOperator.Equal, "Yes");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 2);

        result!.Value.SkipRecord.Should().BeTrue();
        result.Value.Text.Should().BeEmpty();
        state.SkippedIndices.Should().Contain(2);
    }

    [Fact]
    public void MergeRuleEvaluator_SkipRecordIf_ConditionFalse_DoesNotSkip()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Opted Out"] = "No" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildSkipRecordIfInstruction("Opted Out", MergeConditionOperator.Equal, "Yes");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 2);

        result!.Value.SkipRecord.Should().BeFalse();
        state.SkippedIndices.Should().BeEmpty();
    }

    // ── MergeRuleEvaluator — Next Record If ─────────────────────────────────────────────────────

    [Fact]
    public void MergeRuleEvaluator_NextRecordIf_ConditionTrue_SetsAdvanceFlag()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Type"] = "Header" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildNextRecordIfInstruction("Type", MergeConditionOperator.Equal, "Header");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.AdvanceRecord.Should().BeTrue();
        result.Value.SkipRecord.Should().BeFalse();
    }

    [Fact]
    public void MergeRuleEvaluator_NextRecordIf_ConditionFalse_NoAdvance()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Type"] = "Data" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildNextRecordIfInstruction("Type", MergeConditionOperator.Equal, "Header");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.AdvanceRecord.Should().BeFalse();
    }

    // ── MergeRuleEvaluator — Merge Sequence # ───────────────────────────────────────────────────

    [Fact]
    public void MergeRuleEvaluator_MergeSequenceNumber_EmitsCurrentSequenceNumber()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState { SequenceNumber = 3 };
        var instruction = "Merge Sequence #";

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("3");
    }

    [Fact]
    public void MergeRuleEvaluator_MergeSequenceNumber_CaseInsensitive()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState { SequenceNumber = 7 };

        var result = MergeRuleEvaluator.Evaluate("merge sequence #", row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("7");
    }

    // ── MergeRuleEvaluator — Set / Ref Bookmark ─────────────────────────────────────────────────

    [Fact]
    public void MergeRuleEvaluator_SetBookmark_StoresValueInState()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Region"] = "EMEA" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildSetInstruction("MyBookmark", "fixed value");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().BeEmpty();
        state.Bookmarks["MyBookmark"].Should().Be("fixed value");
    }

    [Fact]
    public void MergeRuleEvaluator_RefBookmark_EmitsStoredValue()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState();
        state.Bookmarks["Greeting"] = "Hello, friend";
        var instruction = MergeRuleEvaluator.BuildRefInstruction("Greeting");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("Hello, friend");
    }

    [Fact]
    public void MergeRuleEvaluator_RefBookmark_MissingBookmark_EmitsEmpty()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildRefInstruction("NoSuchBookmark");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().BeEmpty();
    }

    // ── MergeRuleEvaluator — Fill-in / Ask ──────────────────────────────────────────────────────

    [Fact]
    public void MergeRuleEvaluator_FillIn_EmitsPrePopulatedAnswer()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState();
        state.FillInAnswers["Enter your name:"] = "John Smith";
        var instruction = MergeRuleEvaluator.BuildFillInInstruction("Enter your name:");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("John Smith");
    }

    [Fact]
    public void MergeRuleEvaluator_FillIn_MissingAnswer_EmitsEmpty()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildFillInInstruction("What is your department?");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().BeEmpty();
    }

    [Fact]
    public void MergeRuleEvaluator_Ask_StoresAnswerAsBookmarkAndEmitsIt()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState();
        state.AskAnswers["Manager"] = "Alice";
        var instruction = MergeRuleEvaluator.BuildAskInstruction("Manager", "Who is the manager?");

        var result = MergeRuleEvaluator.Evaluate(instruction, row, state, recordIndex: 0);

        result!.Value.Text.Should().Be("Alice");
        state.Bookmarks["Manager"].Should().Be("Alice");
    }

    // ── MergeRuleEvaluator — unrecognised instruction ────────────────────────────────────────────

    [Fact]
    public void InteractivePromptPlanner_PreservesOrderDeduplicatesAndTraversesDocumentStories()
    {
        var document = new TextDocument();
        var splitFillIn = new Paragraph();
        splitFillIn.Runs.Add(new Run($"{MailMerge.FieldOpen}Fill-in \"Depart"));
        splitFillIn.Runs.Add(new Run($"ment\"{MailMerge.FieldClose}"));
        document.Blocks.Add(splitFillIn);

        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Paragraphs.Add(new Paragraph(
            $"{MailMerge.FieldOpen}fill-IN \"department\"{MailMerge.FieldClose}"));
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Blocks.Add(table);

        var shapeParagraph = new Paragraph();
        shapeParagraph.Runs.Add(Run.FromShape(Shape.TextBoxWith(
            $"{MailMerge.FieldOpen}Ask Manager \"Who is the manager?\"{MailMerge.FieldClose}",
            120,
            40)));
        document.Blocks.Add(shapeParagraph);
        document.FinalSectionHeadersFooters.Header = new HeaderFooter(
            $"{MailMerge.FieldOpen}Ask Approver \"Who approves?\"{MailMerge.FieldClose}");

        var prompts = MailMergeInteractivePromptPlanner.Plan(document);

        prompts.Should().Equal(
            new MailMergeInteractivePrompt(MailMergeInteractivePromptKind.FillIn, "Department", "Department"),
            new MailMergeInteractivePrompt(MailMergeInteractivePromptKind.Ask, "Manager", "Who is the manager?"),
            new MailMergeInteractivePrompt(MailMergeInteractivePromptKind.Ask, "Approver", "Who approves?"));
    }

    [Fact]
    public void TryParseInteractivePrompt_UnescapesQuotedPromptText()
    {
        var instruction = MergeRuleEvaluator.BuildFillInInstruction("Manager said \"now\"");

        var parsed = MergeRuleEvaluator.TryParseInteractivePrompt(instruction, out var prompt);

        parsed.Should().BeTrue();
        prompt.Should().Be(new MailMergeInteractivePrompt(
            MailMergeInteractivePromptKind.FillIn,
            "Manager said \"now\"",
            "Manager said \"now\""));
    }

    [Fact]
    public void InteractivePromptPlanner_DiscoversNativeFillInAndAskComplexFields()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.ComplexFieldRun(
            " FILLIN \"Office location?\" \\d \"Kyiv\" \\o ",
            "cached office"));
        paragraph.Runs.Add(new Run(" / "));
        paragraph.Runs.Add(Run.ComplexFieldRun(
            " ASK Approver \"Who approves?\" \\d \"Ada\" \\o ",
            "cached approver"));
        paragraph.Runs.Add(Run.ComplexFieldRun(
            " FILLIN \"Per-record prompt\" ",
            "cached per-record value"));
        document.Blocks.Add(paragraph);

        var prompts = MailMergeInteractivePromptPlanner.Plan(document);

        prompts.Should().Equal(
            new MailMergeInteractivePrompt(MailMergeInteractivePromptKind.FillIn, "Office location?", "Office location?"),
            new MailMergeInteractivePrompt(MailMergeInteractivePromptKind.Ask, "Approver", "Who approves?"));
    }

    [Fact]
    public void MergeRuleEvaluator_UnrecognisedInstruction_ReturnsNull()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "Ada" };
        var state = new MergeState();

        var result = MergeRuleEvaluator.Evaluate("Name", row, state, recordIndex: 0);

        result.Should().BeNull("plain merge-field names are not rule instructions");
    }

    // ── MergeRuleEvaluator.EvaluateCondition — standalone operator tests ─────────────────────────

    [Theory]
    [InlineData("apple", MergeConditionOperator.Equal, "apple", true)]
    [InlineData("apple", MergeConditionOperator.Equal, "Apple", true)]   // case-insensitive
    [InlineData("apple", MergeConditionOperator.NotEqual, "banana", true)]
    [InlineData("10",    MergeConditionOperator.LessThan, "20", true)]
    [InlineData("20",    MergeConditionOperator.LessThan, "10", false)]
    [InlineData("10",    MergeConditionOperator.LessThanOrEqual, "10", true)]
    [InlineData("15",    MergeConditionOperator.GreaterThan, "10", true)]
    [InlineData("10",    MergeConditionOperator.GreaterThanOrEqual, "10", true)]
    [InlineData("",      MergeConditionOperator.IsBlank, "", true)]
    [InlineData("  ",    MergeConditionOperator.IsBlank, "", true)]
    [InlineData("x",     MergeConditionOperator.IsBlank, "", false)]
    [InlineData("x",     MergeConditionOperator.IsNotBlank, "", true)]
    [InlineData("Hello World", MergeConditionOperator.Contains, "world", true)]
    [InlineData("Hello World", MergeConditionOperator.Contains, "xyz", false)]
    public void EvaluateCondition_OperatorCases(string fieldValue, MergeConditionOperator op, string value, bool expected)
    {
        MergeRuleEvaluator.EvaluateCondition(fieldValue, op, value).Should().Be(expected);
    }

    // ── MailMerge.MergeAllWithRules — integration tests ──────────────────────────────────────────

    [Fact]
    public void MergeAllWithRules_SkipRecordIf_ExcludesMatchingRecords()
    {
        var template = new TextDocument();
        var para = new Paragraph();
        // First run: Skip Record If Type = Header
        para.Runs.Add(new Run($"{MailMerge.FieldOpen}{MergeRuleEvaluator.BuildSkipRecordIfInstruction("Type", MergeConditionOperator.Equal, "Header")}{MailMerge.FieldClose}"));
        para.Runs.Add(new Run("«Name»"));
        template.Blocks.Add(para);

        var data = new MergeData(
            ["Type", "Name"],
            [["Header", "Section A"], ["Data", "Alice"], ["Data", "Bob"], ["Header", "Section B"], ["Data", "Carol"]]);

        var state = new MergeState();
        var merged = MailMerge.MergeAllWithRules(template, data, state);

        // Header records (indices 0 and 3) should be skipped.
        merged.Should().HaveCount(3);
        merged[0].PlainText.Should().Contain("Alice");
        merged[1].PlainText.Should().Contain("Bob");
        merged[2].PlainText.Should().Contain("Carol");
        state.SkippedIndices.Should().BeEquivalentTo([0, 3]);
    }

    [Fact]
    public void MergeAllWithRules_PreservesBlockContentControlRegion()
    {
        var control = BlockContentControl.BibliographyRegion();
        var template = new TextDocument();
        var paragraph = new Paragraph
        {
            BlockContentControl = control,
        };
        paragraph.Runs.Add(new Run(
            $"{MailMerge.FieldOpen}{MergeRuleEvaluator.BuildIfInstruction("Tier", MergeConditionOperator.Equal, "VIP", "Priority", "Standard")}{MailMerge.FieldClose}"));
        template.Blocks.Add(paragraph);
        var data = new MergeData(["Tier"], [["VIP"]]);

        var merged = MailMerge.MergeAllWithRules(template, data, new MergeState());

        merged.Should().ContainSingle();
        merged[0].PlainText.Should().Be("Priority");
        merged[0].Blocks[0].BlockContentControl.Should().Be(control);
    }

    [Fact]
    public void MergeAllWithRules_MergeSequenceNumber_CountsNonSkippedRecords()
    {
        // Template: «Skip Record If Type = Header»«Merge Sequence #» «Name»
        var template = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run(
            $"{MailMerge.FieldOpen}{MergeRuleEvaluator.BuildSkipRecordIfInstruction("Type", MergeConditionOperator.Equal, "Header")}{MailMerge.FieldClose}" +
            $"{MailMerge.FieldOpen}{MailMerge.MergeSequenceNumberField}{MailMerge.FieldClose} «Name»"));
        template.Blocks.Add(para);

        var data = new MergeData(
            ["Type", "Name"],
            [["Header", "Ignored"], ["Data", "Alice"], ["Data", "Bob"]]);

        var state = new MergeState();
        var merged = MailMerge.MergeAllWithRules(template, data, state);

        // Record 0 (Header) is skipped; Alice is sequence 1, Bob is sequence 2.
        merged.Should().HaveCount(2);
        merged[0].PlainText.Should().Contain("1 Alice");
        merged[1].PlainText.Should().Contain("2 Bob");
    }

    [Fact]
    public void MergeAllWithRules_SetAndRefBookmark_ResolveAcrossRuns()
    {
        // Template: «Set Region "EMEA"»Dear «Name», your region is «Ref Region».
        var template = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run(
            $"{MailMerge.FieldOpen}{MergeRuleEvaluator.BuildSetInstruction("Region", "EMEA")}{MailMerge.FieldClose}" +
            $"Dear «Name», your region is {MailMerge.FieldOpen}{MergeRuleEvaluator.BuildRefInstruction("Region")}{MailMerge.FieldClose}."));
        template.Blocks.Add(para);

        var data = new MergeData(["Name"], [["Ada"], ["Grace"]]);
        var state = new MergeState();
        var merged = MailMerge.MergeAllWithRules(template, data, state);

        merged.Should().HaveCount(2);
        merged[0].PlainText.Should().Be("Dear Ada, your region is EMEA.");
        merged[1].PlainText.Should().Be("Dear Grace, your region is EMEA.");
    }

    [Fact]
    public void MergeAllWithRules_IfThenElse_EmitsCorrectBranchPerRecord()
    {
        // Template: «If Status = VIP Then "Gold treatment" Else "Standard treatment"»
        var template = new TextDocument();
        var para = new Paragraph();
        var instruction = MergeRuleEvaluator.BuildIfInstruction("Status", MergeConditionOperator.Equal, "VIP", "Gold treatment", "Standard treatment");
        para.Runs.Add(new Run($"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}"));
        template.Blocks.Add(para);

        var data = new MergeData(["Status"], [["VIP"], ["Regular"], ["VIP"]]);
        var state = new MergeState();
        var merged = MailMerge.MergeAllWithRules(template, data, state);

        merged.Should().HaveCount(3);
        merged[0].PlainText.Should().Be("Gold treatment");
        merged[1].PlainText.Should().Be("Standard treatment");
        merged[2].PlainText.Should().Be("Gold treatment");
    }

    [Fact]
    public void MergeAllWithRules_FillIn_UsesPrePopulatedAnswer()
    {
        var template = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run(
            $"Hello «Name», {MailMerge.FieldOpen}{MergeRuleEvaluator.BuildFillInInstruction("Department:")}{MailMerge.FieldClose}"));
        template.Blocks.Add(para);

        var data = new MergeData(["Name"], [["Ada"], ["Grace"]]);
        var state = new MergeState();
        state.FillInAnswers["Department:"] = "Engineering";

        var merged = MailMerge.MergeAllWithRules(template, data, state);

        merged.Should().HaveCount(2);
        merged[0].PlainText.Should().Be("Hello Ada, Engineering");
        merged[1].PlainText.Should().Be("Hello Grace, Engineering");
    }

    [Fact]
    public void MergeAllWithRules_NativeFillInAndAsk_UseAnswersAndMaterializeResults()
    {
        var template = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.ComplexFieldRun(" FILLIN \"Department\" \\o ", "cached department"));
        paragraph.Runs.Add(new Run(" | "));
        paragraph.Runs.Add(Run.ComplexFieldRun(" ASK Manager \"Who is the manager?\" \\o ", "cached manager"));
        paragraph.Runs.Add(new Run($" | {MailMerge.FieldOpen}Name{MailMerge.FieldClose}"));
        template.Blocks.Add(paragraph);
        var data = new MergeData(["Name"], [["Ada"], ["Grace"]]);
        var state = new MergeState();
        state.FillInAnswers["Department"] = "Engineering";
        state.AskAnswers["Manager"] = "Margaret";

        var merged = MailMerge.MergeAllWithRules(template, data, state);

        merged.Select(document => document.PlainText).Should().Equal(
            "Engineering | Margaret | Ada",
            "Engineering | Margaret | Grace");
        merged.SelectMany(document => document.Blocks.OfType<Paragraph>())
            .SelectMany(resultParagraph => resultParagraph.Runs)
            .Should().AllSatisfy(run => run.ComplexField.Should().BeNull());
        state.Bookmarks["Manager"].Should().Be("Margaret");
    }

    [Fact]
    public void MergeAllWithRules_NativeInteractiveFieldWithoutOnceSwitch_RemainsAField()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" FILLIN \"Per-record prompt\" ", "cached result") }
        });
        var state = new MergeState();
        state.FillInAnswers["Per-record prompt"] = "one answer";

        var merged = MailMerge.MergeAllWithRules(
            template,
            new MergeData(["Name"], [["Ada"]]),
            state);

        merged.Should().ContainSingle().Which.PlainText.Should().Be("cached result");
        merged[0].Paragraphs.Single().Runs.Single().ComplexField.Should().NotBeNull();
        MailMergeInteractivePromptPlanner.Plan(template).Should().BeEmpty();
    }

    [Fact]
    public void MergeAllWithRules_NextRecord_ConsumesOneAdditionalSourceRow()
    {
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph(
            $"{MailMerge.FieldOpen}Name{MailMerge.FieldClose}" +
            $"{MailMerge.FieldOpen}{MailMerge.NextRecordField}{MailMerge.FieldClose}"));
        var data = new MergeData(
            ["Name"],
            [["Ada"], ["Grace"], ["Linus"], ["Margaret"]]);

        var merged = MailMerge.MergeAllWithRules(template, data, new MergeState());

        merged.Select(document => document.PlainText).Should().Equal("Ada", "Linus");
    }

    [Fact]
    public void MergeRecordWithRules_NativeSpecialFields_UpdateResultsAndPreserveInstructions()
    {
        var template = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.ComplexFieldRun(
            $" {MailMerge.NextRecordInstruction} ",
            $"{MailMerge.FieldOpen}{MailMerge.NextRecordField}{MailMerge.FieldClose}"));
        paragraph.Runs.Add(Run.ComplexFieldRun(
            $" {MailMerge.MergeRecordNumberInstruction} ",
            $"{MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose}"));
        paragraph.Runs.Add(Run.ComplexFieldRun(
            $" {MailMerge.MergeSequenceNumberInstruction} ",
            $"{MailMerge.FieldOpen}{MailMerge.MergeSequenceNumberField}{MailMerge.FieldClose}"));
        template.Blocks.Add(paragraph);
        var state = new MergeState { SequenceNumber = 2 };

        var merged = MailMerge.MergeRecordWithRules(
            template,
            new Dictionary<string, string>(),
            state,
            recordIndex: 4);

        var fields = merged.Blocks.OfType<Paragraph>().Single().Runs;
        fields.Select(run => run.ComplexField!.Keyword).Should().Equal(
            MailMerge.NextRecordInstruction,
            MailMerge.MergeRecordNumberInstruction,
            MailMerge.MergeSequenceNumberInstruction);
        fields.Select(run => run.Text).Should().Equal(string.Empty, "4", "2");
        state.AdvanceRecordRequested.Should().BeTrue();
    }

    [Theory]
    [InlineData(MailMerge.NextRecordField, MailMerge.NextRecordInstruction)]
    [InlineData(MailMerge.MergeRecordNumberField, MailMerge.MergeRecordNumberInstruction)]
    [InlineData(MailMerge.MergeSequenceNumberField, MailMerge.MergeSequenceNumberInstruction)]
    public void TryGetNativeSpecialFieldInstruction_MapsVisibleLabels(string label, string expected)
    {
        MailMerge.TryGetNativeSpecialFieldInstruction(label, out var instruction).Should().BeTrue();
        instruction.Should().Be(expected);
    }

    [Fact]
    public void MergeAllWithRules_NextRecordIf_AdvancesOnlyWhenConditionMatches()
    {
        var instruction = MergeRuleEvaluator.BuildNextRecordIfInstruction(
            "Advance", MergeConditionOperator.Equal, "Yes");
        var template = new TextDocument();
        template.Blocks.Add(new Paragraph(
            $"{MailMerge.FieldOpen}Name{MailMerge.FieldClose}" +
            $"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}"));
        var data = new MergeData(
            ["Name", "Advance"],
            [["Ada", "Yes"], ["Grace", "No"], ["Linus", "No"], ["Margaret", "No"]]);

        var merged = MailMerge.MergeAllWithRules(template, data, new MergeState());

        merged.Select(document => document.PlainText).Should().Equal("Ada", "Linus", "Margaret");
    }

    [Fact]
    public void MergeRecordWithRules_NextRecordIf_PreservesAdvanceRequestForCaller()
    {
        var template = new TextDocument();
        var paragraph = new Paragraph();
        var instruction = MergeRuleEvaluator.BuildNextRecordIfInstruction(
            "Type", MergeConditionOperator.Equal, "Header");
        paragraph.Runs.Add(new Run(
            $"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}{MailMerge.FieldOpen}Name{MailMerge.FieldClose}"));
        template.Blocks.Add(paragraph);

        var state = new MergeState();
        var merged = MailMerge.MergeRecordWithRules(
            template,
            new Dictionary<string, string> { ["Type"] = "Header", ["Name"] = "Section A" },
            state,
            recordIndex: 1);

        merged.PlainText.Should().Be("Section A");
        state.AdvanceRecordRequested.Should().BeTrue();
        state.SkipRecordRequested.Should().BeFalse();
    }

    [Fact]
    public void MergeRecordWithRules_ResetsPriorRecordOutcomeBeforeCloning()
    {
        var advanceTemplate = new TextDocument();
        var advanceParagraph = new Paragraph();
        var instruction = MergeRuleEvaluator.BuildNextRecordIfInstruction(
            "Type", MergeConditionOperator.Equal, "Header");
        advanceParagraph.Runs.Add(new Run(
            $"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}"));
        advanceTemplate.Blocks.Add(advanceParagraph);

        var plainTemplate = new TextDocument();
        var plainParagraph = new Paragraph();
        plainParagraph.Runs.Add(new Run(
            $"{MailMerge.FieldOpen}Name{MailMerge.FieldClose}"));
        plainTemplate.Blocks.Add(plainParagraph);

        var state = new MergeState();
        var header = new Dictionary<string, string> { ["Type"] = "Header", ["Name"] = "Section A" };
        MailMerge.MergeRecordWithRules(advanceTemplate, header, state, recordIndex: 1);
        state.AdvanceRecordRequested.Should().BeTrue();

        var data = new Dictionary<string, string> { ["Type"] = "Data", ["Name"] = "Ada" };
        var merged = MailMerge.MergeRecordWithRules(plainTemplate, data, state, recordIndex: 2);

        merged.PlainText.Should().Be("Ada");
        state.AdvanceRecordRequested.Should().BeFalse();
        state.SkipRecordRequested.Should().BeFalse();
    }

    // ── SubstituteSpecialWithRules — unit tests ──────────────────────────────────────────────────

    [Fact]
    public void SubstituteSpecialWithRules_MergeSequenceNumber_EmitsSequenceNumber()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState { SequenceNumber = 5 };

        var result = MailMerge.SubstituteSpecialWithRules(
            $"{MailMerge.FieldOpen}{MailMerge.MergeSequenceNumberField}{MailMerge.FieldClose}",
            row, state, recordIndex: 7, out var advance, out var skip);

        result.Should().Be("5");
        advance.Should().BeFalse();
        skip.Should().BeFalse();
    }

    [Fact]
    public void SubstituteSpecialWithRules_SkipRule_SetsSkipFlag()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X"] = "Y" };
        var state = new MergeState();
        var instruction = MergeRuleEvaluator.BuildSkipRecordIfInstruction("X", MergeConditionOperator.Equal, "Y");

        MailMerge.SubstituteSpecialWithRules(
            $"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}",
            row, state, recordIndex: 1, out _, out var skip);

        skip.Should().BeTrue();
    }

    [Fact]
    public void SubstituteSpecialWithRules_MergeRecordNumber_StillWorks()
    {
        var row = new Dictionary<string, string>();
        var state = new MergeState();

        var result = MailMerge.SubstituteSpecialWithRules(
            $"Record {MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose}",
            row, state, recordIndex: 4, out _, out _);

        result.Should().Be("Record 4");
    }
}
