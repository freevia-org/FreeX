using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorksheetRepeatRange? ToRepeatRange(RepeatRangeDto? dto) =>
        dto is null ? null : new WorksheetRepeatRange(dto.Start, dto.End);

    private static RepeatRangeDto? FromRepeatRange(WorksheetRepeatRange? range) =>
        range is null ? null : new RepeatRangeDto { Start = range.Value.Start, End = range.Value.End };

    private static RepeatRangeDto? FromValidRepeatRange(WorksheetRepeatRange? range, uint max) =>
        range is { } value && value.Start >= 1 && value.End >= value.Start && value.End <= max
            ? new RepeatRangeDto { Start = value.Start, End = value.End }
            : null;

    private static PageMarginsDto FromPageMargins(WorksheetPageMargins margins) =>
        new()
        {
            Left = margins.Left,
            Right = margins.Right,
            Top = margins.Top,
            Bottom = margins.Bottom
        };

    private static WorksheetHeaderFooter ToHeaderFooter(HeaderFooterDto? dto) =>
        dto is null
            ? new WorksheetHeaderFooter("", "", "")
            : new WorksheetHeaderFooter(dto.Left ?? "", dto.Center ?? "", dto.Right ?? "");

    private static HeaderFooterDto FromHeaderFooter(WorksheetHeaderFooter value) =>
        new() { Left = value.Left, Center = value.Center, Right = value.Right };

    private static WorksheetHeaderFooterPictureSet ToHeaderFooterPictures(HeaderFooterPictureSetDto? dto) =>
        dto is null
            ? WorksheetHeaderFooterPictureSet.Empty
            : new WorksheetHeaderFooterPictureSet(
                ToHeaderFooterPicture(dto.Left),
                ToHeaderFooterPicture(dto.Center),
                ToHeaderFooterPicture(dto.Right));

    private static HeaderFooterPictureSetDto? FromHeaderFooterPictures(WorksheetHeaderFooterPictureSet value) =>
        value.Left is null && value.Center is null && value.Right is null
            ? null
            : new HeaderFooterPictureSetDto
            {
                Left = FromHeaderFooterPicture(value.Left),
                Center = FromHeaderFooterPicture(value.Center),
                Right = FromHeaderFooterPicture(value.Right)
            };

    private static WorksheetHeaderFooterPicture? ToHeaderFooterPicture(HeaderFooterPictureDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.ImageBase64))
            return null;

        try
        {
            var bytes = Convert.FromBase64String(dto.ImageBase64);
            return new WorksheetHeaderFooterPicture(
                bytes,
                string.IsNullOrWhiteSpace(dto.ContentType) ? "image/png" : dto.ContentType,
                dto.FileName,
                NativeJsonValueSanitizer.PositiveFiniteOrDefault(dto.Width, 96),
                NativeJsonValueSanitizer.PositiveFiniteOrDefault(dto.Height, 48));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static HeaderFooterPictureDto? FromHeaderFooterPicture(WorksheetHeaderFooterPicture? value) =>
        value is null
            ? null
            : new HeaderFooterPictureDto
            {
                ImageBase64 = Convert.ToBase64String(value.ImageBytes),
                ContentType = value.ContentType,
                FileName = value.FileName,
                Width = NativeJsonValueSanitizer.PositiveFiniteOrDefault(value.Width, 96),
                Height = NativeJsonValueSanitizer.PositiveFiniteOrDefault(value.Height, 48)
            };
}
