
using static Hephaestus.Piecewise;

namespace Hephaestus.Contracts;

/// <summary>
/// The contract for problems posed entirely in whole numbers, such as a schedule in whole seconds.
/// CP-SAT can only take part here; every other solver runs it too, so that they are seen to agree.
/// </summary>
public abstract class WholeNumberContract {
    protected abstract ISolver Solver { get; }

    private static readonly IntegerVariable StartA = Variable.Integer("startA");
    private static readonly IntegerVariable StartB = Variable.Integer("startB");
    private static readonly IntegerVariable StartC = Variable.Integer("startC");
    private static readonly BinaryVariable UsesA = Variable.Binary("usesA");
    private static readonly BinaryVariable UsesB = Variable.Binary("usesB");
    private static readonly IntegerVariable N = Variable.Integer("n");
    private static readonly BinaryVariable Flag = Variable.Binary("flag");

    private const double Changeover = 120;

    private static IBooleanExpression<ILinearArithmetic> Separated(ILinearExpression first, ILinearExpression second) =>
        (first + Changeover <= second) | (second + Changeover <= first);

    private Solution Optimum(IOneShotProblem problem) => Assert.IsType<Optimal>(Solver.Solve(problem)).Solution;

    [Fact]
    public void ThreeJobsQueueForOneMachine() {
        var starts = new[] { StartA, StartB, StartC };
        var constraint =
            starts.AllOf(start => start.Between(0, 3600))
            & (StartA >= 60) & (StartC >= 30)
            & Separated(StartA, StartB) & Separated(StartA, StartC) & Separated(StartB, StartC);

        var solution = Optimum(Problem.Minimise(starts.Sum()).SubjectTo(constraint));

        Assert.Equal(0 + 120 + 240, solution.ObjectiveValue);
        Assert.True(solution.Value(constraint));
    }

    [Fact]
    public void SeparationAppliesOnlyToJobsThatShareTheMachine() {
        var constraint = StartA.Between(0, 3600) & StartB.Between(0, 3600) & (!(UsesA & UsesB) | Separated(StartA, StartB));

        Assert.Equal(120, Optimum(Problem.Minimise(StartA + StartB).SubjectTo(constraint & UsesA & UsesB)).ObjectiveValue);
        Assert.Equal(0, Optimum(Problem.Minimise(StartA + StartB).SubjectTo(constraint & UsesA & !UsesB)).ObjectiveValue);
        Assert.Equal(120, Optimum(Problem.Minimise(StartA + StartB + 1000 * (2 - UsesA.Indicator - UsesB.Indicator)).SubjectTo(constraint)).ObjectiveValue);
    }

    [Fact]
    public void AWholeNumberProblemIsSolvedToItsWholeNumberOptimum() {
        var (a, b, c) = (Variable.Integer("a"), Variable.Integer("b"), Variable.Integer("c"));
        var constraint =
            (a >= 0) & (b >= 0) & (c >= 0)
            & (2 * a + 3 * b + c <= 5)
            & (4 * a + b + 2 * c <= 11)
            & (3 * a + 4 * b + 2 * c <= 8);

        var solution = Optimum(Problem.Maximise(5 * a + 4 * b + 3 * c).SubjectTo(constraint));

        Assert.Equal(13, solution.ObjectiveValue);
        Assert.Equal((2, 0, 1), (solution.Value(a), solution.Value(b), solution.Value(c)));
    }

    [Fact]
    public void LogicBindsAsItShould() {
        var domain = N.Between(0, 10);

        Assert.True(Optimum(Problem.Minimise(Flag.Indicator).SubjectTo(domain & Flag.Iff(N >= 5) & N.EqualTo(7))).Value(Flag));
        Assert.False(Optimum(Problem.Maximise(Flag.Indicator).SubjectTo(domain & Flag.Iff(N >= 5) & N.EqualTo(3))).Value(Flag));
        Assert.Equal(6, Optimum(Problem.Minimise(N).SubjectTo(domain & Flag & Flag.Implies(N >= 6))).Value(N));
        Assert.Equal(4, Optimum(Problem.Minimise(N).SubjectTo(domain & (N >= 3) & N.NotEqualTo(3))).Value(N));
        Assert.Equal(9, Optimum(Problem.Maximise(N).SubjectTo(domain & (N < 10) & ((N <= 2) ^ (N >= 8)))).Value(N));
    }

    [Fact]
    public void FractionalCoefficientsAndLimitsAreHandled() {
        var domain = N.Between(0, 100);

        Assert.Equal(5, Optimum(Problem.Maximise(N).SubjectTo(domain & (0.5 * N < 3))).Value(N));
        Assert.Equal(7, Optimum(Problem.Maximise(N).SubjectTo(domain & (0.25 * N + 0.125 * Flag.Indicator <= 1.9))).Value(N));
        Assert.IsType<Infeasible>(Solver.Solve(Problem.Satisfy(domain & (2 * N).EqualTo(5))));
        Assert.Equal(1, Optimum(Problem.Minimise(Flag.Indicator).SubjectTo(domain & (Flag | (2 * N).EqualTo(5)))).ObjectiveValue);
    }

