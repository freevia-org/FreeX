using System.Reflection;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r316: an edit to a picture that came FROM a file must survive being saved back to one.
///
/// <para>A drawing loaded from .xlsx keeps its original XML and replays it on save, so a model edit
/// is dropped unless a save-time rewriter patches that specific field. This codebase has fixed that
/// class repeatedly, one field at a time, and has 176 test references to <c>IsSourceLoaded</c> --
/// all of them examples. What was missing is completeness: nothing said which fields are covered, so
/// a field added to the model joins the unpatched set silently.</para>
///
/// <para>So the field list is derived by reflection rather than typed out. Every scalar member is
/// either expected to survive or excluded by name with a reason; a new member belongs to neither
/// list and fails the census below until someone decides which it is. That is the point -- the
/// decision is forced at the time the field is added, when it is cheap.</para>
/// </summary>
public sealed class R316_SourceLoadedPictureEditsSurviveTests
{
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static IReadOnlyList<PropertyInfo> ScalarProperties() =>
        typeof(PictureModel).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .Where(p => IsScalar(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

    private static bool IsScalar(Type type) =>
        type.IsEnum || type == typeof(bool) || type == typeof(int) || type == typeof(uint)
        || type == typeof(long) || type == typeof(double) || type == typeof(string);

    private static object? DistinctValue(PropertyInfo property, object current)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var value = property.GetValue(current);

        if (type == typeof(bool)) return !(bool)(value ?? false);
        if (type == typeof(int)) return (value is int i ? i : 0) + 7;
        if (type == typeof(uint)) return (value is uint u ? u : 0u) + 7u;
        if (type == typeof(long)) return (value is long l ? l : 0L) + 7L;
        if (type == typeof(string)) return "r316-" + property.Name;
        if (type == typeof(double))
        {
            // A crop is a FRACTION of the image. The first version of this added 3.5 to every double
            // alike, so the crops were set to 3.5, clamped to 1 on the way out, and reported as four
            // lost edits. The value was invalid, not the writer.
            return property.Name.StartsWith("Crop", StringComparison.Ordinal)
                ? 0.25
                : (value is double d ? d : 0d) + 3.5;
        }

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().ToArray();
            return values.FirstOrDefault(v => !Equals(v, value)) ?? values[0];
        }

        return null;
    }

    /// <summary>
    /// Members whose value is DERIVED on load rather than replayed, so "the edit did not survive" is
    /// the correct behaviour and not a finding. Each is here with the reason it belongs here --
    /// r293's lesson, that a member in neither list cannot be judged loss-or-bug.
    /// </summary>
    private static readonly Dictionary<string, string> DerivedOnLoad = new(StringComparer.Ordinal)
    {
        [nameof(PictureModel.Kind)] =
            "classified from what the picture actually is; an image loads as Image whatever was set",
        [nameof(PictureModel.ContentType)] =
            "read from the image part itself, so a bogus content type is corrected rather than kept",
        [nameof(PictureModel.DrawingAnchorKind)] =
            "determined by the shape of the anchor element that was written, not stored separately",
        [nameof(PictureModel.SourceRowCount)] = "camera/linked-range geometry; meaningless for an image",
        [nameof(PictureModel.SourceColumnCount)] = "camera/linked-range geometry; meaningless for an image",
        [nameof(PictureModel.IsLinkedToSourceRange)] = "camera-picture state; meaningless for an image",
        [nameof(PictureModel.LinkedSourceSheetName)] = "camera-picture state; meaningless for an image",
        ["LinkedImageTarget"] = "external \"Link to File\" target; not set for an embedded image",
    };

    /// <summary>
    /// Members a source-loaded picture genuinely does NOT carry back to the file. Declared here so
    /// the loss is visible and counted rather than silent, and so a member that starts surviving
    /// fails this list and gets promoted.
    /// </summary>
    private static readonly Dictionary<string, string> DeclaredLost = new(StringComparer.Ordinal)
    {
        [nameof(PictureModel.Name)] =
            "Name is the IDENTITY key: XlsxSourceDrawingGeometryRewriter pairs a model with its "
            + "xdr:pic element by matching cNvPr@name, so a rename cannot also be written through it",
        [nameof(PictureModel.Title)] = "no save-time rewriter patches cNvPr@title (AltText/descr is patched)",
        [nameof(PictureModel.IsDecorative)] = "decorative marking is not written back onto replayed XML",
        [nameof(PictureModel.IsVisible)] = "cNvPr@hidden is not written back onto replayed XML",
        [nameof(PictureModel.LockAspectRatio)] = "the picture's lock flags are not written back onto replayed XML",
        [nameof(PictureModel.Locked)] = "the picture's lock flags are not written back onto replayed XML",
    };

    private static Workbook RoundTrip(Workbook workbook)
    {
        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return new XlsxFileAdapter().Load(stream);
    }

