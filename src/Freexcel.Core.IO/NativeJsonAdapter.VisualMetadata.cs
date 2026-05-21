using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorksheetBackgroundImage? TryLoadWorksheetBackground(WorksheetBackgroundDto? dto)
    {
        if (dto is not { ImageBase64.Length: > 0 })
            return null;

        try
        {
            return new WorksheetBackgroundImage(
                Convert.FromBase64String(dto.ImageBase64),
                string.IsNullOrWhiteSpace(dto.ContentType) ? "image/png" : dto.ContentType,
                dto.FileName);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static WorksheetBackgroundDto? ToWorksheetBackgroundDto(WorksheetBackgroundImage? background) =>
        background is null
            ? null
            : new WorksheetBackgroundDto
            {
                ImageBase64 = Convert.ToBase64String(background.ImageBytes),
                ContentType = background.ContentType,
                FileName = background.FileName
            };

    private static void NormalizePictureCrop(PictureModel picture)
    {
        if (picture.CropLeft + picture.CropRight >= 1)
        {
            picture.CropLeft = 0;
            picture.CropRight = 0;
        }

        if (picture.CropTop + picture.CropBottom >= 1)
        {
            picture.CropTop = 0;
            picture.CropBottom = 0;
        }
    }
}
