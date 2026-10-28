using System.Collections.Immutable;

namespace Hephaestus.Tests;

/// <summary>Typed expressions side by side: zipped, sequenced, compared entry by entry, and read and written as one.</summary>
public sealed class ZipTests {
    private static readonly DateTime Origin = new(2026, 9, 26, 8, 0, 0);
    private static readonly Point<DateTime, TimeSpan> Start = Variable.DateTime("start", Origin);
    private static readonly Quantity<TimeSpan> Runtime = Variable.TimeSpan("runtime");
    private static readonly Quantity<TimeSpan> Minutes = Variable.TimeSpan("minutes", unit: TimeSpan.FromMinutes(1));
    private static readonly Quantity<double> X = Variable.Continuous<double>("x");
    private static readonly Quantity<double> Y = Variable.Continuous<double>("y");
    private static readonly BinaryVariable Up = Variable.Binary("up");
    private static readonly BinaryVariable Down = Variable.Binary("down");
    private static readonly Switch Lift = new(Up.Indicator, new DirectionProjection());

    /// <summary>The <see cref="DirectionProjection"/> the other way round: its indicator is one when the lift goes down.</summary>
    private sealed record FlippedProjection : IProjection<Direction> {
        public double Encode(Direction value) => value == Direction.Down ? 1 : 0;

        public Direction Decode(double representation) => representation > 0.5 ? Direction.Down : Direction.Up;
    }

    private sealed record Location(double X, double Y);

    private static readonly ImmutableArray<Solution> Assignments = [
        .. from runtime in new[] { 0, 60, 300, 301 }
           from up in new[] { false, true }
           from down in new[] { false, true }
           select Solution.Empty
               .With(Runtime, TimeSpan.FromSeconds(runtime))
               .With(Start, Origin.AddSeconds(runtime))
               .With(Up, up)
               .With(Down, down),
    ];

    /// <summary>Whether two constraints hold under exactly the same assignments of the variables they mention.</summary>
    private static void AssertEquivalent(IBooleanExpression<ILinearArithmetic> expected, IBooleanExpression<ILinearArithmetic> actual) =>
        Assert.Equal(Assignments.Select(solution => solution.Value(expected)), Assignments.Select(solution => solution.Value(actual)));

    private static void AssertSameConstraint(IBooleanExpression<ILinearArithmetic> expected, IBooleanExpression<ILinearArithmetic> actual) =>
        Assert.Equal(NormalForms.Canonical(expected.Normalise()), NormalForms.Canonical(actual.Normalise()));

    [Fact]
    public void AZipOfANumberAndATruthHasTheComponentsOfBothInOrderAndIsReadAsAPair() {
        var solution = Solution.Empty.With(Start, Origin.AddMinutes(5)).With(Up, true);

        Assert.Equal([Start.Expression, Up.Indicator], Start.Zip(Lift).Components);
        Assert.Equal((Origin.AddMinutes(5), Direction.Up), solution.Value(Start.Zip(Lift)));
    }

    [Fact]
    public void ZipsAndSequencesOfTheSameThingsAreEqual() {
        Assert.Equal(Start.Zip(Lift), Start.Zip(Lift));
        Assert.Equal(new[] { Runtime, Minutes }.Sequence(), new[] { Runtime, Minutes }.Sequence());
        Assert.NotEqual(Runtime.Zip(Minutes), Minutes.Zip(Runtime));
    }

    [Fact]
    public void ATwoStateTypeIsReadPinnedAndGivenAValueLikeAnyOther() {
        Assert.Equal(Direction.Up, Solution.Empty.With(Lift, Direction.Up).Value(Lift));
        Assert.Equal(Direction.Down, Solution.Empty.With(Lift, Direction.Down).Value(Lift));
        // Wired the other way round, giving it a direction gives the binary the opposite value.
        Assert.Equal(Direction.Down, Solution.Empty.With(new Switch((!Up).Indicator, new DirectionProjection()), Direction.Up).Value(Lift));
        Assert.Equal("[up] == 1", Lift.EqualTo(Direction.Up).Format());
        AssertEquivalent(!Up, Lift.EqualTo(Direction.Down));
    }

