using System.Collections.Immutable;

namespace Hephaestus.Tests;

/// <summary>The typed layer, exercised with the base class library's time types so that no extra package is needed.</summary>
public sealed class ProjectionTests {
    private static readonly DateTime Origin = new(2026, 9, 19, 8, 0, 0);
    private static readonly Point<DateTime, TimeSpan> Start = Variable.DateTime("start", Origin);
    private static readonly Point<DateTime, TimeSpan> Finish = Variable.DateTime("finish", Origin);
    private static readonly Quantity<TimeSpan> Runtime = Variable.TimeSpan("runtime");
    private static readonly ContinuousVariable StartSeconds = Variable.Continuous("start");
    private static readonly ContinuousVariable FinishSeconds = Variable.Continuous("finish");
    private static readonly ContinuousVariable RuntimeSeconds = Variable.Continuous("runtime");

    private static void AssertSameConstraint(IBooleanExpression<ILinearArithmetic> expected, IBooleanExpression<ILinearArithmetic> actual) =>
        Assert.Equal(NormalForms.Canonical(expected.Normalise()), NormalForms.Canonical(actual.Normalise()));

    [Fact]
    public void TheDifferenceOfTwoPointsIsAQuantityAndAPointShiftedByAQuantityIsAPoint() {
        Quantity<TimeSpan> runTime = Finish - Start;
        Point<DateTime, TimeSpan> earliestFinish = Start + Runtime + TimeSpan.FromMinutes(5);

        AssertSameConstraint(FinishSeconds - StartSeconds >= 300, runTime >= TimeSpan.FromMinutes(5));
        AssertSameConstraint(StartSeconds + RuntimeSeconds + 300 <= FinishSeconds, earliestFinish <= Finish);
        AssertSameConstraint(StartSeconds - RuntimeSeconds - 300 <= FinishSeconds, Start - Runtime - TimeSpan.FromMinutes(5) <= Finish);
        // A plain delta may come first.
        AssertSameConstraint(StartSeconds + 300 <= FinishSeconds, TimeSpan.FromMinutes(5) + Start <= Finish);
    }

    [Fact]
    public void APlainValueShiftedByAQuantityIsAPointAnchoredAtThatValue() {
        var minutes = Variable.TimeSpan("minutes", unit: TimeSpan.FromMinutes(1));

        AssertSameConstraint(StartSeconds >= 600 + RuntimeSeconds, Start >= Origin.AddMinutes(10) + Runtime);
        AssertSameConstraint(StartSeconds >= 600 + RuntimeSeconds, Start >= Runtime + Origin.AddMinutes(10));
        AssertSameConstraint(StartSeconds <= 600 - RuntimeSeconds, Start <= Origin.AddMinutes(10) - Runtime);
        AssertSameConstraint(StartSeconds >= 600 + 60 * Variable.Continuous("minutes") + RuntimeSeconds, Start >= Origin.AddMinutes(10) + minutes + Runtime);
        Assert.Equal(new DateTimeProjection(Origin.AddMinutes(10), TimeSpan.FromMinutes(1)), (Origin.AddMinutes(10) + minutes).Projection);
        Assert.Equal(Runtime.Expression, (Origin + Runtime).Expression);
    }

