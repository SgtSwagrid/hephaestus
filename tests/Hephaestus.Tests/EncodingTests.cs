using System.Collections.Immutable;

namespace Hephaestus.Tests;

public sealed class EncodingTests {
    private static readonly ContinuousVariable StartA = Variable.Continuous("startA");
    private static readonly ContinuousVariable StartB = Variable.Continuous("startB");
    private static readonly BinaryVariable UsesA = Variable.Binary("usesA");
    private static readonly BinaryVariable UsesB = Variable.Binary("usesB");
    private static readonly ContinuousVariable X = Variable.Continuous("x");
    private static readonly ContinuousVariable Y = Variable.Continuous("y");
    private static readonly IntegerVariable N = Variable.Integer("n");

    private const double Changeover = 120;
    private const double Horizon = 3600;

    private static IBooleanExpression<ILinearArithmetic> ChangeoverConstraint =>
        StartA.Between(0, Horizon)
        & StartB.Between(0, Horizon)
        & (!(UsesA & UsesB) | (StartA + Changeover <= StartB) | (StartB + Changeover <= StartA));

    [Fact]
    public void TheChangeoverExampleBecomesTheTextbookEitherOrWithASingleAuxiliary() {
        var encoded = Problem.Satisfy(ChangeoverConstraint).Encode();

        Assert.Equal(["_aux0"], encoded.Columns.Where(column => column.IsAuxiliary).Select(column => column.Variable.Name));
        // A hand-written encoding would have to be given its big-M, the changeover plus the horizon; here it falls out of the bounds.
        Assert.Equal(
            [
                // _aux0 = 1 forces A before B ...
                "3720*_aux0 + startA - startB <= 3600",
                // ... and otherwise, if both jobs use the machine, B goes before A.
                "-3720*_aux0 - startA + startB + 3720*usesA + 3720*usesB <= 7320",
            ],
            encoded.Rows.Select(row => row.Format()));
    }

    [Fact]
    public void StatedBoundsBecomeColumnBoundsRatherThanRows() {
        var encoded = Problem.Satisfy(X.Between(2, 10) & (X + Y <= 12) & (Y >= -1)).Encode();

        Assert.Equal(new Column(X, 2, 10, IsAuxiliary: false), encoded.Columns.Single(column => column.Variable.Equals(X)));
        Assert.Equal(new Column(Y, -1, double.PositiveInfinity, IsAuxiliary: false), encoded.Columns.Single(column => column.Variable.Equals(Y)));
        Assert.Equal(["x + y <= 12"], encoded.Rows.Select(row => row.Format()));
    }

    [Fact]
    public void BoundsOnWholeNumbersRoundInwardsForgivingDustButNotGenuineFractionsHoweverLargeTheNumber() {
        Assert.Equal(new Column(N, 1, 3, IsAuxiliary: false), Problem.Satisfy((2 * N <= 9) & (N > 0.5) & (N < 4)).Encode().Columns.Single());
        Assert.Equal(new Column(N, 5, 999_999, IsAuxiliary: false), Problem.Satisfy((N <= 999_999.9995) & (N >= 0.1 + 0.2 - 0.3 + 5)).Encode().Columns.Single());
    }

    [Fact]
    public void BinaryVariablesAssertedAtTheTopLevelAreFixedByTheirBounds() {
        var encoded = Problem.Satisfy(UsesA & !UsesB).Encode();

        Assert.Equal([new Column(UsesA, 1, 1, false), new Column(UsesB, 0, 0, false)], encoded.Columns);
        Assert.Empty(encoded.Rows);
    }

    [Fact]
    public void BigMUsesBoundsThatAreOnlyImplied() {
        var finish = Variable.Continuous("finish");
        var runtime = Variable.Continuous("runtime");
        var late = Variable.Binary("late");
        var constraint =
            StartA.Between(0, 100)
            & runtime.Between(10, 20)
            & finish.EqualTo(StartA + runtime)
            & late.Iff(finish >= 90);

        var encoded = Problem.Satisfy(constraint).Encode();

        // finish is never bounded directly, but lies in [10, 120] because startA and runtime are bounded.
        Assert.Contains("-finish + 80*late <= -10", encoded.Rows.Select(row => row.Format()));
        Assert.Contains("finish - 30.0001*late <= 89.9999", encoded.Rows.Select(row => row.Format()));
    }

    [Fact]
    public void RowsGuardedAlikeShareOneBinaryForTheirConjunctionButGuardsAreToldApartByTheirLiterals() {
        var alike = GuardedBy([[new Literal(UsesA, true), new Literal(UsesB, true)], [new Literal(UsesB, true), new Literal(UsesA, true)]]);
        // Rendered as names and joined, "a" and "b & c" would read as the same three guards as "a", "b" and "c".
        var both = Variable.Binary("usesB & usesC");
        var third = Variable.Binary("usesC");
        var apart = GuardedBy([
            [new Literal(UsesA, true), new Literal(both, true)],
            [new Literal(UsesA, true), new Literal(UsesB, true), new Literal(third, true)],
        ]);

        Assert.Single(alike.WithSingleGuards().Columns, column => column.IsAuxiliary);
        Assert.Equal(2, apart.WithSingleGuards().Columns.Count(column => column.IsAuxiliary));
    }

