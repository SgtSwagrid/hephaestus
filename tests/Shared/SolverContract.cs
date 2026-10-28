
using static Hephaestus.Piecewise;

namespace Hephaestus.Contracts;

/// <summary>
/// What every solver must do, whatever is underneath. The same problems go to MILP backends (by way
/// of the big-M encoding) and to Z3 (which takes the logic as it stands); agreeing answers are the
/// evidence that the solver really is swappable.
/// </summary>
public abstract class SolverContract {
    protected abstract ISolver Solver { get; }

    private static readonly ContinuousVariable StartA = Variable.Continuous("startA");
    private static readonly ContinuousVariable StartB = Variable.Continuous("startB");
    private static readonly BinaryVariable UsesA = Variable.Binary("usesA");
    private static readonly BinaryVariable UsesB = Variable.Binary("usesB");
    private static readonly ContinuousVariable X = Variable.Continuous("x");
    private static readonly ContinuousVariable Y = Variable.Continuous("y");
    private static readonly IntegerVariable N = Variable.Integer("n");
    private static readonly BinaryVariable Flag = Variable.Binary("flag");

    private const double Changeover = 120;

    /// <summary>Decimal places to which continuous values are compared: solvers work to tolerances of about a millionth.</summary>
    private const int Precision = 4;

    private static readonly IBooleanExpression<ILinearArithmetic> Separated = (StartA + Changeover <= StartB) | (StartB + Changeover <= StartA);
    private static readonly IBooleanExpression<ILinearArithmetic> ConflictFree = !(UsesA & UsesB) | Separated;
    private static readonly IBooleanExpression<ILinearArithmetic> Horizon = StartA.Between(0, 3600) & StartB.Between(0, 3600);

    private static readonly TimeSpan Moment = TimeSpan.FromMilliseconds(1);

    private Solution Optimum(IOneShotProblem problem) => Assert.IsType<Optimal>(Solver.Solve(problem)).Solution;

    [Fact]
    public void OnlyJobsSharingAMachineAreSeparatedByTheChangeover() {
        var shared = Optimum(Problem.Minimise(StartA + StartB).SubjectTo(Horizon & ConflictFree & UsesA & UsesB));
        var apart = Optimum(Problem.Minimise(StartA + StartB).SubjectTo(Horizon & ConflictFree & UsesA & !UsesB));
        // Starting late costs a little; not running on the shared machine at all costs more than waiting.
        var chosen = Optimum(Problem.Minimise(StartA + StartB + 1000 * (2 - UsesA.Indicator - UsesB.Indicator)).SubjectTo(Horizon & ConflictFree));

        Assert.Equal(Changeover, shared.ObjectiveValue, precision: Precision);
        Assert.Equal(Changeover, Math.Abs(shared.Value(StartA - StartB)), precision: Precision);
        Assert.True(shared.Value(Separated));
        Assert.Equal(0, apart.ObjectiveValue, precision: Precision);
        Assert.False(apart.Value(Separated));
        Assert.True(apart.Value(ConflictFree));
        Assert.Equal(Changeover, chosen.ObjectiveValue, precision: Precision);
        Assert.True(chosen.Value(UsesA & UsesB & Separated));
    }

    [Fact]
    public void AnEquivalenceBindsInBothDirectionsAndAnImplicationOnlyWhenItsAntecedentHolds() {
        var domain = X.Between(0, 10);

        Assert.True(Optimum(Problem.Minimise(Flag.Indicator).SubjectTo(domain & Flag.Iff(X >= 5) & X.EqualTo(7))).Value(Flag));
        Assert.False(Optimum(Problem.Maximise(Flag.Indicator).SubjectTo(domain & Flag.Iff(X >= 5) & X.EqualTo(3))).Value(Flag));
        Assert.Equal(10, Optimum(Problem.Minimise(X).SubjectTo(X.Between(0, 100) & Flag & Flag.Implies(X >= 10))).Value(X), precision: Precision);
        Assert.Equal(0, Optimum(Problem.Minimise(X).SubjectTo(X.Between(0, 100) & Flag.Implies(X >= 10))).Value(X), precision: Precision);
    }

