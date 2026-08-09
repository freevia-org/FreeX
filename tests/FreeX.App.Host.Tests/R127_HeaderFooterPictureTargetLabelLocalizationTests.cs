using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Round 127: the header/footer picture target label (fed into the
/// "HeaderFooterPicture_FormatPictureToolTip"/"_TargetHasPictureStatus"/"_TargetHasNoPictureStatus"
/// tooltip and status format strings) used to be built by concatenating two independently
/// localized resx strings with a hardcoded English "scope section" word order
/// (HeaderFooterDialog.Pictures.cs, ActiveBoxLabel). That defeated real per-locale translations:
/// fr-FR and de-DE both have grammatically correct translations for the scope and section
/// fragments individually, but gluing them together in fixed English order produced text no
/// French or German user would consider valid. These tests exercise the real HeaderFooterDialog
/// (the actual product entry point a user reaches by opening Page Layout > Header/Footer >
/// Format Picture) under non-English UI cultures and assert the composite label now reflects
/// the locale's own HeaderFooterPicture_TargetLabelFormat resx entry rather than a hardcoded
/// scope-then-section order.
/// </summary>
public sealed class R127_HeaderFooterPictureTargetLabelLocalizationTests
{
    [Fact]
    public void R127_FrenchLocale_TargetLabelUsesTheFrLocaleFormatOrderNotHardcodedConcatenation()
    {
        StaTestRunner.Run(() =>
        {
            using var cultureScope = TestCultureScope.CurrentUICultureAndDefaultThreadUICulture("fr-FR");

            var dialog = new HeaderFooterDialog(new Sheet(SheetId.New(), "Sheet1"));
            dialog.Show();
            try
            {
                var status = DialogSourceTestSupport.GetPrivateField<TextBlock>(dialog, "PictureTargetStatusText");
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "HeaderLeftBox").Focus();

                // fr-FR overrides HeaderFooterPicture_TargetLabelFormat to "{1} ({0})", so the
                // section comes first with the scope parenthesized -- the reported defect
                // hardcoded "scope section" (scope, then section, bare space) regardless of
                // locale, which read as an invalid French noun phrase. The fr-FR resx sentence
                // itself uses a French non-breaking space (U+00A0) before the colon, hence the
                // U+00A0 escape rather than a plain ASCII space in this literal.
                var expected = "Cible : partie gauche (En-tête) n'a pas d'image.";
                status.Text.Should().Be(expected);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void R127_GermanLocale_TargetLabelUsesTheDeLocaleFormatOrderNotHardcodedConcatenation()
    {
        StaTestRunner.Run(() =>
        {
            using var cultureScope = TestCultureScope.CurrentUICultureAndDefaultThreadUICulture("de-DE");

            var dialog = new HeaderFooterDialog(new Sheet(SheetId.New(), "Sheet1"));
            dialog.Show();
            try
            {
                var status = DialogSourceTestSupport.GetPrivateField<TextBlock>(dialog, "PictureTargetStatusText");
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "HeaderLeftBox").Focus();

                // de-DE overrides HeaderFooterPicture_TargetLabelFormat to "{1} ({0})" too --
                // the reported defect hardcoded "Kopfzeile linker Abschnitt" (scope, then
                // section), which put the adjective "linker" after the noun it should modify.
                var expected = "Ziel: linker Abschnitt (Kopfzeile) hat kein Bild.";
                status.Text.Should().Be(expected);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void R127_EnglishLocale_TargetLabelIsUnchangedByTheResourceDrivenJoin()
    {
        // No-regression sibling: the neutral HeaderFooterPicture_TargetLabelFormat resx value is
        // "{0} {1}", so English behavior (and any locale that doesn't override the key) must be
        // byte-identical to the pre-fix hardcoded "{scope} {section}" concatenation.
        StaTestRunner.Run(() =>
        {
            using var cultureScope = TestCultureScope.CurrentUICultureAndDefaultThreadUICulture("en-US");

            var dialog = new HeaderFooterDialog(new Sheet(SheetId.New(), "Sheet1"));
            dialog.Show();
            try
            {
                var status = DialogSourceTestSupport.GetPrivateField<TextBlock>(dialog, "PictureTargetStatusText");
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "HeaderLeftBox").Focus();

                status.Text.Should().Be("Target: Header left section has no picture.");
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
