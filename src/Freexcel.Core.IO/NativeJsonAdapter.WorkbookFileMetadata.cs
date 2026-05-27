using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorkbookFileSharingModel? ToWorkbookFileSharing(WorkbookFileSharingDto? dto)
    {
        if (dto is null)
            return null;

        var userName = string.IsNullOrWhiteSpace(dto.UserName) ? null : dto.UserName;
        var reservationPassword = string.IsNullOrWhiteSpace(dto.ReservationPassword) ? null : dto.ReservationPassword;
        if (dto.ReadOnlyRecommended is null &&
            userName is null &&
            reservationPassword is null)
        {
            return null;
        }

        return new WorkbookFileSharingModel
        {
            ReadOnlyRecommended = dto.ReadOnlyRecommended,
            UserName = userName,
            ReservationPassword = reservationPassword
        };
    }

    private static WorkbookFileVersionModel? ToWorkbookFileVersion(WorkbookFileVersionDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = (dto.NativeAttributes ?? new Dictionary<string, string>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var appName = string.IsNullOrWhiteSpace(dto.AppName) ? null : dto.AppName;
        var lastEdited = string.IsNullOrWhiteSpace(dto.LastEdited) ? null : dto.LastEdited;
        var lowestEdited = string.IsNullOrWhiteSpace(dto.LowestEdited) ? null : dto.LowestEdited;
        var rupBuild = string.IsNullOrWhiteSpace(dto.RupBuild) ? null : dto.RupBuild;
        var codeName = string.IsNullOrWhiteSpace(dto.CodeName) ? null : dto.CodeName;
        if (appName is null &&
            lastEdited is null &&
            lowestEdited is null &&
            rupBuild is null &&
            codeName is null &&
            nativeAttributes.Count == 0)
        {
            return null;
        }

        return new WorkbookFileVersionModel
        {
            AppName = appName,
            LastEdited = lastEdited,
            LowestEdited = lowestEdited,
            RupBuild = rupBuild,
            CodeName = codeName,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorkbookFileRecoveryPropertiesModel? ToWorkbookFileRecoveryProperties(WorkbookFileRecoveryPropertiesDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = (dto.NativeAttributes ?? new Dictionary<string, string>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (dto.AutoRecover is null &&
            dto.CrashSave is null &&
            dto.DataExtractLoad is null &&
            dto.RepairLoad is null &&
            nativeAttributes.Count == 0)
        {
            return null;
        }

        return new WorkbookFileRecoveryPropertiesModel
        {
            AutoRecover = dto.AutoRecover,
            CrashSave = dto.CrashSave,
            DataExtractLoad = dto.DataExtractLoad,
            RepairLoad = dto.RepairLoad,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorkbookFileSharingDto? FromWorkbookFileSharing(WorkbookFileSharingModel? model)
    {
        if (model is null)
            return null;

        var userName = string.IsNullOrWhiteSpace(model.UserName) ? null : model.UserName;
        var reservationPassword = string.IsNullOrWhiteSpace(model.ReservationPassword) ? null : model.ReservationPassword;
        if (model.ReadOnlyRecommended is null &&
            userName is null &&
            reservationPassword is null)
        {
            return null;
        }

        return new WorkbookFileSharingDto
        {
            ReadOnlyRecommended = model.ReadOnlyRecommended,
            UserName = userName,
            ReservationPassword = reservationPassword
        };
    }

    private static WorkbookFileVersionDto? FromWorkbookFileVersion(WorkbookFileVersionModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = (model.NativeAttributes ?? new Dictionary<string, string>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var appName = string.IsNullOrWhiteSpace(model.AppName) ? null : model.AppName;
        var lastEdited = string.IsNullOrWhiteSpace(model.LastEdited) ? null : model.LastEdited;
        var lowestEdited = string.IsNullOrWhiteSpace(model.LowestEdited) ? null : model.LowestEdited;
        var rupBuild = string.IsNullOrWhiteSpace(model.RupBuild) ? null : model.RupBuild;
        var codeName = string.IsNullOrWhiteSpace(model.CodeName) ? null : model.CodeName;
        if (appName is null &&
            lastEdited is null &&
            lowestEdited is null &&
            rupBuild is null &&
            codeName is null &&
            nativeAttributes.Count == 0)
        {
            return null;
        }

        return new WorkbookFileVersionDto
        {
            AppName = appName,
            LastEdited = lastEdited,
            LowestEdited = lowestEdited,
            RupBuild = rupBuild,
            CodeName = codeName,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorkbookFileRecoveryPropertiesDto? FromWorkbookFileRecoveryProperties(WorkbookFileRecoveryPropertiesModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = (model.NativeAttributes ?? new Dictionary<string, string>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (model.AutoRecover is null &&
            model.CrashSave is null &&
            model.DataExtractLoad is null &&
            model.RepairLoad is null &&
            nativeAttributes.Count == 0)
        {
            return null;
        }

        return new WorkbookFileRecoveryPropertiesDto
        {
            AutoRecover = model.AutoRecover,
            CrashSave = model.CrashSave,
            DataExtractLoad = model.DataExtractLoad,
            RepairLoad = model.RepairLoad,
            NativeAttributes = nativeAttributes
        };
    }
}