    [Fact]
    public void DisequalityAndStrictnessAreExactOverWholeNumbersAndRespectedOverReals() {
        var domain = N.Between(3, 6);
        var real = Solver.Solve(Problem.Minimise(X).SubjectTo(X.Between(0, 10) & (X > 5))).SolutionOrNull;

        Assert.Equal(4, Optimum(Problem.Minimise(N).SubjectTo(domain & N.NotEqualTo(3))).Value(N));
        Assert.Equal(5, Optimum(Problem.Maximise(N).SubjectTo(domain & (N < 6))).Value(N));
        Assert.Equal(5, Optimum(Problem.Minimise(N).SubjectTo(domain & !(N <= 4))).Value(N));
        Assert.NotNull(real);
        Assert.True(real.Value(X) > 5);
    }

    [Fact]
    public void ASatisfactionProblemYieldsSomeSolutionThatSatisfiesIt() {
        var constraint = X.Between(0, 10) & Y.Between(0, 10) & ((X + Y).EqualTo(12) | (X - Y >= 8)) & (X <= 7) & (Y >= 3);

        var solution = Solver.Solve(Problem.Satisfy(constraint)).SolutionOrNull;

        Assert.NotNull(solution);
        Assert.True(solution.Value(constraint));
    }

    [Fact]
    public void InfeasibilityAndUnboundednessAreReported() {
        Assert.IsType<Infeasible>(Solver.Solve(Problem.Satisfy(X.Between(0, 1) & Y.Between(0, 1) & (X + Y >= 3))));
        Assert.IsType<Infeasible>(Solver.Solve(Problem.Satisfy(X.Between(0, 10) & ((X <= 2) | (X >= 8)) & X.Between(3, 7))));
        Assert.IsType<Infeasible>(Solver.Solve(Problem.Satisfy((X <= 1) & (X >= 2))));
        Assert.IsType<Unbounded>(Solver.Solve(Problem.Maximise(X + Y).SubjectTo((X >= 0) & (Y >= 0) & (X - Y <= 1))));
    }

    [Fact]
    public void TypedExpressionsAreSolvedAndReadBackInTheirOwnTypes() {
        var shiftStart = new DateTime(2026, 9, 19, 8, 0, 0);
        var start = Variable.DateTime("start", origin: shiftStart);
        var finish = Variable.DateTime("finish", origin: shiftStart);
        var runtime = Variable.TimeSpan("runtime", unit: TimeSpan.FromSeconds(30), inWholeUnits: true);
        var constraint =
            start.Between(shiftStart, shiftStart.AddHours(1))
            & finish.Between(shiftStart, shiftStart.AddHours(2))
            & runtime.Between(TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(5))
            & (start >= shiftStart.AddMinutes(10) + runtime)
            & (finish - start >= TimeSpan.FromMinutes(25));

        var solution = Optimum(Problem.Minimise(finish).SubjectTo(constraint));

        Assert.Equal(TimeSpan.FromSeconds(60), solution.Value(runtime));
        Assert.Equal(shiftStart.AddMinutes(11), solution.Value(start), Moment);
        Assert.Equal(shiftStart.AddMinutes(36), solution.Value(finish), Moment);
        Assert.Equal(shiftStart + TimeSpan.FromMinutes(25), shiftStart + solution.Value(finish - start), Moment);

        // So are vectors, entry by entry, and a number zipped with a truth, as one.
        var u = Vector.Of(Variable.Continuous<double>("u0"), Variable.Continuous<double>("u1"), Variable.Continuous<double>("u2"));
        var v = Vector.Of(Variable.Continuous<double>("v0"), Variable.Continuous<double>("v1"), Variable.Continuous<double>("v2"));
        var sums = Optimum(Problem.Maximise((u + v).Sum()).SubjectTo((u + v <= [4, 5, 6]) & (u >= 0) & (v >= 0) & (u - v <= 1) & (v - u <= 1)));
        var duration = Variable.TimeSpan("duration");
        var job = duration.Zip(Flag.AsEncodable());
        var capped = Optimum(Problem.Maximise(duration).SubjectTo((job <= (TimeSpan.FromMinutes(5), false)) & (duration >= TimeSpan.Zero)));
        var pinned = Optimum(Problem.Minimise(duration).SubjectTo(job.EqualTo((TimeSpan.FromMinutes(2), true))));

        Assert.Equal(15, sums.ObjectiveValue, precision: Precision);
        Assert.Equal(Vector.Of(4d, 5d, 6d), (u + v).Select(sums.Value));
        Assert.Equal((TimeSpan.FromMinutes(5), false), capped.Value(job));
        Assert.Equal((TimeSpan.FromMinutes(2), true), pinned.Value(job));
    }

