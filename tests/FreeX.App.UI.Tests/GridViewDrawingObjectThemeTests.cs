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

    private static string FindWorkspaceFile(params string[] relativeParts) =>
        WorkspaceFileLocator.Find(relativeParts);
}