    /// <summary>A problem whose rows say nothing but are guarded as given, for the single-guard rewrite to chew on.</summary>
    private static IndicatorProblem GuardedBy(ImmutableArray<ImmutableList<Literal>> guards) =>
        new(
            [.. guards.SelectMany(row => row).Select(guard => guard.Variable).Distinct().Select(variable => new Column(variable, 0, 1, IsAuxiliary: false))],
            [.. guards.Select(row => new GuardedRow(row, (X - 1).Normalise(), IsEquality: false))],
            ObjectiveSense.Minimise,
            AffineForm.Zero);

    [Fact]
    public void EachGuardGetsABigMOfItsOwnDerivedWithThatGuardOff() {
        // A row that counts the very binary guarding it: with usesA off the row reaches 95, not the 105 it reaches unconditionally.
        var counting = X.Between(0, 100) & UsesA.Implies(X + 10 * UsesA.Indicator <= 5);
        // With usesA off this row reaches 2 and with usesB off it reaches 5, so the two slacks are weighted apart.
        var twoGuards = N.Between(0, 6) & (!UsesA | !UsesB | (N + 3 * UsesA.Indicator <= 4));
        // Without usesA this row is 0 <= x, which the bounds already say, so no big-M is needed and the row stays unconditional.
        var needless = X.Between(0, 10) & UsesA.Implies(5 * UsesA.Indicator <= X);
        var encoded = Problem.Satisfy(twoGuards).Encode();

        Assert.Equal(["105*usesA + x <= 100"], Problem.Satisfy(counting).Encode().Rows.Select(row => row.Format()));
        Assert.Equal(["n + 5*usesA + 5*usesB <= 11"], encoded.Rows.Select(row => row.Format()));
        Assert.Equal(["5*usesA - x <= 0"], Problem.Satisfy(needless).Encode().Rows.Select(row => row.Format()));
        // The relaxed rows admit exactly the assignments the constraint does.
        Assert.All(
            Assignments(encoded.Columns),
            assignment => Assert.Equal(assignment.Value(twoGuards), Admits(encoded, assignment)));
    }

    /// <summary>Every whole-number assignment within the column bounds, which is all of them when every column is bounded and whole.</summary>
    private static IEnumerable<Solution> Assignments(IEnumerable<Column> columns) =>
        columns.Aggregate<Column, IEnumerable<Solution>>(
            [Solution.Empty],
            (partial, column) => partial.SelectMany(assignment =>
                Enumerable
                    .Range((int)column.LowerBound, (int)(column.UpperBound - column.LowerBound) + 1)
                    .Select(value => assignment.With(column.Variable, value))));

    private static bool Admits(MilpProblem problem, Solution assignment) =>
        problem.Rows.All(row => Within(row, new AffineForm(row.Coefficients, 0).Evaluate(variable => assignment.ValueOf(variable))));

    private static bool Within(LinearRow row, double activity) => row.LowerBound <= activity && activity <= row.UpperBound;

    [Fact]
    public void AnUnderivableBigMIsALoudErrorNamingTheCulpritsUnlessAFallbackIsGiven() {
        var problem = Problem.Satisfy(X.Between(0, 10) & ((X + Y <= 5) | (X >= 8)));

        var exception = Assert.Throws<ModellingException>(() => problem.Encode());

        Assert.Contains("'y'", exception.Message);
        Assert.DoesNotContain("'x'", exception.Message);
        // The fallback is used only where no big-M can be derived.
        Assert.Equal(["1000000*_aux0 + x + y <= 1000005", "-8*_aux0 - x <= -8"], problem.Encode(new EncodingOptions(FallbackBigM: 1e6)).Rows.Select(row => row.Format()));
    }

    [Fact]
    public void ABigMBeyondTheStatedLimitIsALoudErrorNamingTheWidestVariables() {
        var problem = Problem.Satisfy(X.Between(0, 1e9) & (UsesA | (X <= 5)));

        var exception = Assert.Throws<ModellingException>(() => problem.Encode(new EncodingOptions(MaximumBigM: 1e6)));

        Assert.Contains("'x'", exception.Message);
        Assert.Contains(nameof(EncodingOptions.MaximumBigM), exception.Message);
        // Within the limit, or with no limit, which is the default, it passes without comment.
        Assert.Equal(["-999999995*usesA + x <= 5"], problem.Encode(new EncodingOptions(MaximumBigM: 1e10)).Rows.Select(row => row.Format()));
        Assert.Equal(["-999999995*usesA + x <= 5"], problem.Encode().Rows.Select(row => row.Format()));
    }

    [Fact]
    public void EqualSubformulasShareOneAuxiliary() {
        var constraint = X.Between(0, 10) & Y.Between(0, 10) & ((X + Y <= 5) | (X >= 8)) & ((Y + X <= 5) | (Y >= 8));

        var encoded = Problem.Satisfy(constraint).Encode();

        Assert.Single(encoded.Columns, column => column.IsAuxiliary);
    }

