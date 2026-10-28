using System.Collections.Immutable;
using static Hephaestus.Piecewise;

namespace Hephaestus.Tests;

public sealed class PiecewiseTests {
    private static readonly ContinuousVariable X = Variable.Continuous("x");
    private static readonly ContinuousVariable Y = Variable.Continuous("y");
    private static readonly ContinuousVariable Z = Variable.Continuous("z");
    private static readonly IntegerVariable N = Variable.Integer("n");
    private static readonly IntegerVariable M = Variable.Integer("m");
    private static readonly BinaryVariable Runs = Variable.Binary("runs");
    private static readonly BinaryVariable Stops = Variable.Binary("stops");
    private static readonly IBooleanExpression<ILinearArithmetic> Box = X.Between(0, 10) & Y.Between(0, 10);

    private static IEnumerable<string> Rows(IOneShotProblem problem) => problem.Encode().Rows.Select(row => row.Format()).Order(StringComparer.Ordinal);

    private static IEnumerable<string> Auxiliaries(IOneShotProblem problem) => problem.Encode().Columns.Where(column => column.IsAuxiliary).Select(column => column.Variable.Name).Order(StringComparer.Ordinal);

    [Fact]
    public void TheFunctionsOnlyBuildData() {
        Assert.Equal(new Maximum(X, Y), Max(X, Y));
        Assert.Equal(new Minimum(X, new Constant(5)), Min(X, 5));
        Assert.Equal(new Maximum(new Constant(5), X), Max(5, X));
        Assert.Equal(new AbsoluteValue(X - Y), Abs(X - Y));
        Assert.Equal(new Maximum(new Maximum(X, Y), new Maximum(Z, N)), Max([X, Y, Z, N]));
        Assert.Equal(new Minimum(X, new Minimum(Y, Z)), Min([X, Y, Z]));
        Assert.Throws<InvalidOperationException>(() => Max(Array.Empty<ILinearExpression>()));
        // So do a conditional and the product with a binary variable.
        Assert.Equal(new Conditional(X >= 5, Y, Z), If(X >= 5, Y, Z));
        Assert.Equal(new Conditional(Runs, X, new Constant(0)), If(Runs, X));
        Assert.Equal(new Conditional(Runs, new Constant(3), new Constant(1)), If(Runs, 3, 1));
        Assert.Equal(new Conditional(Runs, X + 1, new Constant(0)), Runs.Indicator * (X + 1));
        Assert.Equal(new Conditional(Runs, X, new Constant(0)), X * Runs.Indicator);
        Assert.Equal(new Conditional(Runs, new Indicator(Stops), new Constant(0)), Runs.Indicator * Stops.Indicator);
        Assert.Equal(new Product(2, new Indicator(Runs)), 2 * Runs.Indicator);
    }

    [Fact]
    public void TheyAreWrittenAndReadOffASolutionTheWayTheyAreRead() {
        var solution = new Solution(ImmutableSortedDictionary.Create<IVariable, double>(VariableOrder.Comparer).Add(X, 3).Add(Y, -7), 0);

        Assert.Equal("max(x, y + 1) <= 2*abs(x - y)", (Max(X, Y + 1) <= 2 * Abs(X - Y)).Format());
        Assert.Equal("-min(x, 3)", (-Min(X, 3)).Format());
        Assert.Equal<IVariable>([X, Y, Z], [.. (Max(X, Y) + Abs(Z) <= 1).Variables]);
        Assert.Equal(3, solution.Value(Max(X, Y)));
        Assert.Equal(-7, solution.Value(Min(X, Y)));
        Assert.Equal(10, solution.Value(Abs(X - Y)));
        Assert.True(solution.Value(Abs(Y) >= 2 * Max(X, 0)));
    }

    [Fact]
    public void TheyHaveNoAffineFormOfTheirOwn() =>
        Assert.Contains("piecewise", Assert.Throws<ModellingException>(() => (X + Max(X, Y)).Normalise()).Message);

