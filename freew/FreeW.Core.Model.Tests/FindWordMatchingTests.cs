namespace FreeW.Core.Model.Tests;

public class FindWordMatchingTests
{
    private static readonly TextSearchOptions Phonetic = new(SoundsLike: true);
    private static readonly TextSearchOptions Forms = new(FindAllWordForms: true);

    private static List<(int Start, int Length)> Find(string haystack, string needle, TextSearchOptions options) =>
        TextSearch.FindAll(haystack, needle, options).ToList();

    // ---------------------------------------------------------------------------------------------
    // Sounds like (Metaphone)
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Smith", "SM0")]
    [InlineData("Smyth", "SM0")]
    [InlineData("knight", "NT")]
    [InlineData("night", "NT")]
    [InlineData("phone", "FN")]
    [InlineData("Thomas", "0MS")]
    [InlineData("school", "SKL")]
    [InlineData("Stephen", "STFN")]
    [InlineData("write", "RT")]
    [InlineData("Catherine", "K0RN")]
    [InlineData("John", "JN")]
    [InlineData("Xavier", "SFR")]
    [InlineData("suggest", "SJST")]
    [InlineData("church", "XRX")]
    [InlineData("Christ", "KRST")]
    [InlineData("laugh", "LF")]
    [InlineData("wrap", "RP")]
    [InlineData("gnat", "NT")]
    [InlineData("Aeon", "EN")]
    [InlineData("whale", "WL")]
    public void PhoneticKey_FollowsMetaphoneRules(string word, string expected)
    {
        FindWordMatching.PhoneticKey(word).Should().Be(expected);
    }

    [Fact]
    public void PhoneticKey_IsNullForNonLatinOrDigitWords()
    {
        FindWordMatching.PhoneticKey("Привет").Should().BeNull();
        FindWordMatching.PhoneticKey("abc123").Should().BeNull();
        FindWordMatching.PhoneticKey("").Should().BeNull();
    }

    [Fact]
    public void PhoneticKey_FoldsDiacriticsAndApostrophes()
    {
        FindWordMatching.PhoneticKey("Jörg").Should().Be(FindWordMatching.PhoneticKey("Jorg"));
        FindWordMatching.PhoneticKey("O'Neil").Should().Be(FindWordMatching.PhoneticKey("Oneil"));
    }

    [Theory]
    [InlineData("Smith", "Smyth")]
    [InlineData("night", "knight")]
    [InlineData("phone", "fone")]
    [InlineData("write", "right")]
    [InlineData("write", "rite")]
    [InlineData("cite", "sight")]
    [InlineData("cite", "site")]
    [InlineData("Stephen", "Steven")]
    [InlineData("Catherine", "Katherine")]
    [InlineData("John", "Jon")]
    [InlineData("there", "their")]
    [InlineData("meat", "meet")]
    [InlineData("bare", "bear")]
    [InlineData("wrap", "rap")]
    [InlineData("gnat", "nat")]
    [InlineData("SMITH", "smith")]
    [InlineData("66", "66")]
    public void SoundsLike_RelatesWordsThatSoundAlike(string a, string b)
    {
        FindWordMatching.SoundsLike(a, b).Should().BeTrue();
        FindWordMatching.SoundsLike(b, a).Should().BeTrue();
    }

    [Theory]
    [InlineData("cat", "dog")]
    [InlineData("night", "light")]
    [InlineData("phone", "pine")]
    [InlineData("tree", "three")]
    [InlineData("ship", "sip")]
    [InlineData("chair", "hair")]
    [InlineData("knife", "life")]
    [InlineData("Smith", "Smithson")]
    [InlineData("Robert", "Rupert")]
    [InlineData("Привет", "Privet")]
    [InlineData("route66", "route67")]
    public void SoundsLike_KeepsDifferentSoundingWordsApart(string a, string b)
    {
        FindWordMatching.SoundsLike(a, b).Should().BeFalse();
        FindWordMatching.SoundsLike(b, a).Should().BeFalse();
    }