    [Fact]
    public void EveryScalarEditOnASourceLoadedPictureIsEitherKeptOrDeclaredLost()
    {
        var authored = new Workbook("Book1");
        var authoredSheet = authored.AddSheet("Sheet1");
        authoredSheet.Pictures.Add(new PictureModel
        {
            Name = "Picture 1",
            ImageBytes = Png,
            ContentType = "image/png",
        });

        var loaded = RoundTrip(authored);
        var picture = loaded.Sheets[0].Pictures.Should().ContainSingle(
            "the fixture depends on the picture surviving a plain round trip at all").Subject;
        picture.IsSourceLoaded.Should().BeTrue(
            "this test is about drawings that replay their original XML; if the flag is not set the "
            + "whole premise is gone and every assertion below is about the wrong code path");

        var edited = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in ScalarProperties())
        {
            if (property.Name is nameof(PictureModel.IsSourceLoaded))
                continue;

            if (DistinctValue(property, picture) is not { } value)
                continue;

            property.SetValue(picture, value);
            edited[property.Name] = value;
        }

        edited.Should().NotBeEmpty("an empty edit set would make this vacuous");

        var resaved = RoundTrip(loaded);
        var survivor = resaved.Sheets[0].Pictures.Should().ContainSingle().Subject;

        var unexpectedlyLost = new List<string>();
        var unexpectedlyKept = new List<string>();
        foreach (var (name, expected) in edited.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (DerivedOnLoad.ContainsKey(name))
                continue;

            var actual = typeof(PictureModel).GetProperty(name)!.GetValue(survivor);
            var kept = expected is double wanted && actual is double got
                ? Math.Abs(wanted - got) < 0.01   // sizes and offsets round-trip through EMUs
                : Equals(expected, actual);

            if (!kept && !DeclaredLost.ContainsKey(name))
                unexpectedlyLost.Add($"{name}: set [{expected}] read back [{actual}]");
            else if (kept && DeclaredLost.ContainsKey(name))
                unexpectedlyKept.Add(name);
        }

        unexpectedlyLost.Should().BeEmpty(
            "a source-loaded drawing replays its original XML, so an edit no save-time rewriter "
            + "patches is silently discarded; a member that starts being dropped must be fixed or "
            + "declared:\n" + string.Join("\n", unexpectedlyLost));

        unexpectedlyKept.Should().BeEmpty(
            "these members are declared lost; one that now survives should be promoted out of "
            + $"DeclaredLost so the list keeps meaning something: {string.Join(", ", unexpectedlyKept)}");

        // The census: every scalar member is accounted for, so adding one forces the decision now
        // rather than leaving it to be discovered as a bug later.
        var accounted = DerivedOnLoad.Keys.Concat(DeclaredLost.Keys).ToHashSet(StringComparer.Ordinal);
        var unaccounted = ScalarProperties()
            .Select(property => property.Name)
            .Where(name => name != nameof(PictureModel.IsSourceLoaded))
            .Where(name => !accounted.Contains(name) && !edited.ContainsKey(name))
            .ToList();

        unaccounted.Should().BeEmpty(
            "every scalar member must be exercised, derived-on-load, or declared lost: "
            + string.Join(", ", unaccounted));
    }
    /// <summary>
    /// The serious version of the question. Name is not just a field: the geometry rewriter pairs a
    /// source-loaded picture with its physical xdr:pic element by matching cNvPr@name. A renamed
    /// picture therefore matches nothing and falls into POSITIONAL pairing among the leftovers -- so
    /// the risk is not a lost rename but a size edit landing on somebody else's picture.
    /// </summary>
    [Fact]
    public void RenamingOnePictureDoesNotMoveAnotherPicturesEditOntoIt()
    {
        var authored = new Workbook("Book1");
        var sheet = authored.AddSheet("Sheet1");
        foreach (var name in new[] { "Alpha", "Beta", "Gamma" })
        {
            sheet.Pictures.Add(new PictureModel
            {
                Name = name, ImageBytes = Png, ContentType = "image/png", Width = 100, Height = 100,
            });
        }

        var loaded = RoundTrip(authored);
        var pictures = loaded.Sheets[0].Pictures;
        pictures.Should().HaveCount(3);

        // Rename the FIRST, and resize the THIRD. If pairing degrades to position, the resize can be
        // written onto the wrong element.
        pictures[0].Name = "Renamed";
        pictures[2].Width = 321;
        pictures[2].Height = 123;

        var resaved = RoundTrip(loaded);
        var after = resaved.Sheets[0].Pictures;
        after.Should().HaveCount(3);

        var gamma = after.SingleOrDefault(p => p.Name == "Gamma");
        gamma.Should().NotBeNull("the picture that was never renamed must still be identifiable");
        gamma!.Width.Should().BeApproximately(321, 0.5,
            "the resize was applied to Gamma and must land on Gamma");
        gamma.Height.Should().BeApproximately(123, 0.5);

        foreach (var other in after.Where(p => p.Name != "Gamma"))
        {
            other.Width.Should().BeApproximately(100, 0.5,
                $"{other.Name} was not resized, so it must not have inherited Gamma's size");
        }
    }

}