    [Fact]
    public void TheLargestAndSmallestOfSeveralArePushedEitherWay() {
        var balanced = Optimum(Problem.Minimise(Max(X, Y)).SubjectTo(X.Between(0, 10) & Y.Between(0, 10) & (X + Y >= 7) & (X - Y <= 1)));
        var constraint = X.Between(0, 4) & Y.Between(0, 7) & (X + Y <= 8);

        Assert.Equal(3.5, balanced.ObjectiveValue, precision: Precision);
        Assert.Equal(3.5, balanced.Value(Max(X, Y)), precision: Precision);
        Assert.Equal(7, Optimum(Problem.Maximise(Max(X, Y)).SubjectTo(constraint)).ObjectiveValue, precision: Precision);
        Assert.Equal(4, Optimum(Problem.Maximise(Min(X, Y)).SubjectTo(constraint)).ObjectiveValue, precision: Precision);
        Assert.Equal(0, Optimum(Problem.Minimise(Min(X, Y)).SubjectTo(constraint)).ObjectiveValue, precision: Precision);
    }

    [Fact]
    public void AnAbsoluteValueBindsFromBothSidesAndDeviationsFromSeveralTargetsAreSummed() {
        var constraint = X.Between(0, 10) & (Abs(X - 5) >= 2);
        var targets = new double[] { 2, 4, 9 };
        var deviations = Optimum(Problem.Minimise(targets.Sum(target => Abs(X - target))).SubjectTo(X.Between(0, 10)));

        Assert.Equal(2, Optimum(Problem.Minimise(Abs(X - 5)).SubjectTo(constraint)).ObjectiveValue, precision: Precision);
        Assert.Equal(5, Optimum(Problem.Maximise(Abs(X - 5)).SubjectTo(constraint)).ObjectiveValue, precision: Precision);
        Assert.Equal(7, Optimum(Problem.Minimise(X).SubjectTo(constraint & (X >= 4))).ObjectiveValue, precision: Precision);
        Assert.IsType<Infeasible>(Solver.Solve(Problem.Satisfy(constraint & (Abs(X - 5) <= 1))));
        Assert.Equal(7, deviations.ObjectiveValue, precision: Precision);
        Assert.Equal(4, deviations.Value(X), precision: Precision);
    }

    [Fact]
    public void PiecewiseFunctionsNestAndMixWithLogic() {
        var inner = Max(X, Min(Y, 6));
        var constraint = X.Between(0, 3) & Y.Between(0, 10) & Flag.Iff(inner >= 5);

        Assert.Equal(6, Optimum(Problem.Maximise(inner).SubjectTo(constraint)).ObjectiveValue, precision: Precision);
        Assert.True(Optimum(Problem.Maximise(Y).SubjectTo(constraint)).Value(Flag));
        Assert.Equal(10 - 20, Optimum(Problem.Maximise(Y - 20 * Flag.Indicator).SubjectTo(constraint & (Y >= 5))).ObjectiveValue, precision: Precision);
    }

    [Fact]
    public void AStartingSolutionChangesNothingButTheRoute() {
        var problem = Problem.Minimise(StartA + StartB).SubjectTo(Horizon & ConflictFree & UsesA & UsesB);
        var optimum = Optimum(problem);
        var starts = new[] {
            optimum,
            Solution.Empty.With(StartA, 500).With(StartB, 1000).With(UsesA, true).With(UsesB, true),
            Solution.Empty.With(StartB, 1000),
            Solution.Empty.With(StartA, 10).With(StartB, 20).With(X, 3),
        };

        Assert.All(starts, start => Assert.Equal(Changeover, Assert.IsType<Optimal>(Solver.Solve(problem, startingFrom: start)).Solution.ObjectiveValue, precision: Precision));
    }