    [Fact]
    public void SoundsLike_FindsHomophoneSpellingsAsWholeWords()
    {
        Find("Smith and Smyth met Jones", "Smith", Phonetic)
            .Should().Equal((0, 5), (10, 5));
        // A substring of a longer word is not a phonetic hit.
        Find("Smithson", "Smith", Phonetic).Should().BeEmpty();
    }

    [Fact]
    public void SoundsLike_IsCaseInsensitive_AndMatchesWordsWithTheSameText()
    {
        Find("SMITH smith", "Smith", Phonetic).Should().Equal((0, 5), (6, 5));
        Find("route 66", "66", Phonetic).Should().Equal((6, 2));
    }

    [Fact]
    public void SoundsLike_MultiWordNeedleNeedsConsecutiveWordsOnTheSameLine()
    {
        Find("john smith here", "jon smyth", Phonetic).Should().Equal((0, 10));
        Find("john\rsmith", "jon smyth", Phonetic).Should().BeEmpty();
        Find("john, smith", "jon smyth", Phonetic).Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------
    // Find all word forms
    // ---------------------------------------------------------------------------------------------

    [Theory]
    // regular -s / -es / -ies / -ves
    [InlineData("box", "boxes")]
    [InlineData("church", "churches")]
    [InlineData("wish", "wishes")]
    [InlineData("city", "cities")]
    [InlineData("study", "studies")]
    [InlineData("wolf", "wolves")]
    [InlineData("life", "lives")]
    [InlineData("knife", "knives")]
    [InlineData("leaf", "leaves")]
    [InlineData("quiz", "quizzes")]
    [InlineData("tomato", "tomatoes")]
    // -ed / -ing: e-drop, doubling, y -> i, ie -> y, c -> ck
    [InlineData("walk", "walked")]
    [InlineData("walked", "walking")]
    [InlineData("hope", "hoped")]
    [InlineData("hope", "hoping")]
    [InlineData("stop", "stopped")]
    [InlineData("stop", "stopping")]
    [InlineData("plan", "planned")]
    [InlineData("study", "studied")]
    [InlineData("study", "studying")]
    [InlineData("die", "dying")]
    [InlineData("die", "died")]
    [InlineData("tie", "tying")]
    [InlineData("agree", "agreeing")]
    [InlineData("agree", "agreed")]
    [InlineData("dye", "dyeing")]
    [InlineData("panic", "panicking")]
    [InlineData("panic", "panicked")]
    [InlineData("refer", "referred")]
    [InlineData("occur", "occurring")]
    [InlineData("visit", "visited")]
    [InlineData("open", "opening")]
    [InlineData("travel", "travelled")]
    [InlineData("travel", "traveled")]
    [InlineData("travel", "travelling")]
    [InlineData("quit", "quitting")]
    // comparatives and superlatives
    [InlineData("happy", "happier")]
    [InlineData("happy", "happiest")]
    [InlineData("big", "bigger")]
    [InlineData("big", "biggest")]
    [InlineData("hot", "hotter")]
    [InlineData("nice", "nicer")]
    [InlineData("nice", "nicest")]
    [InlineData("dry", "drier")]
    [InlineData("dry", "driest")]
    [InlineData("cool", "cooler")]
    [InlineData("late", "latest")]
    [InlineData("new", "newer")]
    [InlineData("tall", "tallest")]
    [InlineData("light", "lighter")]
    // irregular verbs
    [InlineData("run", "runs")]
    [InlineData("run", "running")]
    [InlineData("run", "ran")]
    [InlineData("make", "made")]
    [InlineData("make", "making")]
    [InlineData("go", "went")]
    [InlineData("go", "gone")]
    [InlineData("go", "goes")]
    [InlineData("go", "going")]
    [InlineData("is", "were")]
    [InlineData("be", "been")]
    [InlineData("am", "being")]
    [InlineData("have", "had")]
    [InlineData("have", "has")]
    [InlineData("do", "done")]
    [InlineData("do", "does")]
    [InlineData("eat", "ate")]
    [InlineData("eat", "eaten")]
    [InlineData("write", "wrote")]
    [InlineData("write", "written")]
    [InlineData("write", "writing")]
    [InlineData("speak", "spoken")]
    [InlineData("swim", "swam")]
    [InlineData("swim", "swum")]
    [InlineData("swim", "swimming")]
    [InlineData("drink", "drank")]
    [InlineData("drink", "drunk")]
    [InlineData("ring", "rang")]
    [InlineData("sing", "sung")]
    [InlineData("bring", "brought")]
    [InlineData("catch", "caught")]
    [InlineData("teach", "taught")]
    [InlineData("think", "thought")]
    [InlineData("buy", "bought")]
    [InlineData("fight", "fought")]
    [InlineData("seek", "sought")]
    [InlineData("sell", "sold")]
    [InlineData("lend", "lent")]
    [InlineData("lay", "laid")]
    [InlineData("lie", "lain")]
    [InlineData("lie", "lying")]
    [InlineData("leave", "left")]
    [InlineData("leave", "leaving")]
    [InlineData("hide", "hidden")]
    [InlineData("shake", "shaken")]
    [InlineData("steal", "stolen")]
    [InlineData("wear", "worn")]
    [InlineData("tear", "tore")]
    [InlineData("throw", "threw")]
    [InlineData("grow", "grown")]
    [InlineData("know", "knew")]
    [InlineData("draw", "drawn")]
    [InlineData("fly", "flew")]
    [InlineData("fly", "flown")]
    [InlineData("freeze", "frozen")]
    [InlineData("forgive", "forgave")]
    [InlineData("forget", "forgotten")]
    [InlineData("forget", "forgetting")]
    [InlineData("begin", "began")]
    [InlineData("begin", "begun")]
    [InlineData("begin", "beginning")]
    [InlineData("get", "got")]
    [InlineData("get", "gotten")]
    [InlineData("get", "getting")]
    [InlineData("see", "saw")]
    [InlineData("see", "seen")]
    [InlineData("seen", "saw")]
    [InlineData("see", "seeing")]
    [InlineData("put", "putting")]
    [InlineData("cut", "cutting")]
    [InlineData("dig", "dug")]
    [InlineData("dig", "digging")]
    [InlineData("spin", "spun")]
    [InlineData("bleed", "bled")]
    [InlineData("light", "lit")]
    [InlineData("understand", "understood")]
    [InlineData("overtake", "overtook")]
    [InlineData("overtook", "overtaken")]
    [InlineData("withdraw", "withdrew")]
    [InlineData("undo", "undone")]
    [InlineData("rewrite", "rewrote")]
    [InlineData("become", "became")]
    [InlineData("foresee", "foresaw")]
    // irregular plurals
    [InlineData("man", "men")]
    [InlineData("woman", "women")]
    [InlineData("child", "children")]
    [InlineData("foot", "feet")]
    [InlineData("tooth", "teeth")]
    [InlineData("mouse", "mice")]
    [InlineData("goose", "geese")]
    [InlineData("person", "people")]
    [InlineData("ox", "oxen")]
    [InlineData("analysis", "analyses")]
    [InlineData("crisis", "crises")]
    [InlineData("criterion", "criteria")]
    [InlineData("datum", "data")]
    [InlineData("medium", "media")]
    [InlineData("index", "indices")]
    [InlineData("matrix", "matrices")]
    [InlineData("cactus", "cacti")]
    [InlineData("bacterium", "bacteria")]
    [InlineData("formula", "formulae")]
    [InlineData("sheep", "sheep")]
    // irregular comparatives
    [InlineData("good", "better")]
    [InlineData("good", "best")]
    [InlineData("bad", "worse")]
    [InlineData("bad", "worst")]
    [InlineData("many", "more")]
    [InlineData("far", "further")]
    [InlineData("old", "elder")]
    [InlineData("little", "least")]
    // possessives and case
    [InlineData("dog", "dog's")]
    [InlineData("dogs", "dogs'")]
    [InlineData("children", "children's")]
    [InlineData("Run", "RUNNING")]
    public void AreWordForms_RelatesInflectionsOfOneWord(string a, string b)
    {
        FindWordMatching.AreWordForms(a, b).Should().BeTrue();
        FindWordMatching.AreWordForms(b, a).Should().BeTrue();
    }

    [Theory]
    // the look-alikes the old suffix-stripping rule table over-matched
    [InlineData("number", "numb")]
    [InlineData("news", "new")]
    [InlineData("corner", "corn")]
    [InlineData("paper", "pap")]
    [InlineData("water", "wat")]
    [InlineData("hammer", "ham")]
    [InlineData("hunger", "hung")]
    [InlineData("evening", "even")]
    [InlineData("morning", "morn")]
    [InlineData("seed", "see")]
    [InlineData("feed", "fee")]
    [InlineData("better", "bet")]
    [InlineData("runner", "run")]
    [InlineData("carrier", "carry")]
    [InlineData("sing", "see")]
    // unrelated and near-miss pairs
    [InlineData("run", "rung")]
    [InlineData("is", "its")]
    [InlineData("bed", "be")]
    [InlineData("make", "mask")]
    [InlineData("cat", "dog")]
    [InlineData("man", "many")]
    [InlineData("go", "good")]
    [InlineData("tear", "tea")]
    [InlineData("night", "light")]
    [InlineData("walk", "wall")]
    [InlineData("stop", "stoop")]
    [InlineData("66", "67")]
    public void AreWordForms_KeepsUnrelatedWordsApart(string a, string b)
    {
        FindWordMatching.AreWordForms(a, b).Should().BeFalse();
        FindWordMatching.AreWordForms(b, a).Should().BeFalse();
    }

    [Fact]
    public void BaseCandidates_AlwaysRegenerateTheWord()
    {
        foreach (var word in new[] { "stopped", "wolves", "happiest", "went", "children", "numbers", "dying" })
        {
            foreach (var lemma in FindWordMatching.BaseCandidates(word))
                FindWordMatching.WordForms(lemma).Should().Contain(word, $"{lemma} must regenerate {word}");
        }

        FindWordMatching.BaseCandidates("number").Should().NotContain("numb");
        FindWordMatching.BaseCandidates("news").Should().NotContain("new");
        FindWordMatching.BaseCandidates("stopped").Should().Contain("stop");
        FindWordMatching.BaseCandidates("went").Should().Contain("go");
    }

    [Fact]
    public void AllWordForms_ReplacesEveryFormAndLeavesSubstringsAlone()
    {
        const string text = "He runs, ran and is running; a runner; rungs";
        Find(text, "run", Forms).Select(m => text.Substring(m.Start, m.Length))
            .Should().Equal("runs", "ran", "running");
    }

    [Fact]
    public void AllWordForms_DoesNotConfuseLookAlikes()
    {
        Find("the number of numb limbs", "numb", Forms).Select(m => m.Start).Should().Equal(14);
        Find("news and new things", "new", Forms).Select(m => m.Start).Should().Equal(9);
    }

    [Fact]
    public void AllWordForms_MultiWordNeedleMatchesConsecutiveForms()
    {
        Find("He ran home", "run home", Forms).Should().Equal((3, 8));
        Find("He ran, home", "run home", Forms).Should().BeEmpty();
    }

    [Fact]
    public void WordSearch_IsIgnoredWhenWildcardsAreOn_AndNeedleWithoutWordsMatchesNothing()
    {
        Find("runs", "r*s", new TextSearchOptions(UseWildcards: true, SoundsLike: true)).Should().Equal((0, 4));
        Find("runs", "...", Forms).Should().BeEmpty();
    }
}
