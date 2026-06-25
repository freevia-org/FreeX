using FluentAssertions;
using Free.Shared.Pdf.Skia;

namespace Free.Shared.Pdf.Tests;

/// <summary>
/// Verifies that <see cref="SkiaPdfAvailabilityHelper.IsSkiaUnavailable"/> matches exactly
/// the exception types that indicate a missing or non-loadable Skia native asset, and does
/// not swallow unrelated exceptions.
/// </summary>
public sealed class SkiaPdfAvailabilityHelperTests
{
    [Theory]
    [InlineData(typeof(DllNotFoundException))]
    [InlineData(typeof(TypeInitializationException))]
    [InlineData(typeof(PlatformNotSupportedException))]
    [InlineData(typeof(EntryPointNotFoundException))]
    [InlineData(typeof(BadImageFormatException))]
    public void IsSkiaUnavailable_ReturnsTrueForExpectedExceptionTypes(Type exceptionType)
    {
        var ex = CreateInstance(exceptionType);
        SkiaPdfAvailabilityHelper.IsSkiaUnavailable(ex).Should().BeTrue(
            because: $"{exceptionType.Name} signals Skia native-asset unavailability");
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentNullException))]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(Exception))]
    public void IsSkiaUnavailable_ReturnsFalseForUnrelatedExceptions(Type exceptionType)
    {
        var ex = CreateInstance(exceptionType);
        SkiaPdfAvailabilityHelper.IsSkiaUnavailable(ex).Should().BeFalse(
            because: $"{exceptionType.Name} is not a Skia-unavailability signal");
    }

    private static Exception CreateInstance(Type type)
    {
        // TypeInitializationException requires a type name + inner exception
        if (type == typeof(TypeInitializationException))
            return new TypeInitializationException("SomeType", new Exception("inner"));

        return (Exception)Activator.CreateInstance(type)!;
    }
}
