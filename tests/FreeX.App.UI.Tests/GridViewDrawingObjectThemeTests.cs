using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using FreeX.App.UI;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewDrawingObjectThemeTests
{
    private static T GetStaticResource<T>(string fieldName)
    {
        var field = typeof(GridView).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();
        return field!.GetValue(null).Should().BeAssignableTo<T>().Subject;
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
