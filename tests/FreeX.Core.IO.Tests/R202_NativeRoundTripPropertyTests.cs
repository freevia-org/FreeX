using System.Reflection;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r202: closes the hole r201 left open and wrote down. Its coverage contract proves a DTO HAS a
/// member; it cannot tell whether both conversion directions use it, and deleting only the write
/// lines left it green.
/// <para>
/// The first attempt at closing that was a source check asking whether each member name appeared on
/// both sides of the adapter. It passed the very probe it existed to catch, because a member's own
/// DECLARATION mentions its name -- a name-appearance check cannot tell a declaration from a use.
/// That attempt was deleted rather than weakened into something that looks like a guard.
/// </para>
/// <para>
/// This is the behavioural version. For each scalar member of the model types the .fxl format
/// carries, it writes a distinctive non-default value, round-trips the workbook, and requires the
/// value back. A member that is declared but written from nothing, or read into nothing, fails --
/// which is exactly what the source check could not see.
/// </para>
/// <para>
/// Its limit, stated: it covers members whose type it can synthesise a value for (bool, string,
/// numbers, enums, and nullables of those). Collections and nested models are left to the
/// hand-written tests, and <see cref="R201_NativeDtoCoverageContractTests"/> still guards their
/// PRESENCE.
/// </para>
/// </summary>
public sealed class R202_NativeRoundTripPropertyTests
{
    /// <summary>
    /// Members this test cannot meaningfully drive, with the reason. Anything genuinely not
    /// serialized belongs in R201's IntentionallyNotSerialized instead -- this list is only for
    /// members whose value this test cannot choose freely.
    /// </summary>
    private static readonly Dictionary<string, string> NotDrivableHere = new()
    {
        ["Sheet.Name"] = "the workbook enforces uniqueness and sanitises it; covered by its own tests",
        ["Sheet.Id"] = "identity is reassigned by the loader by design",
        ["Workbook.Name"] = "set from the file name by the caller, not from the payload",
        ["Workbook.ActiveSheetIndex"] = "must stay in range of the sheets actually present",
        ["Workbook.FilePath"] = "not serialized (see R201's exemption list)",
        ["Workbook.HasPendingManualRecalculation"] = "not serialized (see R201's exemption list)",
        ["Workbook.NextStructuredTableIdWatermark"] = "deliberately not persisted -- see R109",
        ["Cell.CachedAst"] = "an opaque parse cache, not serialized",
        ["Cell.FormulaText"] = "drives Value and the array fields; covered by dedicated tests",
        ["Cell.Value"] = "a ScalarValue, not a scalar this test can synthesise",

        // Found by this test's first run. None is a defect: each is a documented behaviour that a
        // free choice of value cannot satisfy, and each was traced to the code that causes it.
        ["Cell.ArrayMode"] =
            "only written when the cell HAS a formula (Save.cs writes Dynamic otherwise), and this "
            + "test drives one member at a time on a value cell",
        ["Sheet.ZoomPercent"] =
            "NativeJsonValueSanitizer.ValidZoomPercentOrDefault clamps out-of-range zoom on load",
        ["Workbook.SheetTabRatio"] = "sanitised to 0..1000 on both save and load",
        ["Workbook.FirstVisibleSheetIndex"] = "sanitised to the range of sheets actually present",
        ["Sheet.ProtectionPassword"] =
            "deliberately dropped unless IsProtected -- a password for an unprotected sheet is not "
            + "state worth keeping",
        ["Workbook.StructureProtectionPassword"] =
            "StoreProtectionPassword hashes it on save, so a plaintext value cannot come back "
            + "unchanged -- by design, see the comment above that helper",
    };

    public static TheoryData<string, string> DrivableMembers()
    {
        var data = new TheoryData<string, string>();
        foreach (var (owner, member) in Enumerate())
            data.Add(owner, member);
        return data;
    }

    [Theory]
    [MemberData(nameof(DrivableMembers))]
    public void AScalarMemberSurvivesANativeRoundTrip(string owner, string member)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("seed")));

        var (target, property) = Resolve(workbook, sheet, owner, member);
        var expected = DistinctiveValue(property.PropertyType);
        property.SetValue(target, expected);

        var reopened = RoundTrip(workbook);
        var (reopenedTarget, _) = Resolve(reopened, reopened.Sheets[0], owner, member);

        property.GetValue(reopenedTarget).Should().Be(
            expected,
            "{0}.{1} must survive a .fxl round trip -- autosave and crash recovery use this adapter "
            + "exclusively, so anything lost here is lost on a recovered document",
            owner,
            member);
    }

    [Fact]
    public void TheEnumerationActuallyFindsMembersToDrive()
    {
        // Without this, a pairing that silently returns nothing would make the theory above pass
        // with zero cases and guard nothing at all.
        Enumerate().Should().HaveCountGreaterThan(40);
    }

    [Fact]
    public void EveryNotDrivableEntryStillNamesALiveMember()
    {
        var stale = NotDrivableHere.Keys
            .Where(key =>
            {
                var parts = key.Split('.');
                var type = OwnerType(parts[0]);
                return type is null || type.GetProperty(parts[1]) is null;
            })
            .ToList();

        stale.Should().BeEmpty("remove entries whose member is gone");
    }

    private static List<(string Owner, string Member)> Enumerate()
    {
        var found = new List<(string, string)>();
        foreach (var owner in new[] { "Workbook", "Sheet", "Cell" })
        {
            foreach (var property in OwnerType(owner)!.GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanWrite || property.SetMethod?.IsPublic != true)
                    continue;
                if (NotDrivableHere.ContainsKey($"{owner}.{property.Name}"))
                    continue;
                if (!CanSynthesise(property.PropertyType))
                    continue;

                found.Add((owner, property.Name));
            }
        }

        return found;
    }

    private static Type? OwnerType(string owner) => owner switch
    {
        "Workbook" => typeof(Workbook),
        "Sheet" => typeof(Sheet),
        "Cell" => typeof(Cell),
        _ => null,
    };

    private static (object Target, PropertyInfo Property) Resolve(
        Workbook workbook, Sheet sheet, string owner, string member)
    {
        object target = owner switch
        {
            "Workbook" => workbook,
            "Sheet" => sheet,
            _ => sheet.GetCell(new CellAddress(sheet.Id, 1, 1))!,
        };

        return (target, OwnerType(owner)!.GetProperty(member)!);
    }

    private static bool CanSynthesise(Type type)
    {
        var bare = Nullable.GetUnderlyingType(type) ?? type;
        return bare == typeof(bool)
               || bare == typeof(string)
               || bare == typeof(int)
               || bare == typeof(uint)
               || bare == typeof(double)
               || bare.IsEnum;
    }

    /// <summary>A value no default would produce, so a dropped member cannot pass by coincidence.</summary>
    private static object DistinctiveValue(Type type)
    {
        var bare = Nullable.GetUnderlyingType(type) ?? type;
        if (bare == typeof(bool)) return true;
        if (bare == typeof(string)) return "r202-distinctive";
        if (bare == typeof(int)) return 4242;
        if (bare == typeof(uint)) return 4242u;
        if (bare == typeof(double)) return 42.25;

        // The last enum value, so a member left at its default cannot match by accident.
        var values = Enum.GetValues(bare);
        return values.GetValue(values.Length - 1)!;
    }

    private static Workbook RoundTrip(Workbook workbook)
    {
        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }
}
