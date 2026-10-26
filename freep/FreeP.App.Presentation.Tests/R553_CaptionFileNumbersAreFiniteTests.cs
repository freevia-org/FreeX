using System.Text;
using FluentAssertions;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r553: a caption track's cue settings are parsed straight out of a WebVTT FILE embedded in the
/// .pptx, so every number on a <c>--&gt;</c> line is file-controlled. Two of those parsers used
/// the REJECT form of a range check that r551 showed cannot stop a non-finite value:
///
///   percent is &lt; 0 or &gt; 100   rejects Infinity, but NaN fails BOTH comparisons and passes
///   size &lt;= 0                    rejects neither, and its regex allows unlimited digits
///
/// <para>NumberStyles.Float parses the literal "NaN", and a several-hundred-digit run overflows to
/// Infinity even where an exponent is forbidden -- the same overflow route r550 found in
/// ZoomPercentPolicy. A non-finite position or size then reaches caption layout.</para>
///
/// <para>The remedy is each site's own existing contract: a setting that cannot be used is
/// dropped, which is exactly what these functions already did for text that failed to parse.</para>
/// </summary>
public sealed class R553_CaptionFileNumbersAreFiniteTests
{
    private static PresentationMediaTranscriptPlan PlanFor(string vtt)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 42,
            Name = "Video",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                IsVideo = true,
                CaptionTracks =
                {
                    new MediaCaptionTrackInfo
                    {
                        Source = "ppt/media/captions.vtt",
                        ContentType = "text/vtt",
                        Language = "en-US",
                        Label = "English",
                        Bytes = Encoding.UTF8.GetBytes(vtt),
                    },
                },
            },
        });

        return PresentationMediaTranscriptPlanner.BuildTranscriptPlan(presentation);
    }

    private static string CueWithSettings(string settings) =>
        "WEBVTT\n\n00:00.000 --> 00:01.500 " + settings + "\nCaption text";

    [Theory]
    [InlineData("position:NaN%")]
    [InlineData("line:NaN%")]
    [InlineData("position:NaN% line:NaN%")]
    public void A_cue_position_that_is_not_finite_is_dropped(string settings)
    {
        var cue = PlanFor(CueWithSettings(settings)).Tracks[0].Cues[0];

        // Not "equals some substitute": the contract is that an unusable setting is dropped, so
        // whatever survives must be a number layout can actually use.
        (cue.PositionPercent is null || double.IsFinite(cue.PositionPercent.Value)).Should().BeTrue(
            "PositionPercent was " + cue.PositionPercent);
        (cue.LinePercent is null || double.IsFinite(cue.LinePercent.Value)).Should().BeTrue(
            "LinePercent was " + cue.LinePercent);
    }

    [Fact]
    public void A_cue_position_of_many_digits_overflows_and_is_dropped()
    {
        // No exponent needed: 400 digits overflow to Infinity on parse. This is the route that
        // survives a regex or NumberStyles restriction forbidding "1e400".
        var huge = new string('9', 400);
        var cue = PlanFor(CueWithSettings("position:" + huge + "%")).Tracks[0].Cues[0];

        (cue.PositionPercent is null || double.IsFinite(cue.PositionPercent.Value)).Should().BeTrue(
            "PositionPercent was " + cue.PositionPercent);
    }

    [Fact]
    public void Ordinary_cue_settings_are_still_read()
    {
        // Non-vacuity: a guard that dropped every setting would satisfy the assertions above
        // while silently discarding real caption positioning.
        var cue = PlanFor(CueWithSettings("position:25% line:80%")).Tracks[0].Cues[0];

        cue.PositionPercent.Should().Be(25);
        cue.LinePercent.Should().Be(80);
    }

    [Fact]
    public void An_ordinary_cue_still_parses_at_all()
    {
        // Guards the fixture itself: if the VTT shape were wrong every test above would pass
        // vacuously on an empty cue list.
        var plan = PlanFor(CueWithSettings("position:25%"));

        plan.CueCount.Should().Be(1);
        plan.Tracks[0].Cues[0].Text.Should().Be("Caption text");
    }
}