    [Fact]
    public void ZippedValuesAreEqualWhenEveryEntryIsAndDifferWhenAnyEntryDoes() {
        AssertEquivalent((Runtime <= TimeSpan.FromMinutes(1)) & (Runtime >= TimeSpan.FromMinutes(1)) & Up, Runtime.Zip(Lift).EqualTo((TimeSpan.FromMinutes(1), Direction.Up)));
        AssertEquivalent((Runtime < TimeSpan.FromMinutes(1)) | (Runtime > TimeSpan.FromMinutes(1)) | !Up, Runtime.Zip(Lift).NotEqualTo((TimeSpan.FromMinutes(1), Direction.Up)));
    }

    [Fact]
    public void TruthsAreOrderedByImplicationFalseBeforeTrue() {
        var other = new Switch(Down.Indicator, new DirectionProjection());

        AssertEquivalent(Up.Implies(Down), Lift <= other);
        AssertEquivalent(Down.Implies(Up), Lift >= other);
        AssertEquivalent(!Up & Down, Lift < other);
        AssertEquivalent(Up & !Down, Lift > other);
        AssertEquivalent(!Up, Lift <= Direction.Down);
        AssertEquivalent(BooleanConstant.True, Direction.Down <= Lift);
    }

    [Fact]
    public void PairsAreOrderedInEveryEntry() {
        AssertEquivalent((Runtime <= TimeSpan.FromMinutes(5)) & !Up, Runtime.Zip(Lift) <= (TimeSpan.FromMinutes(5), Direction.Down));
        AssertEquivalent((TimeSpan.FromMinutes(1) < Runtime) & Up, (TimeSpan.FromMinutes(1), Direction.Down) < Runtime.Zip(Lift));
        AssertEquivalent(
            (TimeSpan.Zero <= Runtime) & (Runtime <= TimeSpan.FromMinutes(5)) & Up,
            Runtime.Zip(Lift).Between((TimeSpan.Zero, Direction.Up), (TimeSpan.FromMinutes(5), Direction.Up)));
    }

    [Fact]
    public void ZippedValuesUnderDifferentProjectionsAreReconciled() {
        var later = Variable.DateTime("later", Origin.AddHours(1));
        var runtime = Variable.Continuous("runtime");
        var minutes = Variable.Continuous("minutes");

        AssertSameConstraint(
            (runtime <= 60 * minutes) & (Variable.Continuous("start") <= Variable.Continuous("later") + 3600),
            Runtime.Zip(Start) <= Minutes.Zip(later));
        // A truth under an opposite projection is negated.
        AssertEquivalent(Up.Iff(!Down), Lift.EqualTo(new Switch(Down.Indicator, new FlippedProjection())));
    }

    [Fact]
    public void EntriesInAnotherOrderAreReconciledEntryByEntryWhateverTheirKinds() {
        var otherX = Variable.Continuous<double>("otherX");
        var otherY = Variable.Continuous<double>("otherY");
        var swapped = otherY.Zip(otherX).Biselect(pair => (pair.Item2, pair.Item1), pair => (pair.Item2, pair.Item1));
        var reordered = new Switch(Down.Indicator, new DirectionProjection()).Zip(Minutes).Biselect(pair => (pair.Item2, pair.Item1), pair => (pair.Item2, pair.Item1));

        AssertSameConstraint(X.Expression.EqualTo(otherX.Expression) & Y.Expression.EqualTo(otherY.Expression), X.Zip(Y).EqualTo(swapped));
        AssertSameConstraint((Runtime.Expression <= 60 * Minutes.Expression) & (Up.Indicator <= Down.Indicator), Runtime.Zip(Lift) <= reordered);
    }

    [Fact]
    public void ATruthIsANumberThroughItsIndicator() {
        var liftAsNumber = Lift.Biselect(direction => direction == Direction.Up ? 1d : 0d, number => number > 0.5 ? Direction.Up : Direction.Down);

        Assert.Equal("x <= [up]", (X <= liftAsNumber).Format());
        AssertEquivalent(!Up | (Runtime >= TimeSpan.FromSeconds(1)), liftAsNumber <= Runtime.In(TimeSpan.FromSeconds(1)).AsEncodable());
    }

    [Fact]
    public void AZipIsGivenAValueOneVariableAtATime() {
        var solution = Solution.Empty.With(Start.Zip(Lift), (Origin.AddMinutes(3), Direction.Up));

        Assert.Equal(180, solution.Value(Start.Expression));
        Assert.True(solution.Value(Up));
        Assert.Throws<ArgumentException>(() => Solution.Empty.With((Start + Runtime).Zip(Lift), (Origin, Direction.Up)));
        Assert.Throws<ArgumentException>(() => Solution.Empty.With(Runtime.Zip((Up & Down).AsEncodable()), (TimeSpan.Zero, true)));
    }

