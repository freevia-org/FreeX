using System.Reflection;

namespace FreeW.Core.Model.Tests;

public sealed class IntegerIdAllocatorTests
{
    [Fact]
    public void EmptyAllocators_StartCommentsAtZeroAndNotesAtOne()
    {
        var commentIds = new HashSet<int>();
        var noteIds = new HashSet<int>();
        var comments = new AllocatorHarness(commentIds, firstFreshId: 0);
        var notes = new AllocatorHarness(noteIds, firstFreshId: 1);

        comments.AllocateNext().Should().Be(0);
        comments.AllocateNext().Should().Be(1);
        notes.AllocateNext().Should().Be(1);
        notes.AllocateNext().Should().Be(2);

        commentIds.Should().BeEquivalentTo([0, 1]);
        noteIds.Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public void PreferredIdsArePreservedAndCollisionsAdvanceMonotonicallyPastTheGreatestReservation()
    {
        var usedIds = new HashSet<int> { 0, 2, 7 };
        var allocator = new AllocatorHarness(usedIds, firstFreshId: 1);

        allocator.ReservePreferredOrNext(3).Should().Be(3);
        allocator.ReservePreferredOrNext(20).Should().Be(20);
        allocator.ReservePreferredOrNext(2).Should().Be(21);
        allocator.ReservePreferredOrNext(3).Should().Be(22);
        allocator.ReservePreferredOrNext(0).Should().Be(23);

        usedIds.Should().BeEquivalentTo([0, 2, 3, 7, 20, 21, 22, 23]);
    }

    [Fact]
    public void FreePreferredIdBelowTheFirstFreshIdIsStillPreserved()
    {
        var usedIds = new HashSet<int>();
        var allocator = new AllocatorHarness(usedIds, firstFreshId: 1);

        allocator.ReservePreferredOrNext(0).Should().Be(0);
        allocator.AllocateNext().Should().Be(1);

        usedIds.Should().BeEquivalentTo([0, 1]);
    }

    [Fact]
    public void CallerAdditionsToTheSharedUsedSetRemainVisible()
    {
        var usedIds = new HashSet<int> { 0 };
        var allocator = new AllocatorHarness(usedIds, firstFreshId: 0);
        usedIds.Add(1);

        allocator.AllocateNext().Should().Be(2);

        usedIds.Should().BeEquivalentTo([0, 1, 2]);
    }

    [Fact]
    public void DenseTenThousandIdSet_AllocatesTenThousandMonotonicCollisions()
    {
        const int count = 10_000;
        var usedIds = Enumerable.Range(0, count).ToHashSet();
        var allocator = new AllocatorHarness(usedIds, firstFreshId: 0);
        var allocated = new int[count];

        for (var index = 0; index < count; index++)
            allocated[index] = allocator.ReservePreferredOrNext(index);

        allocated.Should().Equal(Enumerable.Range(count, count));
        usedIds.Should().HaveCount(count * 2);
    }

    [Fact]
    public void CompareCombineAndMergeDelegateCollisionAllocationToTheSharedOwner()
    {
        var compare = ReadSource("DocumentCompare.cs");
        var combine = ReadSource("DocumentCombine.cs");
        var merge = ReadSource("DocumentMerge.cs");

        foreach (var source in new[] { compare, combine, merge })
        {
            source.Should().Contain("new IntegerIdAllocator(");
            source.Should().NotContain("usedIds.Max()")
                .And.NotContain("NextUnusedCommentId(")
                .And.NotContain("NextUnusedNoteId(")
                .And.NotContain("AllocateNoteId(");
        }

        merge.Should().Contain("sourceId >= firstId && allocator.TryReservePreferred(sourceId)",
            "merge must keep rejecting preferred note IDs below its first fresh ID");
    }

    private static string ReadSource(string fileName) =>
        TestWorkspaceFileLocator.ReadAllText("freew", "FreeW.Core.Model", fileName);

    private sealed class AllocatorHarness
    {
        private readonly object _allocator;
        private readonly MethodInfo _allocateNext;
        private readonly MethodInfo _reservePreferredOrNext;

        public AllocatorHarness(HashSet<int> usedIds, int firstFreshId)
        {
            var type = typeof(TextDocument).Assembly.GetType("FreeW.Core.Model.IntegerIdAllocator");
            type.Should().NotBeNull("the model assembly should own the shared integer-ID allocator");

            _allocator = Activator.CreateInstance(
                type!,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: [usedIds, firstFreshId],
                culture: null)!;
            _allocateNext = type!.GetMethod("AllocateNext")!;
            _reservePreferredOrNext = type.GetMethod("ReservePreferredOrNext")!;
        }

        public int AllocateNext() => (int)_allocateNext.Invoke(_allocator, null)!;

        public int ReservePreferredOrNext(int preferredId) =>
            (int)_reservePreferredOrNext.Invoke(_allocator, [preferredId])!;
    }
}