    [Fact]
    public void AMaximumThatIsPushedDownNeedsNoBinaryVariable() {
        var problem = Problem.Minimise(Max(X, Y)).SubjectTo(Box & (Max(X, Y) <= 8));

        Assert.Equal(["_max0"], Auxiliaries(problem));
        Assert.Equal(["-_max0 + x <= 0", "-_max0 + y <= 0"], Rows(problem));
        Assert.Equal(8, problem.Encode().Columns.Single(column => column.IsAuxiliary).UpperBound);
    }

    [Fact]
    public void AMaximumThatIsPushedUpNeedsTheChoiceOfWhichOperandItEquals() {
        var problem = Problem.Maximise(Max(X, Y)).SubjectTo(Box & (X + Y <= 12));

        Assert.Equal(["_aux0", "_max0"], Auxiliaries(problem));
        // Neither conditional row could be relaxed without an upper bound for _max0. It has the largest of its operands' upper bounds: 10.
        Assert.Equal(["-10*_aux0 + _max0 - y <= 0", "10*_aux0 + _max0 - x <= 10", "x + y <= 12"], Rows(problem));
    }

    [Fact]
    public void AMinimumIsAMaximumInDisguise() {
        var problem = Problem.Maximise(Min(X, Y)).SubjectTo(Box);

        // min(x, y) = -max(-x, -y), and maximising it pushes that maximum down.
        Assert.Equal("-_max0", problem.Encode().Objective.Format());
        Assert.Equal(["-_max0 - x <= 0", "-_max0 - y <= 0"], Rows(problem));
    }

    [Fact]
    public void AnAbsoluteValueBoundedFromAboveIsTwoPlainRowsButUsedBothWaysItIsTiedBothWays() {
        Assert.Equal(["-_max0 + x - y <= 0", "-_max0 - x + y <= 0"], Rows(Problem.Satisfy(Box & (Abs(X - Y) <= 3))));
        Assert.Equal(["_aux0", "_max0"], Auxiliaries(Problem.Minimise(Abs(X - 5)).SubjectTo(X.Between(0, 10) & (Abs(X - 5) >= 2))));
    }

    [Fact]
    public void EqualFunctionsShareOneVariableHoweverTheyWereWritten() {
        Assert.Equal(["_max0", "_max1"], Auxiliaries(Problem.Satisfy(Box & (Max(X + Y, 3) <= 8) & (Max(Y + X, 3) + X <= 9) & (Abs(X) <= 4))));
        // A maximum is the same maximum either way round.
        Assert.Equal(["_max0"], Auxiliaries(Problem.Minimise(Max(X, Y) + Max(Y, X)).SubjectTo(Box)));
        // So is an absolute value: |x - y| and |y - x| are max(x - y, y - x) and max(y - x, x - y), which are one maximum.
        Assert.Equal(["_max0"], Auxiliaries(Problem.Minimise(Abs(X - Y) + Abs(Y - X)).SubjectTo(Box)));
        // But a minimum is not a maximum: it is max(-y, -x), a different maximum, and pushing it down as well costs the one binary.
        Assert.Equal(["_aux0", "_max0", "_max1"], Auxiliaries(Problem.Minimise(Max(X, Y) + Min(Y, X)).SubjectTo(Box)));
    }

    [Fact]
    public void LoweredConstraintsKnowWhichOnesWereWritten() {
        var written = Box & (Max(X, Y) <= 8);

        var constraints = Problem.Minimise(Max(X, Y)).SubjectTo(written).Linearise().Constraints;

        // Those as written come first and in order, each beside what it was lowered to.
        Assert.Equal(written.Conjuncts, constraints.Take(written.Conjuncts.Length).Select(constraint => constraint.Written));
        Assert.Equal("_max0 <= 8", constraints[written.Conjuncts.Length - 1].Lowered.Format());
        // What ties the maximum to its operands comes after, and nobody wrote it.
        Assert.NotEmpty(constraints.Skip(written.Conjuncts.Length));
        Assert.All(constraints.Skip(written.Conjuncts.Length), constraint => Assert.Null(constraint.Written));
    }

