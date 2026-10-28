namespace Hephaestus.Tests;

/// <summary>Vectors of quantities, combined and compared entry by entry.</summary>
public sealed class VectorTests {
    private static readonly Vector<Quantity<double>> U = Vector.Of(Variable.Continuous<double>("u0"), Variable.Continuous<double>("u1"), Variable.Continuous<double>("u2"));
    private static readonly Vector<Quantity<double>> V = Vector.Of(Variable.Continuous<double>("v0"), Variable.Continuous<double>("v1"), Variable.Continuous<double>("v2"));

    private static readonly Solution Solution = Solution.Empty
        .With(U.Elements.Sequence(), [1, 2, 3])
        .With(V.Elements.Sequence(), [3, 3, 3]);

    [Fact]
    public void ZippingTwoVectorsAndAddingEachPairGivesTheVectorOfSums() {
        var sums = U.Zip(V).Select((u, v) => u + v);

        Assert.Equal(["u0 + v0", "u1 + v1", "u2 + v2"], sums.Select(sum => sum.Expression.Format()).Elements);
        Assert.Equal(U + V, sums);
    }

    [Fact]
    public void AComparisonOfVectorsHoldsInEveryEntryAndAPlainValueStandsForItselfInEach() {
        Assert.Equal("(u0 + v0 < 4) & (u1 + v1 < 5) & (u2 + v2 < 6)", (U + V < [4, 5, 6]).Format());
        Assert.Equal("(u0 <= v0) & (u1 <= v1) & (u2 <= v2)", (U <= V).Format());
        Assert.Equal("(4 > u0) & (5 > u1) & (6 > u2)", ([4, 5, 6] > U).Format());
        Assert.Equal("(u0 >= 0) & (u1 >= 0) & (u2 >= 0)", (U >= 0).Format());
        Assert.Equal("(1 < u0) & (1 < u1) & (1 < u2)", (1 < U).Format());
        Assert.Equal(["u0 + 1", "u1 + 1", "u2 + 1"], (U + 1).Select(entry => entry.Expression.Format()).Elements);
    }

    [Fact]
    public void VectorsAreEqualWhenEveryEntryIsAndDifferWhenAnyIs() {
        // U + V is [4, 5, 6]: only the last entry exceeds [4, 5, 5], so neither <= nor > holds, and the negation of <= is that some entry fails it.
        Assert.False(Solution.Value(U + V <= [4, 5, 5]));
        Assert.True(Solution.Value(!(U + V <= [4, 5, 5])));
        Assert.False(Solution.Value(U + V > [4, 5, 5]));
        Assert.True(Solution.Value(U.EqualTo([1, 2, 3])));
        Assert.False(Solution.Value(U.EqualTo(V)));
        Assert.True(Solution.Value(U.NotEqualTo(V)));
        Assert.False(Solution.Value(U.NotEqualTo([1, 2, 3])));
    }

    [Fact]
    public void VectorsOfQuantitiesFormAVectorSpace() {
        Assert.Equal(Vector.Of(-1d, 1d, 3d), (2 * U - V).Select(Solution.Value));
        Assert.Equal(Vector.Of(-0.5d, -1d, -1.5d), (-U / 2).Select(Solution.Value));
        Assert.Equal(Vector.Of(2d, 2d, 2d), ([3, 4, 5] - U).Select(Solution.Value));
        Assert.Equal(6, Solution.Value(U.Sum()));
        Assert.Equal(14, Solution.Value(U.Dot([1, 2, 3])));
    }

    [Fact]
    public void VectorsOfDifferentDimensionsDoNotCombine() {
        var pair = Vector.Of(Variable.Continuous<double>("p0"), Variable.Continuous<double>("p1"));

        Assert.Throws<ArgumentException>(() => U + pair);
        Assert.Throws<ArgumentException>(() => U < [1, 2]);
        Assert.Throws<ArgumentException>(() => U.Dot([1]));
    }

    [Fact]
    public void AVectorOfConstraintsCanBeConjoinedDisjoinedAndRead() {
        var close = U.Zip(V).Select((u, v) => v - u <= 1.0);

        Assert.Equal(Vector.Of(false, true, true), close.Select(Solution.Value));
        Assert.False(Solution.Value(close.AllOf()));
        Assert.True(Solution.Value(close.AnyOf()));
    }

    [Fact]
    public void AVectorOfAnythingReadableIsReadEntryByEntry() {
        var origin = new DateTime(2026, 9, 26, 8, 0, 0);
        var starts = Vector.Of(Variable.DateTime("start0", origin), Variable.DateTime("start1", origin));
        var solution = Solution.With(starts.Elements.Sequence(), [origin, origin.AddMinutes(5)]);

        Assert.Equal(Vector.Of(1d, 2d, 3d), U.Select(solution.Value));
        Assert.Equal(Vector.Of(4d, 5d, 6d), (U + V).Select(solution.Value));
        Assert.Equal(Vector.Of(origin, origin.AddMinutes(5)), starts.Select(solution.Value));
    }

    [Fact]
    public void EntriesUnderDifferentUnitsAreReconciled() {
        var waits = Vector.Of(Variable.TimeSpan("seconds"), Variable.TimeSpan("minutes", unit: TimeSpan.FromMinutes(1)));

        Assert.Equal("(2*seconds <= 60) & (2*minutes <= 3)", (2 * waits <= [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3)]).Format());
        Assert.Equal("(seconds <= 60*minutes) & (minutes <= 0.0166666666667*seconds)", (waits <= Vector.Of(waits.Elements[1], waits.Elements[0])).Format());
    }

    [Fact]
    public void VectorsOfPointsAreCombinedByZipping() {
        var origin = new DateTime(2026, 9, 26, 8, 0, 0);
        var starts = Vector.Of(Variable.DateTime("start0", origin), Variable.DateTime("start1", origin));
        var finishes = Vector.Of(Variable.DateTime("finish0", origin), Variable.DateTime("finish1", origin));

        var runtimes = finishes.Zip(starts).Select((finish, start) => finish - start);

        Assert.Equal("(finish0 - start0 <= 600) & (finish1 - start1 <= 600)", (runtimes <= TimeSpan.FromMinutes(10)).Format());
    }
}