    [Fact]
    public void BoundsThatAreOnlyImpliedAreEnough() {
        var finish = Variable.Integer("finish");
        var constraint = StartA.Between(0, 100) & N.Between(10, 20) & finish.EqualTo(StartA + N) & Flag.Iff(finish >= 90);

        var solution = Optimum(Problem.Maximise(finish - 50 * Flag.Indicator).SubjectTo(constraint));

        Assert.Equal(89, solution.Value(finish));
        Assert.False(solution.Value(Flag));
    }

    [Fact]
    public void InfeasibilityIsReported() {
        Assert.IsType<Infeasible>(Solver.Solve(Problem.Satisfy(N.Between(0, 10) & ((N <= 2) | (N >= 8)) & N.Between(3, 7))));
        Assert.IsType<Infeasible>(Solver.Solve(Problem.Satisfy(Flag & !Flag & N.Between(0, 1))));
    }

    [Fact]
    public void QuantisedTypedVariablesAndCountsAreReadBackAsWholeNumbersOfTheirOwnType() {
        var shiftStart = new DateTime(2026, 9, 19, 8, 0, 0);
        var start = Variable.DateTime("start", origin: shiftStart, inWholeUnits: true);
        var runtime = Variable.TimeSpan("runtime", unit: TimeSpan.FromSeconds(30), inWholeUnits: true);
        var (jobs, machines) = (Variable.Integer<int>("jobs"), Variable.Integer<int>("machines"));
        var units = Variable.Integer<long>("units");
        var schedule = start.Between(shiftStart, shiftStart.AddHours(1)) & runtime.Between(TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(5)) & (start >= shiftStart.AddMinutes(10) + runtime);
        var counts = jobs.Between(0, 40) & machines.Between(1, 6) & (jobs <= 6 * machines) & (units.Expression <= 850 * jobs.Expression) & (units >= 9000L);

        var scheduled = Optimum(Problem.Minimise(start).SubjectTo(schedule));
        var counted = Optimum(Problem.Minimise(100 * machines.Expression + 7 * jobs.Expression).SubjectTo(counts));

        Assert.Equal(TimeSpan.FromSeconds(60), scheduled.Value(runtime));
        Assert.Equal(shiftStart.AddMinutes(11), scheduled.Value(start));
        Assert.Equal((11, 2, 9000L), (counted.Value(jobs), counted.Value(machines), counted.Value(units)));
        Assert.IsType<int>(counted.Value(jobs));
    }

    [Fact]
    public void PiecewiseFunctionsAndConditionalsOfWholeNumbersStayWhole() {
        var domain = StartA.Between(0, 100) & StartB.Between(0, 100);
        var counted = StartA.Between(0, 100) & N.Between(0, 5);

        Assert.Equal(60, Optimum(Problem.Minimise(Max(StartA, StartB)).SubjectTo(domain & (StartA + StartB >= 120))).ObjectiveValue);
        Assert.Equal(100, Optimum(Problem.Maximise(Abs(StartA - StartB)).SubjectTo(domain)).ObjectiveValue);
        Assert.Equal(7, Optimum(Problem.Maximise(Min(StartA, 7)).SubjectTo(domain)).ObjectiveValue);
        Assert.Equal(40, Optimum(Problem.Minimise(StartA).SubjectTo(domain & (Abs(StartA - 50) <= 10) & (Max(StartA, StartB) >= 30))).ObjectiveValue);
        Assert.Equal(100 + 5, Optimum(Problem.Maximise(UsesA.Indicator * StartA + If(!UsesA, 200, N)).SubjectTo(counted & UsesA)).ObjectiveValue);
        Assert.Equal(200, Optimum(Problem.Maximise(UsesA.Indicator * StartA + If(!UsesA, 200, N)).SubjectTo(counted)).ObjectiveValue);
        Assert.Equal(3, Optimum(Problem.Minimise(If(StartA >= 50, N + 3, StartA)).SubjectTo(counted & (StartA >= 10))).ObjectiveValue);
    }

    [Fact]
    public void AStartingSolutionChangesNothingButTheRoute() {
        var problem = Problem.Minimise(StartA + StartB).SubjectTo(StartA.Between(0, 3600) & StartB.Between(0, 3600) & Separated(StartA, StartB));
        var starts = new[] { Solution.Empty.With(StartA, 300).With(StartB, 600), Solution.Empty.With(StartA, 0.4), Solution.Empty.With(StartA, 5).With(StartB, 6) };

        // The solver is fetched first, because a solver that has to be skipped says so by throwing, which Assert.All would count as a failure.
        var solver = Solver;

        Assert.All(starts, start => Assert.Equal(120, Assert.IsType<Optimal>(solver.Solve(problem, startingFrom: start)).Solution.ObjectiveValue));
    }

    [Fact]
    public void ObjectivesAreMetInOrderOfPriority() {
        var constraint = StartA.Between(0, 3600) & StartB.Between(0, 3600) & Separated(StartA, StartB);

        var solution = Optimum(Problem.Minimise(StartA + StartB).SubjectTo(constraint).Then(Objective.Minimise(StartB)).Then(Objective.Maximise(N)), N.Between(0, 3));

        Assert.Equal((120, 0, 3), (solution.Value(StartA), solution.Value(StartB), solution.Value(N)));
    }

    private Solution Optimum(IMultipleObjectiveProblem problem, IBooleanExpression<ILinearArithmetic> also) =>
        Assert.IsType<Optimal>(Solver.Solve(problem.SubjectTo(also))).Solution;
}