    [Fact]
    public void ConditionalRowsThatCanNeverBindAreDroppedAndThoseThatCanNeverHoldForbidTheirGuards() {
        Assert.Empty(Problem.Satisfy(X.Between(0, 10) & (UsesA | (X <= 20))).Encode().Rows);
        Assert.Equal(["-usesA <= -1"], Problem.Satisfy(X.Between(0, 10) & (UsesA | (X >= 20))).Encode().Rows.Select(row => row.Format()));
    }

    [Fact]
    public void AuxiliaryNamesStepAroundNamesAlreadyInUse() {
        var squatter = Variable.Continuous("_aux0");
        var constraint = squatter.Between(0, 1) & X.Between(0, 10) & ((X <= 3) | (X >= 7));

        var encoded = Problem.Satisfy(constraint).Encode();

        Assert.Equal(["_aux1"], encoded.Columns.Where(column => column.IsAuxiliary).Select(column => column.Variable.Name));
    }

    [Fact]
    public void VariablesOfDifferentKindsMayNotShareAName() =>
        Assert.Throws<ModellingException>(() => Problem.Satisfy((Variable.Integer("x") <= 3) & (X >= 1)).Encode());

    [Fact]
    public void EvidentInfeasibilityIsRecognisedWithoutASolverBeforeAndAfterTheGuardsAreRelaxed() {
        Assert.True(Problem.Satisfy((X <= 1) & (X >= 2)).Encode().IsTriviallyInfeasible);
        Assert.True(Problem.Satisfy(BooleanConstant.False).Encode().IsTriviallyInfeasible);
        Assert.True(Problem.Satisfy((X <= 1) & !(X <= 1 | UsesA | !UsesA)).Encode().IsTriviallyInfeasible);
        Assert.False(Problem.Satisfy((X <= 2) & (X >= 1)).Encode().IsTriviallyInfeasible);
        Assert.True(Problem.Satisfy((X <= 1) & (X >= 2)).EncodeLogic().IsTriviallyInfeasible);
        Assert.True(Problem.Satisfy(BooleanConstant.False).EncodeLogic().IsTriviallyInfeasible);
        Assert.False(Problem.Satisfy((X <= 2) & (X >= 1)).EncodeLogic().IsTriviallyInfeasible);
        // A row that fails only under its guards is not evidently anything; the guards may simply not hold.
        Assert.False(Problem.Satisfy(X.Between(0, 10) & (UsesA | (X >= 20))).EncodeLogic().IsTriviallyInfeasible);
    }

    [Fact]
    public void BothFormsOfLoweredProblemAnswerToTheOneType() {
        var problem = Problem.Minimise(X).SubjectTo(X.Between(2, 8));

        Assert.All<ILoweredProblem>(
            [problem.Encode(), problem.EncodeLogic()],
            lowered => {
                Assert.Equal(ObjectiveSense.Minimise, lowered.Sense);
                Assert.Equal(X, Assert.Single(lowered.Columns).Variable);
                Assert.Equal(1, lowered.Objective.Coefficients[X]);
                Assert.False(lowered.IsTriviallyInfeasible);
            });
    }

    [Fact]
    public void TheObjectiveIsCarriedAcrossInNormalFormAndItsVariablesGetColumns() {
        var encoded = Problem.Maximise(2 * (X + Y) - X + 7).SubjectTo(X.Between(0, 1) & Y.Between(0, 1)).Encode();

        Assert.Equal(ObjectiveSense.Maximise, encoded.Sense);
        Assert.Equal("x + 2*y + 7", encoded.Objective.Format());
        // Even those that appear nowhere else.
        Assert.Equal([X, Y], Problem.Minimise(X + Y).SubjectTo(X >= 0).Encode().Columns.Select(column => column.Variable));
    }

    [Fact]
    public void AHundredThousandConstraintsEncodeWithoutDrama() {
        var variables = Enumerable.Range(0, 1000).Select(index => Variable.Continuous($"v{index:D4}")).ToList();
        var constraint = Enumerable.Range(0, 100_000).AllOf(index => variables[index % 1000] + variables[(index + 1) % 1000] <= index);

        var encoded = Problem.Satisfy(constraint).Encode();

        Assert.Equal(100_000, encoded.Rows.Length);
    }

    [Fact]
    public void TheEncodedProgrammeCanBePrinted() {
        var encoded = Problem.Minimise(X).SubjectTo(X.Between(0, 10) & ((X <= 3) | (X >= 7))).Encode();

        Assert.Equal(
            string.Join(Environment.NewLine, [
                "minimise x",
                "subject to",
                "  7*_aux0 + x <= 10",
                "  -7*_aux0 - x <= -7",
                "where",
                "  0 <= x <= 10, continuous",
                "  0 <= _aux0 <= 1, binary",
            ]),
            encoded.Format());
    }
}
