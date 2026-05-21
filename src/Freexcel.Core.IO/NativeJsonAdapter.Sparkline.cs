using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static SparklineModel? TryLoadSparkline(SparklineDto? sparklineDto, SheetId sheetId)
    {
        if (sparklineDto?.DataRange is null || sparklineDto.Location is null)
            return null;

        try
        {
            var dataRange = GridRange.Parse(sparklineDto.DataRange, sheetId);
            var location = CellAddress.Parse(sparklineDto.Location, sheetId);
            if (dataRange.Start.Sheet != sheetId || dataRange.End.Sheet != sheetId || location.Sheet != sheetId)
                return null;
            if (!Enum.IsDefined(sparklineDto.Kind))
                return null;

            return new SparklineModel
            {
                DataRange = dataRange,
                Location = location,
                Kind = sparklineDto.Kind
            };
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsSparklineOnSheet(SparklineModel sparkline, SheetId sheetId) =>
        sparkline.DataRange.Start.Sheet == sheetId &&
        sparkline.DataRange.End.Sheet == sheetId &&
        sparkline.Location.Sheet == sheetId;

    private static SparklineDto ToSparklineDto(SparklineModel sparkline) => new()
    {
        DataRange = sparkline.DataRange.ToString(),
        Location = sparkline.Location.ToA1(),
        Kind = sparkline.Kind
    };
}