    [Fact]
    public void FunctionsNestAndTheInnerOneIsTiedAsTheOuterOneNeeds() =>
        // Bounding max(x, min(y, z)) from above pushes the minimum down too, which it can only resist by choosing which operand it equals.
        Assert.Equal(["_aux0", "_max0", "_max1"], Auxiliaries(Problem.Satisfy(Box & Z.Between(0, 10) & (Max(X, Min(Y, Z)) <= 8))));

    [Fact]
    public void TheMaximumOfWholeNumbersIsAWholeNumber() {
        var encoded = Problem.Minimise(Max(N, 2 * M) + Max(N, 0.5 * M)).SubjectTo(N.Between(0, 5) & M.Between(0, 5)).Encode();

        Assert.IsType<IntegerVariable>(encoded.Columns.Single(column => column.Variable.Name == "_max0").Variable);
        Assert.IsType<ContinuousVariable>(encoded.Columns.Single(column => column.Variable.Name == "_max1").Variable);
    }

    [Fact]
    public void NamesStepAroundThoseAlreadyInUse() =>
        Assert.Equal(["_max1"], Auxiliaries(Problem.Minimise(Max(X, Variable.Continuous("_max0"))).SubjectTo(Box & Variable.Continuous("_max0").Between(0, 1))));

    [Fact]
    public void AProblemWithoutThemKeepsItsObjectiveAndItsShape() {
        var problem = Problem.Minimise(X).SubjectTo(Box);
        var linearised = problem.Linearise();

        Assert.Equal(problem.Objective, linearised.Objective);
        Assert.Empty(linearised.Definitions);
        Assert.Equal(problem.Constraint.Conjuncts, linearised.Constraints.Select(constraint => constraint.Written));
        Assert.Equal(["-x <= 0", "x <= 10", "-y <= 0", "y <= 10"], linearised.Constraints.Select(constraint => constraint.Lowered.Format()));
    }

    [Fact]
    public void TypedExpressionsHaveThemToo() {
        var origin = new DateTime(2026, 9, 19, 8, 0, 0);
        var (first, second) = (Variable.DateTime("first", origin), Variable.DateTime("second", origin.AddHours(1)));
        var delay = Variable.TimeSpan("delay", unit: TimeSpan.FromMinutes(1));
        var solution = new Solution(ImmutableSortedDictionary.Create<IVariable, double>(VariableOrder.Comparer).Add(Variable.Continuous("first"), 5400).Add(Variable.Continuous("second"), 600).Add(Variable.Continuous("delay"), -3), 0);

        Assert.Equal(origin.AddMinutes(90), solution.Value(Max(first, second)));
        Assert.Equal(origin.AddMinutes(70), solution.Value(Min(first, second)));
        Assert.Equal(origin.AddMinutes(100), solution.Value(Max(first, origin.AddMinutes(100))));
        Assert.Equal(TimeSpan.FromMinutes(3), solution.Value(Abs(delay)));
        Assert.Equal(TimeSpan.FromMinutes(20), solution.Value(Max(delay, first - second)));
        Assert.Equal(TimeSpan.FromMinutes(-3), solution.Value(Min([delay, first - second, Abs(delay)])));
    }

    [Fact]
    public void AConditionalIsWrittenAndReadLikeTheRest() {
        var solution = new Solution(Solution.Empty.Values.Add(X, 3).Add(Y, -7).Add(Runs, 1).Add(Stops, 0), 0);

        Assert.Equal("if(runs & (x >= 2), y, 2*x) + 1", (If(Runs & (X >= 2), Y, 2 * X) + 1).Format());
        Assert.Equal<IVariable>([Runs, X, Y], [.. If(Runs, X, Y).Variables]);
        Assert.Equal(-7, solution.Value(If(Runs & (X >= 2), Y, 2 * X)));
        Assert.Equal(6, solution.Value(If(Stops | (X >= 4), Y, 2 * X)));
        Assert.Equal(0, solution.Value(Stops.Indicator * Y));
        Assert.Equal(-7, solution.Value(Runs.Indicator * Y));
    }