    [Fact]
    public void ObjectivesAreMetInOrderOfPriority() {
        var constraint = Horizon & ConflictFree & UsesA & UsesB;
        var earliestThenAFirst = Problem.Minimise(StartA + StartB).SubjectTo(constraint).Then(Objective.Minimise(StartA));
        var earliestThenBFirst = Problem.Minimise(StartA + StartB).SubjectTo(constraint).Then(Objective.Minimise(StartB));
        var withinAMinuteThenFarApart = Problem.Lexicographic([Objective.Minimise(StartA + StartB, absoluteTolerance: 60), Objective.Maximise(StartB - StartA)]).SubjectTo(constraint);

        Assert.Equal((0, Changeover), Starts(Assert.IsType<Optimal>(Solver.Solve(earliestThenAFirst))));
        Assert.Equal((Changeover, 0), Starts(Assert.IsType<Optimal>(Solver.Solve(earliestThenBFirst))));
        Assert.Equal((0, Changeover + 60), Starts(Assert.IsType<Optimal>(Solver.Solve(withinAMinuteThenFarApart))));
        Assert.Equal(Changeover + 60, Assert.IsType<Optimal>(Solver.Solve(withinAMinuteThenFarApart)).Solution.ObjectiveValue, precision: Precision);
    }

    private static (double, double) Starts(Optimal result) =>
        (Math.Round(result.Solution.Value(StartA), Precision), Math.Round(result.Solution.Value(StartB), Precision));

    [Fact]
    public void AnInfeasibleScheduleIsExplainedByTheConstraintsThatClash() {
        var constraint =
            Horizon
            & ConflictFree.WithName("changeover")
            & UsesA & UsesB
            & (StartA <= 100).WithName("A starts early")
            & X.Between(0, 5) & Y.Between(0, 5) & (X + Y <= 3)
            & (StartB <= 100).WithName("B starts early")
            & Flag.Implies(X >= 1);

        var conflict = Solver.FindConflict(Problem.Minimise(StartA + StartB).SubjectTo(constraint));

        Assert.Equal(["0 <= startA", "0 <= startB", "A starts early", "B starts early", "changeover", "usesA", "usesB"], conflict.Select(conjunct => conjunct.Name).Order(StringComparer.Ordinal));
        Assert.Empty(Solver.FindConflict(constraint.Conjuncts.Where(conjunct => !ReferenceEquals(conjunct, conflict[0])).AllOf()));
    }

    [Fact]
    public void AnIndicatorCountsWhetherItsConstraintHoldsAndAProductWithOneWhetherItsVariableIsSet() {
        var xs = Enumerable.Range(0, 3).Select(index => Variable.Continuous($"x{index}")).ToList();
        var domain = xs.AllOf(x => x.Between(0, 10)) & (xs.Sum() <= 12);
        var product = X.Between(2, 10) & Y.Between(0, 10) & (Flag.Indicator * X).EqualTo(Y);

        // At most two of three can reach 5 when together they come to no more than 12.
        Assert.Equal(2, Optimum(Problem.Maximise(xs.Sum(x => (x >= 5).Indicator)).SubjectTo(domain)).ObjectiveValue, precision: Precision);
        Assert.Equal(0, Optimum(Problem.Minimise(xs.Sum(x => (x >= 5).Indicator)).SubjectTo(domain & (xs.Sum() >= 12))).ObjectiveValue, precision: Precision);
        Assert.Equal(10, Optimum(Problem.Maximise(Y).SubjectTo(product)).ObjectiveValue, precision: Precision);
        Assert.Equal(0, Optimum(Problem.Minimise(Y).SubjectTo(product)).ObjectiveValue, precision: Precision);
        Assert.Equal(2, Optimum(Problem.Minimise(Y).SubjectTo(product & Flag)).ObjectiveValue, precision: Precision);
        Assert.False(Optimum(Problem.Maximise(X).SubjectTo(product & (Y <= 1))).Value(Flag));
    }

    [Fact]
    public void AConditionalTakesTheBranchItsConditionSelects() {
        var cost = If(X >= 6, 2 * X - 3, X + 1);
        var domain = X.Between(0, 10);

        Assert.Equal(17, Optimum(Problem.Maximise(cost).SubjectTo(domain)).ObjectiveValue, precision: Precision);
        Assert.Equal(1, Optimum(Problem.Minimise(cost).SubjectTo(domain)).ObjectiveValue, precision: Precision);
        Assert.Equal(9, Optimum(Problem.Minimise(cost).SubjectTo(domain & (X >= 6))).ObjectiveValue, precision: Precision);
        Assert.Equal(8, Optimum(Problem.Maximise(X).SubjectTo(domain & (cost <= 13))).ObjectiveValue, precision: Precision);
        Assert.Equal(15 + 4, Optimum(Problem.Maximise(cost + Flag.Indicator * Max(Y, 4)).SubjectTo(domain & Y.Between(0, 3) & (X <= 9))).ObjectiveValue, precision: Precision);
    }
}