    [Fact]
    public void BoundsMayBePlainValuesOrExpressions() {
        AssertSameConstraint((StartSeconds <= FinishSeconds) & (FinishSeconds <= StartSeconds + 600), Finish.Between(Start, Start + TimeSpan.FromMinutes(10)));
        AssertSameConstraint((0 <= FinishSeconds) & (FinishSeconds <= StartSeconds), Finish.Between(Origin, Start));
        AssertSameConstraint((StartSeconds <= FinishSeconds) & (FinishSeconds <= 3600), Finish.Between(Start, Origin.AddHours(1)));
        AssertSameConstraint((30 <= RuntimeSeconds) & (RuntimeSeconds <= FinishSeconds - StartSeconds), Runtime.Between(TimeSpan.FromSeconds(30), Finish - Start));
        AssertSameConstraint((FinishSeconds - StartSeconds <= RuntimeSeconds) & (RuntimeSeconds <= 300), Runtime.Between(Finish - Start, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void AQuantityCanBeCountedInAPlainUnitOrMeasuredByAnotherProjectionForUseInAPlainExpression() {
        Assert.Equal((RuntimeSeconds / 60).Normalise(), Runtime.In(TimeSpan.FromMinutes(1)).Normalise());
        Assert.Equal((RuntimeSeconds / 60).Normalise(), Runtime.In(new TimeSpanProjection(TimeSpan.FromMinutes(1))).Normalise());
    }

    [Fact]
    public void PointsCompareWithPlainValues() {
        AssertSameConstraint(StartSeconds >= 600, Start >= Origin.AddMinutes(10));
        AssertSameConstraint(StartSeconds.Between(0, 3600), Start.Between(Origin, Origin.AddHours(1)));
        AssertSameConstraint(StartSeconds.EqualTo(60), Start.EqualTo(Origin.AddMinutes(1)));
        AssertSameConstraint((FinishSeconds - 900).EqualTo(0), (Origin.AddMinutes(15) - Finish).EqualTo(TimeSpan.Zero));
    }

    [Fact]
    public void QuantitiesFormAVectorSpace() {
        AssertSameConstraint(2 * RuntimeSeconds + 30 <= 120, 2 * Runtime + TimeSpan.FromSeconds(30) <= TimeSpan.FromMinutes(2));
        AssertSameConstraint(-RuntimeSeconds / 2 >= -60, -Runtime / 2 >= TimeSpan.FromMinutes(-1));
        AssertSameConstraint(60 - RuntimeSeconds > 0, TimeSpan.FromMinutes(1) - Runtime > TimeSpan.Zero);
    }

    [Fact]
    public void QuantitiesInDifferentUnitsAndPointsWithDifferentOriginsAreReconciled() {
        var minutes = Variable.TimeSpan("minutes", unit: TimeSpan.FromMinutes(1), inWholeUnits: true);
        var later = Variable.DateTime("later", Origin.AddHours(1));

        AssertSameConstraint(RuntimeSeconds + 60 * Variable.Integer("minutes") <= 600, Runtime + minutes <= TimeSpan.FromMinutes(10));
        AssertSameConstraint(Variable.Integer("minutes") + RuntimeSeconds / 60 <= 10, minutes + Runtime <= TimeSpan.FromMinutes(10));
        AssertSameConstraint(StartSeconds <= Variable.Continuous("later") + 3600, Start <= later);
        AssertSameConstraint((Variable.Continuous("later") + 3600 - StartSeconds).EqualTo(60), (later - Start).EqualTo(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void QuantitiesSumUnderTheProjectionOfTheFirst() {
        var minutes = Variable.TimeSpan("minutes", unit: TimeSpan.FromMinutes(1));

        Assert.Equal((RuntimeSeconds + 60 * Variable.Continuous("minutes")).Normalise(), new[] { Runtime, minutes }.Sum().Expression.Normalise());
        Assert.Throws<InvalidOperationException>(() => Array.Empty<Quantity<TimeSpan>>().Sum());
    }

    [Fact]
    public void SolutionsAreReadBackInTheDomainType() {
        var solution = new Solution(
            ImmutableSortedDictionary.Create<IVariable, double>(VariableOrder.Comparer).Add(StartSeconds, 90).Add(FinishSeconds, 600).Add(RuntimeSeconds, 45),
            0);

        Assert.Equal(Origin.AddSeconds(90), solution.Value(Start));
        Assert.Equal(TimeSpan.FromSeconds(45), solution.Value(Runtime));
        Assert.Equal(TimeSpan.FromSeconds(510), solution.Value(Finish - Start));
        Assert.Equal(Origin.AddSeconds(135), solution.Value(Start + Runtime));
    }

    /// <summary>A type of a modeller's own, projected onto the number line, with an algebra of its own and nothing inherited.</summary>
    private sealed record Money(ILinearExpression Expression, IProjection<decimal> Projection) : ILinearlyEncodable<decimal>;

    [Fact]
    public void ATypeOfYourOwnIsComparedReadAndOptimisedLikeTheBuiltInOnes() {
        var cost = new Money(Variable.Continuous("cost"), new RealNumberProjection<decimal>());

        // Comparison, against another of its kind and against a plain value.
        Assert.Equal("cost <= 100", (cost <= 100m).Format());
        Assert.Equal("cost >= budget", (cost >= new Money(Variable.Continuous("budget"), new RealNumberProjection<decimal>())).Format());
        Assert.Equal("cost == 5", cost.EqualTo(5m).Format());
        Assert.Equal("(0 <= cost) & (cost <= 9)", cost.Between(0m, 9m).Format());

        // Reading, and optimising.
        Assert.Equal(12.5m, Solution.Empty.With(cost, 12.5m).Value(cost));
        Assert.Equal(new Optimisation(ObjectiveSense.Minimise, cost.Expression), Objective.Minimise(cost));
    }

    [Fact]
    public void OneValueReadsEveryKindOfExpression() {
        var flag = Variable.Binary("flag");
        var count = Variable.Integer<int>("count");
        var cost = Variable.Continuous<decimal>("cost");
        var runtime = Variable.TimeSpan("runtime");
        var start = Variable.DateTime("start", Origin);
        var solution = Solution.Empty
            .With(flag, true)
            .With(count, 3)
            .With(cost, 12.5m)
            .With(runtime, TimeSpan.FromMinutes(2))
            .With(start, Origin.AddMinutes(5));

        // One name, and the type of the answer follows the thing asked about.
        Assert.Equal(1d, solution.Value(flag.Indicator));
        Assert.True(solution.Value(flag));
        Assert.True(solution.Value(flag & (count >= 1)));
        Assert.Equal(3, solution.Value(count));
        Assert.Equal(12.5m, solution.Value(cost));
        Assert.Equal(TimeSpan.FromMinutes(2), solution.Value(runtime));
        Assert.Equal(Origin.AddMinutes(5), solution.Value(start));
        Assert.Equal("120s", solution.Value(runtime.Select(span => $"{span.TotalSeconds}s")));
        Assert.Equal(2d, solution.Value(runtime.Biselect(span => span.TotalMinutes, (double m) => TimeSpan.FromMinutes(m))));
    }

    [Fact]
    public void AnExpressionCanBeSeenAsOneOfAnotherTypeOrMappedOneWayOnly() {
        var seconds = Variable.TimeSpan("seconds");

        var minutes = seconds.Biselect(span => span.TotalMinutes, (double count) => TimeSpan.FromMinutes(count));
        IReadableExpression<string> written = seconds.Select(span => $"{span.TotalSeconds}s");
        IWritableExpression<int> fromMinutes = seconds.Preselect((int count) => TimeSpan.FromMinutes(count));

        Assert.Equal(2d, Solution.Empty.With(seconds, TimeSpan.FromMinutes(2)).Value(minutes));
        // Still writable, so it may still be constrained.
        Assert.Equal("seconds <= 120", (minutes <= 2d).Format());
        // Select leaves a reading that no constraint can mention ...
        Assert.Equal("90s", Solution.Empty.With(seconds, TimeSpan.FromSeconds(90)).Value(written));
        Assert.IsNotAssignableFrom<IWritableExpression<string>>(written);
        // ... and Preselect one that no solution can be asked for.
        Assert.Equal<double>([120], fromMinutes.Encoder.Encode(2));
        Assert.IsNotAssignableFrom<IReadableExpression<int>>(fromMinutes);
    }

    [Fact]
    public void AProjectionCanBeSeenAsOneOfAnotherTypeOrMappedOneWayOnly() {
        var seconds = new TimeSpanProjection(TimeSpan.FromSeconds(1));

        var minutes = seconds.Biselect(span => span.TotalMinutes, (double count) => TimeSpan.FromMinutes(count));
        IDecoder<string, double> written = seconds.Select(span => $"{span.TotalSeconds}s");
        IEncoder<int, double> fromMinutes = seconds.Preselect((int count) => TimeSpan.FromMinutes(count));

        Assert.Equal(2, minutes.Decode(120));
        Assert.Equal(120, minutes.Encode(2));
        Assert.Equal("90s", written.Decode(90));
        // It reads, and that is all: there is no Encode to put one back into a model.
        Assert.IsNotAssignableFrom<IEncoder<string, double>>(written);
        Assert.Equal(120, fromMinutes.Encode(2));
    }

    [Fact]
    public void DateTimeOffsetsBehaveLikeDateTimes() {
        var origin = new DateTimeOffset(Origin, TimeSpan.FromHours(10));
        var moment = Variable.DateTimeOffset("moment", origin, unit: TimeSpan.FromMinutes(1));

        AssertSameConstraint(Variable.Continuous("moment") >= 30, moment >= origin.AddMinutes(30));
        Assert.Equal(origin.AddMinutes(2), new DateTimeOffsetProjection(origin, TimeSpan.FromMinutes(1)).Decode(2));
    }
}