    [Fact]
    public void ZipIsAssociativeUpToTheShapeOfTheTupleAndItsProjectionReadsBackWhatItWrites() {
        var solution = Solution.Empty.With(Runtime, TimeSpan.FromMinutes(2)).With(Start, Origin.AddMinutes(5)).With(Up, true);
        var right = Runtime.Zip(Start.Zip(Lift));
        var left = Runtime.Zip(Start).Zip(Lift);
        var value = ((TimeSpan.FromMinutes(2), Origin.AddMinutes(5)), Direction.Up);

        Assert.Equal(left.Components, right.Components);
        Assert.Equal((TimeSpan.FromMinutes(2), (Origin.AddMinutes(5), Direction.Up)), solution.Value(right));
        Assert.Equal(value, solution.Value(left));
        Assert.Equal<double>([120, 300, 1], left.Projection.Encode(value));
        Assert.Equal(value, left.Projection.Decode(left.Projection.Encode(value)));
    }

    [Fact]
    public void APairIsReadAsSomethingMadeOfItsHalvesOrReadAndWrittenAsARecordOfYourOwn() {
        var finish = Start.Zip(Runtime).Select((from, span) => from + span);
        var location = X.Zip(Y).Biselect((across, down) => new Location(across, down), place => (place.X, place.Y));

        Assert.Equal(Origin.AddMinutes(7), Solution.Empty.With(Start, Origin.AddMinutes(5)).With(Runtime, TimeSpan.FromMinutes(2)).Value(finish));
        Assert.IsNotAssignableFrom<IWritableExpression<DateTime>>(finish);
        AssertSameConstraint(X.Expression.EqualTo(1) & Y.Expression.EqualTo(2), location.EqualTo(new Location(1, 2)));
        Assert.Equal(new Location(3, 4), Solution.Empty.With(location, new Location(3, 4)).Value(location));
    }

    [Fact]
    public void ASequenceIsReadAndWrittenAsAnArray() {
        var runtimes = new[] { Runtime, Minutes }.Sequence();
        var solution = Solution.Empty.With(runtimes, [TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)]);

        Assert.Equal<TimeSpan>([TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)], solution.Value(runtimes));
        Assert.Equal(5, solution.Value(Minutes.Expression));
        AssertSameConstraint(Runtime.Expression.EqualTo(60) & Minutes.Expression.EqualTo(3), runtimes.EqualTo([TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3)]));
    }

    [Fact]
    public void SequencesAreReconciledEntryByEntry() {
        var otherMinutes = Variable.TimeSpan("otherMinutes", unit: TimeSpan.FromMinutes(1));
        var otherSeconds = Variable.TimeSpan("otherSeconds");

        AssertSameConstraint(
            (Runtime.Expression <= 60 * otherMinutes.Expression) & (Minutes.Expression <= otherSeconds.Expression / 60),
            new[] { Runtime, Minutes }.Sequence() <= new[] { otherMinutes, otherSeconds }.Sequence());
    }

    [Fact]
    public void ASequenceMustBeGivenOneValueForEachEntryAndAnEmptyOneHasNothingToCompare() {
        var runtimes = new[] { Runtime, Minutes }.Sequence();
        var nothing = Array.Empty<Quantity<TimeSpan>>().Sequence();

        Assert.Throws<ArgumentException>(() => runtimes.EqualTo([TimeSpan.Zero]));
        Assert.Throws<ArgumentException>(() => runtimes <= new[] { Runtime }.Sequence());
        Assert.Empty(nothing.Components);
        Assert.Empty(Solution.Empty.Value(nothing));
        Assert.Equal(BooleanConstant.True, nothing.EqualTo([]));
    }

    [Fact]
    public void PlainExpressionsCanBeZippedWithTypedOnes() {
        var late = Runtime > TimeSpan.FromMinutes(5);
        var solution = Solution.Empty.With(Runtime, TimeSpan.FromMinutes(6)).With(Up, true);

        Assert.Equal((true, TimeSpan.FromMinutes(6)), solution.Value(late.AsEncodable().Zip(Runtime)));
        Assert.Equal((true, 360d), solution.Value(Up.AsEncodable().Zip(Runtime.Expression.AsEncodable())));
        Assert.IsType<Indicator>(Up.AsEncodable().Components.Single());
    }
}
