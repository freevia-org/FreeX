namespace FreeW.Core.Model.Tests;

public class ChangeCaseTests
{
    // --- Upper ---

    [Theory]
    [InlineData("Hello World", "HELLO WORLD")]
    [InlineData("already UPPER", "ALREADY UPPER")]
    [InlineData("mixed123!?", "MIXED123!?")]
    public void Upper_UpperCasesEveryLetter(string input, string expected) =>
        ChangeCase.Apply(input, CaseKind.Upper).Should().Be(expected);

    // --- Lower ---

    [Theory]
    [InlineData("Hello World", "hello world")]
    [InlineData("already lower", "already lower")]
    [InlineData("MIXED123!?", "mixed123!?")]
    public void Lower_LowerCasesEveryLetter(string input, string expected) =>
        ChangeCase.Apply(input, CaseKind.Lower).Should().Be(expected);

    // --- Sentence ---

    [Fact]
    public void Sentence_CapitalisesFirstLetterOfStringAndEachSentence()
    {
        ChangeCase.Apply("hello world. how are you? fine! thanks", CaseKind.Sentence)
            .Should().Be("Hello world. How are you? Fine! Thanks");
    }

    [Fact]
    public void Sentence_LowersTheRestOfEachSentence()
    {
        ChangeCase.Apply("HELLO WORLD. GOODBYE", CaseKind.Sentence)
            .Should().Be("Hello world. Goodbye");
    }

    [Fact]
    public void Sentence_LeadingPunctuationStillCapitalisesFirstLetter()
    {
        ChangeCase.Apply("   hello. bye", CaseKind.Sentence)
            .Should().Be("   Hello. Bye");
    }

    [Fact]
    public void Sentence_HandlesTerminatorWithoutFollowingSpace()
    {
        ChangeCase.Apply("one.two.three", CaseKind.Sentence)
            .Should().Be("One.Two.Three");
    }

    // --- Capitalize (title-ish) ---

    [Fact]
    public void Capitalize_UpperCasesFirstLetterOfEachWord()
    {
        ChangeCase.Apply("the quick brown fox", CaseKind.Capitalize)
            .Should().Be("The Quick Brown Fox");
    }

    [Fact]
    public void Capitalize_LowersTheRestOfEachWord()
    {
        ChangeCase.Apply("hELLO wORLD", CaseKind.Capitalize)
            .Should().Be("Hello World");
    }

    [Fact]
    public void Capitalize_LeadingPunctuationOnAWordStillCapitalisesFirstLetter()
    {
        ChangeCase.Apply("(hello) world", CaseKind.Capitalize)
            .Should().Be("(Hello) World");
    }

    [Fact]
    public void Capitalize_PreservesWhitespaceRuns()
    {
        ChangeCase.Apply("a\tb  c", CaseKind.Capitalize)
            .Should().Be("A\tB  C");
    }

    // --- Toggle ---

    [Theory]
    [InlineData("Hello World", "hELLO wORLD")]
    [InlineData("hELLO wORLD", "Hello World")]
    [InlineData("ABC123xyz", "abc123XYZ")]
    public void Toggle_InvertsEachLetterCase(string input, string expected) =>
        ChangeCase.Apply(input, CaseKind.Toggle).Should().Be(expected);

    [Fact]
    public void Toggle_LeavesNonLettersUnchanged()
    {
        ChangeCase.Apply("12:34 - !?", CaseKind.Toggle)
            .Should().Be("12:34 - !?");
    }

    // --- Edge cases shared across kinds ---

    [Theory]
    [InlineData(CaseKind.Upper)]
    [InlineData(CaseKind.Lower)]
    [InlineData(CaseKind.Sentence)]
    [InlineData(CaseKind.Capitalize)]
    [InlineData(CaseKind.Toggle)]
    public void Apply_EmptyString_ReturnsEmpty(CaseKind kind) =>
        ChangeCase.Apply(string.Empty, kind).Should().Be(string.Empty);

    [Theory]
    [InlineData(CaseKind.Upper)]
    [InlineData(CaseKind.Lower)]
    [InlineData(CaseKind.Sentence)]
    [InlineData(CaseKind.Capitalize)]
    [InlineData(CaseKind.Toggle)]
    public void Apply_PunctuationOnly_IsUnchanged(CaseKind kind) =>
        ChangeCase.Apply("...!?  -- 123", kind).Should().Be("...!?  -- 123");

    [Fact]
    public void Apply_NullText_Throws() =>
        ((Action)(() => ChangeCase.Apply(null!, CaseKind.Upper))).Should().Throw<ArgumentNullException>();

    [Fact]
    public void Apply_IsDeterministic_SameInputSameOutput()
    {
        const string input = "tHe Quick. brown FOX? jumps!";
        var first = ChangeCase.Apply(input, CaseKind.Sentence);
        var second = ChangeCase.Apply(input, CaseKind.Sentence);
        second.Should().Be(first);
    }
}