    [Fact]
    public void TheProductWithABinaryVariableIsTwoConditionalRowsAndNoFurtherBinary() {
        var problem = Problem.Satisfy(Box & (Runs.Indicator * X).EqualTo(Y));

        Assert.Equal(["_if0"], Auxiliaries(problem));
        Assert.Equal(["!runs => _if0 == 0", "_if0 - y == 0", "runs => _if0 - x == 0"], problem.EncodeLogic().Rows.Select(row => row.Format()).Order(StringComparer.Ordinal));
        // Relaxed for a solver without indicator constraints, those are the rows of the textbook, with big-M from the bounds of x.
        // (The fourth, that the product is at least zero when the variable is not set, is already a bound of its column.)
        Assert.Equal(["-_if0 + 10*runs + x <= 10", "_if0 + 10*runs - x <= 10", "_if0 - 10*runs <= 0", "_if0 - y == 0"], Rows(problem));
        Assert.Equal((0, 10), (problem.Encode().Columns.Single(column => column.IsAuxiliary).LowerBound, problem.Encode().Columns.Single(column => column.IsAuxiliary).UpperBound));
    }

    [Fact]
    public void OnlyAnIndicatorWithoutAnAffineFormIsLowered() {
        // The indicator of a binary, of its negation or of a constant is a column or a number already, and costs nothing.
        Assert.Empty(Auxiliaries(Problem.Minimise(Runs.Indicator - (!Stops).Indicator + BooleanConstant.False.Indicator).SubjectTo(Box)));
        Assert.Contains("_if0", Auxiliaries(Problem.Minimise((X >= 2).Indicator).SubjectTo(Box)));
    }

    [Fact]
    public void AConditionalThatIsOnlyPushedOneWayIsOnlyHeldFromTheOther() =>
        Assert.Equal(["!runs => _if0 >= 2", "runs => _if0 - x >= 0"], Problem.Minimise(If(Runs, X, 2)).SubjectTo(Box).EncodeLogic().Rows.Select(row => row.Format().Replace("-_if0 + x <= 0", "_if0 - x >= 0").Replace("-_if0 <= -2", "_if0 >= 2")).Order(StringComparer.Ordinal));

    [Fact]
    public void ACompoundConditionIsEncodedBothWays() {
        var problem = Problem.Maximise(If((X >= 5) & Runs, Y, 1)).SubjectTo(Box);

        Assert.Contains("_if0", Auxiliaries(problem));
        Assert.Equal<IVariable>([Runs, X, Y], [.. problem.Variables]);
    }

    [Fact]
    public void ConditionalsNestWithTheOtherFunctionsAndShareWhenEqual() =>
        Assert.Equal(["_if1", "_max0"], Auxiliaries(Problem.Minimise(If(Runs, Max(X, Y)) + If(Runs, Max(X, Y) + 0)).SubjectTo(Box)));

    [Fact]
    public void TypedConditionals() {
        var origin = new DateTime(2026, 9, 19, 8, 0, 0);
        var (first, second) = (Variable.DateTime("first", origin), Variable.DateTime("second", origin));
        var runtime = Variable.TimeSpan("runtime");
        var solution = new Solution(Solution.Empty.Values.Add(Variable.Continuous("first"), 60).Add(Variable.Continuous("second"), 600).Add(Variable.Continuous("runtime"), 45).Add(Runs, 0), 0);

        Assert.Equal(TimeSpan.Zero, solution.Value(If(Runs, runtime)));
        Assert.Equal(TimeSpan.FromSeconds(30), solution.Value(If(Runs, runtime, TimeSpan.FromSeconds(30))));
        Assert.Equal(TimeSpan.FromSeconds(540), solution.Value(If(!Runs, second - first, runtime)));
        Assert.Equal(origin.AddMinutes(10), solution.Value(If(Runs, first, second)));
        Assert.Equal(origin, solution.Value(If(Runs, first, origin)));
    }
}
