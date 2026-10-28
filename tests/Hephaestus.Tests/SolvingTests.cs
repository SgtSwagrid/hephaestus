using System.Collections.Immutable;

namespace Hephaestus.Tests;

/// <summary>
/// The solving pipeline, driven by a backend that simply tries every whole-number assignment:
/// slow, but obviously correct, and proof that a backend needs to know nothing beyond columns and rows.
/// </summary>
public sealed class SolvingTests {
    private sealed record ExhaustiveBackend : IMilpBackend {
        public ISolveResult Solve(MilpProblem problem, IReadOnlyDictionary<IVariable, double> start, SolverOptions options, CancellationToken cancellationToken) =>
            Assignments(problem.Columns)
                .Where(assignment => problem.Rows.All(row => Holds(row, assignment)))
                .Select(assignment => new Solution(assignment, problem.Objective.Evaluate(variable => assignment[variable])))
                .OrderBy(solution => problem.Sense == ObjectiveSense.Minimise ? solution.ObjectiveValue : -solution.ObjectiveValue)
                .FirstOrDefault() is { } best
                    ? new Optimal(best)
                    : new Infeasible();

        private static IEnumerable<ImmutableSortedDictionary<IVariable, double>> Assignments(IEnumerable<Column> columns) =>
            columns.Aggregate(
                (IEnumerable<ImmutableSortedDictionary<IVariable, double>>)[ImmutableSortedDictionary.Create<IVariable, double>(VariableOrder.Comparer)],
                (assignments, column) =>
                    from assignment in assignments
                    from value in Enumerable.Range((int)column.LowerBound, (int)(column.UpperBound - column.LowerBound) + 1)
                    select assignment.Add(column.Variable, value));

        private static bool Holds(LinearRow row, ImmutableSortedDictionary<IVariable, double> assignment) =>
            new AffineForm(row.Coefficients, 0).Evaluate(variable => assignment[variable]) is var value
            && row.LowerBound - 1e-9 <= value && value <= row.UpperBound + 1e-9;
    }

    private static readonly ISolver Solver = new MilpSolver(new ExhaustiveBackend());
    private static readonly IntegerVariable M = Variable.Integer("m");
    private static readonly IntegerVariable N = Variable.Integer("n");
    private static readonly BinaryVariable A = Variable.Binary("a");
    private static readonly IBooleanExpression<ILinearArithmetic> Domain = M.Between(0, 5) & N.Between(0, 5);

    [Fact]
    public void ADisjunctiveProblemIsSolvedThroughTheEncodingAndItsAuxiliariesNeverLeakIntoTheSolution() {
        var separated = (M + 2 <= N) | (N + 2 <= M);

        var solution = Assert.IsType<Optimal>(Solver.Solve(Problem.Minimise(M + 2 * N).SubjectTo(Domain & separated))).Solution;

        Assert.Equal(2, solution.ObjectiveValue);
        Assert.Equal(2, solution.Value(M));
        Assert.Equal(0, solution.Value(N));
        Assert.True(solution.Value(separated));
        Assert.Equal([M, N], solution.Values.Keys);
    }

    [Fact]
    public void AnyExpressionCanBeReadOffASolutionButNotAVariableTheProblemNeverMentioned() {
        var solution = Assert.IsType<Optimal>(Solver.Solve(Problem.Maximise(M - N).SubjectTo(Domain & A.Iff(M >= 3)))).Solution;

        Assert.Equal(5, solution.Value(M - N));
        Assert.Equal(11, solution.Value(2 * M + 1));
        Assert.True(solution.Value(A));
        Assert.Equal(1, solution.Value(A.Indicator));
        Assert.True(solution.Value(A & (M > N)));
        Assert.False(solution.Value(A.Implies(N >= 1)));
        Assert.Throws<KeyNotFoundException>(() => solution.Value(Variable.Integer("stranger")));
    }

    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 0)]
    public void KnownConditionsSwitchConstraintsOnAndOff(bool isUrgent, double expected) =>
        Assert.Equal(expected, Assert.IsType<Optimal>(Solver.Solve(Problem.Minimise(M).SubjectTo(Domain & isUrgent.Implies(M >= 3) & (!isUrgent | (N <= 1))))).Solution.Value(M));

    [Fact]
    public void InfeasibilityIsReported() {
        Assert.IsType<Infeasible>(Solver.Solve(Problem.Satisfy(Domain & (M + N >= 11))));
        Assert.IsType<Infeasible>(Solver.Solve(Problem.Satisfy(Domain & (M >= 6))));
    }

    [Fact]
    public async Task SolvingCanBeAwaited() =>
        Assert.IsType<Optimal>(await Solver.SolveAsync(Problem.Maximise(M).SubjectTo(Domain), cancellationToken: TestContext.Current.CancellationToken));

    /// <summary>Solves nothing, but remembers what it was asked to start from.</summary>
    private sealed record RecordingBackend(List<IReadOnlyDictionary<IVariable, double>> Starts) : IMilpBackend {
        public ISolveResult Solve(MilpProblem problem, IReadOnlyDictionary<IVariable, double> start, SolverOptions options, CancellationToken cancellationToken) {
            Starts.Add(start);
            return new Unknown("Nothing was solved.");
        }
    }

    [Fact]
    public void AStartingSolutionReachesTheBackendAsValuesForItsOwnColumns() {
        var (hinted, unhinted) = (new RecordingBackend([]), new RecordingBackend([]));
        var hint = Solution.Empty.With(M, 2.2).With(A, true).With(Variable.Integer("stranger"), 9);

        new MilpSolver(hinted).Solve(Problem.Minimise(M).SubjectTo(Domain & ((M >= 2) | A)), startingFrom: hint);
        new MilpSolver(unhinted).Solve(Problem.Minimise(M).SubjectTo(Domain));

        // The stranger is dropped, the whole-number variable is rounded, and n and the auxiliary binary are left to the solver.
        Assert.Equal([KeyValuePair.Create<IVariable, double>(A, 1), KeyValuePair.Create<IVariable, double>(M, 2)], hinted.Starts.Single().OrderBy(entry => entry.Key.Name));
        // Without a starting solution, the backend starts from nothing.
        Assert.Empty(unhinted.Starts.Single());
    }

    [Fact]
    public void AStartingSolutionCanBeBuiltByHandInTheTypesOfTheModel() {
        var origin = new DateTime(2026, 9, 19, 8, 0, 0);
        var start = Variable.DateTime("start", origin);
        var runtime = Variable.TimeSpan("runtime", unit: TimeSpan.FromMinutes(1));

        var hint = Solution.Empty.With(start, origin.AddMinutes(5)).With(runtime, TimeSpan.FromSeconds(90)).With(A, false);

        Assert.Equal(300, hint.Values[Variable.Continuous("start")]);
        Assert.Equal(1.5, hint.Values[Variable.Continuous("runtime")]);
        Assert.Equal(0, hint.Values[A]);
        Assert.Throws<ArgumentException>(() => Solution.Empty.With(start + runtime, origin));
    }

    /// <summary>Solves with the exhaustive backend, and remembers what each solve was asked and where it started from.</summary>
    private sealed record WatchedBackend(List<(MilpProblem Problem, IReadOnlyDictionary<IVariable, double> Start)> Solves) : IMilpBackend {
        public ISolveResult Solve(MilpProblem problem, IReadOnlyDictionary<IVariable, double> start, SolverOptions options, CancellationToken cancellationToken) {
            Solves.Add((problem, start));
            return new ExhaustiveBackend().Solve(problem, start, options, cancellationToken);
        }
    }

    private static readonly IBooleanExpression<ILinearArithmetic> Linked = Domain & (N <= M + 1);

    [Fact]
    public void ALaterObjectiveOnlyChoosesAmongTheBestForTheEarlierOnes() {
        var solution = Assert.IsType<Optimal>(Solver.Solve(Problem.Minimise(M).SubjectTo(Linked).Then(Objective.Maximise(N)))).Solution;

        Assert.Equal((0, 1), (solution.Value(M), solution.Value(N)));
        Assert.Equal(0, solution.ObjectiveValue);
    }

    [Fact]
    public void ATolerancedObjectiveGivesGroundToThoseAfterIt() {
        var byAmount = Problem.Lexicographic([Objective.Minimise(M, absoluteTolerance: 2), Objective.Maximise(N)]).SubjectTo(Linked);
        var byFraction = Problem.Lexicographic([Objective.Maximise(M + 5, relativeTolerance: 0.2), Objective.Minimise(N)]).SubjectTo(Linked & (N >= M - 1));

        Assert.Equal((2, 3), Read(Assert.IsType<Optimal>(Solver.Solve(byAmount)).Solution));
        Assert.Equal((3, 2), Read(Assert.IsType<Optimal>(Solver.Solve(byFraction)).Solution));
    }

    private static (double, double) Read(Solution solution) => (solution.Value(M), solution.Value(N));

    [Fact]
    public void ObjectivesCanBeChainedAtLength() {
        var problem = Problem.Satisfy(Linked).Then(Objective.Maximise(M + N)).Then(Objective.Minimise(M)).Then(Objective.Maximise(A.Indicator)).Then(Objective.Minimise(N));

        var solution = Assert.IsType<Optimal>(Solver.Solve(problem)).Solution;

        Assert.Equal(4, problem.Objective.Priorities.Length);
        Assert.Equal((5, 5), Read(solution));
        Assert.Equal(10, solution.ObjectiveValue);
    }

    [Fact]
    public void EachStageStartsFromTheSolutionBeforeAndIsHeldToItsValue() {
        var backend = new WatchedBackend([]);

        new MilpSolver(backend).Solve(Problem.Minimise(M).SubjectTo(Linked).Then(Objective.Maximise(N)), startingFrom: Solution.Empty.With(M, 4));

        Assert.Equal([4.0], backend.Solves[0].Start.Values);
        Assert.Equal([0.0, 0.0], backend.Solves[1].Start.OrderBy(entry => entry.Key.Name).Select(entry => entry.Value));
        Assert.Equal(0, backend.Solves[1].Problem.Columns.Single(column => column.Variable.Equals(M)).UpperBound, precision: 6);
    }

    [Fact]
    public void WithoutASolutionTheFirstStagesOutcomeStands() {
        Assert.IsType<Infeasible>(Solver.Solve(Problem.Minimise(M).SubjectTo(Linked & (M >= 9)).Then(Objective.Maximise(N))));
        Assert.IsType<Optimal>(Solver.Solve(Problem.Lexicographic([]).SubjectTo(Linked)));
    }

    /// <summary>Gives up on every solve after its first.</summary>
    private sealed record FlaggingBackend(List<int> Count) : IMilpBackend {
        public ISolveResult Solve(MilpProblem problem, IReadOnlyDictionary<IVariable, double> start, SolverOptions options, CancellationToken cancellationToken) {
            Count.Add(0);
            return Count.Count == 1 ? new ExhaustiveBackend().Solve(problem, start, options, cancellationToken) : new Unknown("Out of time.");
        }
    }

    [Fact]
    public void AStageThatGivesUpLeavesTheSolutionBeforeItUnproven() {
        var result = new MilpSolver(new FlaggingBackend([])).Solve(Problem.Minimise(M).SubjectTo(Linked).Then(Objective.Maximise(N)).Then(Objective.Maximise(A.Indicator)));

        Assert.Equal(0, Assert.IsType<Feasible>(result).Solution.Value(M));
    }

    [Fact]
    public void TypedObjectivesPursueTheirExpressionsAndTakeTypedTolerances() {
        var origin = new DateTime(2026, 9, 19, 8, 0, 0);
        var delay = Variable.TimeSpan("delay", unit: TimeSpan.FromMinutes(1));
        var start = Variable.DateTime("start", origin);

        Assert.Equal(new Prioritised(Objective.Minimise(delay.Expression), 1.5), Objective.Minimise(delay, tolerance: TimeSpan.FromSeconds(90)));
        Assert.Equal(new Prioritised(Objective.Maximise(delay.Expression), 0, 0.1), Objective.Maximise(delay, TimeSpan.Zero, relativeTolerance: 0.1));
        Assert.Equal(new Prioritised(Objective.Minimise(start.Expression), 30), Objective.Minimise(start, tolerance: TimeSpan.FromSeconds(30)));
        // Without a tolerance each is an Optimisation, not a Prioritised, so it can start a problem or a list of objectives.
        Assert.IsType<Optimisation>(Objective.Minimise(delay));
        Assert.IsType<Optimisation>(Objective.Maximise(start));
        Assert.Equal(new SingleObjectiveProblem(Objective.Minimise(delay.Expression), BooleanConstant.True), Problem.Optimise(Objective.Minimise(delay)));
        Assert.Equal(new SingleObjectiveProblem(Objective.Minimise(delay.Expression), BooleanConstant.True), Problem.Minimise(delay).SubjectTo(BooleanConstant.True));
        Assert.Equal(new SingleObjectiveProblem(Objective.Maximise(start.Expression), BooleanConstant.True), Problem.Maximise(start).SubjectTo(BooleanConstant.True));
        Assert.Equal([Objective.Maximise(start.Expression), Objective.Minimise(delay.Expression)], Objective.Maximise(start).Then(Objective.Minimise(delay)).Priorities.Select(priority => priority.Objective));
    }

    [Fact]
    public void AProblemIsBuiltUpOneStepAtATimeAndEachStepIsAProblem() {
        var unconstrained = Problem.Minimise(M);
        var bounded = unconstrained.SubjectTo(Domain);
        var tightened = bounded.SubjectTo(M >= 3).SubjectTo(A | (N >= 1));

        Assert.Equal(new SingleObjectiveProblem(Objective.Minimise(M), BooleanConstant.True), unconstrained);
        Assert.Empty(unconstrained.Constraint.Conjuncts);
        Assert.Empty(unconstrained.Encode().Rows);
        Assert.Equal(new SingleObjectiveProblem(Objective.Minimise(M), Domain), bounded);
        Assert.Equal(new SingleObjectiveProblem(Objective.Minimise(M), Domain & (M >= 3) & (A | (N >= 1))), tightened);
        Assert.Equal(new SingleObjectiveProblem(Objective.Maximise(M), Domain & A), Problem.Maximise(M).SubjectTo(Domain).SubjectTo(A));
        Assert.Equal(new SatisfactionProblem(Domain & A), Problem.Satisfy(Domain).SubjectTo(A));
        Assert.Equal(0, Assert.IsType<Optimal>(Solver.Solve(bounded)).Solution.ObjectiveValue);
        Assert.Equal(3, Assert.IsType<Optimal>(Solver.Solve(tightened)).Solution.ObjectiveValue);
    }

    [Fact]
    public void SeveralConstraintsGivenAtOnceMustAllHold() {
        var several = new List<IBooleanExpression<ILinearArithmetic>> { M >= 3, N <= 2, A };

        Assert.Equal(new SingleObjectiveProblem(Objective.Minimise(M), Domain & (M >= 3)), Problem.Minimise(M).SubjectTo(Domain, M >= 3));
        Assert.Equal(new SingleObjectiveProblem(Objective.Minimise(M), Domain & several.AllOf()), Problem.Minimise(M).SubjectTo(Domain).SubjectTo(several));
        Assert.Equal(Problem.Minimise(M).SubjectTo(Domain), Problem.Minimise(M).SubjectTo(Domain).SubjectTo(Array.Empty<IBooleanExpression<ILinearArithmetic>>()));
        Assert.Equal(Domain & A & N.EqualTo(1), Problem.Minimise(M, N).SubjectTo(Domain, A, N.EqualTo(1)).Constraint.Conjuncts.AllOf(), EqualAsConjunctions);
        Assert.Equal(3, Assert.IsType<Optimal>(Solver.Solve(Problem.Minimise(M).SubjectTo(Domain, M >= 3, A))).Solution.ObjectiveValue);
    }

    private static bool EqualAsConjunctions(IBooleanExpression<ILinearArithmetic> left, IBooleanExpression<ILinearArithmetic> right) => left.Conjuncts.SequenceEqual(right.Conjuncts);

    [Fact]
    public void ObjectivesAndConstraintsMayComeInAnyOrder() {
        var objectives = new[] { new Prioritised(Objective.Minimise(M)), new Prioritised(Objective.Maximise(N), 1), new Prioritised(Objective.Minimise(A.Indicator), 0, 0.5) };
        var problem = Problem.Minimise(M).SubjectTo(Domain).ThenMaximise(N, absoluteTolerance: 1).SubjectTo(N <= M + 1).ThenMinimise(A.Indicator, relativeTolerance: 0.5);

        Assert.Equal(objectives, problem.Objective.Priorities);
        Assert.Equal(Domain & (N <= M + 1), problem.Constraint);
        Assert.Equal(problem.Objective.Priorities, Problem.Lexicographic(objectives).SubjectTo(Domain).Objective.Priorities);
        Assert.Equal(BooleanConstant.True, Problem.Lexicographic(objectives).Constraint);
        Assert.Equal(0, Assert.IsType<Optimal>(Solver.Solve(problem)).Solution.Value(M));
    }

    [Fact]
    public void SeveralObjectivesGivenAtOnceAreTakenInOrderWhetherOnAProblemOrAmongThemselves() {
        var chained = Problem.Minimise(M).ThenMinimise(N).ThenMinimise(A.Indicator).SubjectTo(Domain);
        var objectives = Objective.Minimise(M).ThenMaximise(N, absoluteTolerance: 1).ThenMinimise(A.Indicator, M + N);

        Assert.Equal(chained.Objective.Priorities, Problem.Minimise(M, N, A.Indicator).SubjectTo(Domain).Objective.Priorities);
        Assert.Equal(chained.Objective.Priorities, Problem.Minimise(new ILinearExpression[] { M, N, A.Indicator }).Objective.Priorities);
        Assert.Equal(chained.Objective.Priorities, Problem.Minimise(M).ThenMinimise(N, A.Indicator).Objective.Priorities);
        Assert.Equal(chained.Objective.Priorities, Problem.Minimise(M).ThenMinimise(new List<IntegerVariable> { N }).ThenMinimise(A.Indicator).Objective.Priorities);
        Assert.Equal(
            [new Prioritised(Objective.Maximise(M)), new Prioritised(Objective.Maximise(N)), new Prioritised(Objective.Minimise(A.Indicator)), new Prioritised(Objective.Maximise(M + N))],
            Problem.Maximise(M, N).ThenMinimise(A.Indicator).ThenMaximise(M + N, M + N).Objective.Priorities.Take(4));
        // One expression is still an ordinary problem, and a number after it is still a tolerance.
        Assert.IsType<SingleObjectiveProblem>(Problem.Minimise(M));
        Assert.Equal(new Prioritised(Objective.Minimise(N), 2), Problem.Minimise(M).ThenMinimise(N, 2).Objective.Priorities[1]);
        Assert.Equal((5, 5), Read(Assert.IsType<Optimal>(Solver.Solve(Problem.Maximise(M, N).SubjectTo(Linked))).Solution));
        // Objectives chain among themselves just as problems do.
        Assert.Equal(
            [new Prioritised(Objective.Minimise(M)), new Prioritised(Objective.Maximise(N), 1), new Prioritised(Objective.Minimise(A.Indicator)), new Prioritised(Objective.Minimise(M + N))],
            objectives.Priorities);
        Assert.Equal(objectives.Priorities, Problem.Minimise(M).ThenMaximise(N, absoluteTolerance: 1).ThenMinimise(A.Indicator, M + N).Objective.Priorities);
        Assert.Equal(objectives.Priorities, Objective.None.ThenMinimise(M).ThenMaximise(N, 1).ThenMinimise(new ILinearExpression[] { A.Indicator, M + N }).Priorities);
    }

    [Fact]
    public void AProblemOfEitherKindIsSolvedAndExplainedThroughTheirCommonType() {
        IProblem single = Problem.Maximise(M).SubjectTo(Linked);
        IProblem several = Problem.Maximise(M).ThenMinimise(N).SubjectTo(Linked);

        Assert.Equal((5, 0), Read(Assert.IsType<Optimal>(Solver.Solve(several)).Solution));
        Assert.Equal(5, Assert.IsType<Optimal>(Solver.Solve(single)).Solution.ObjectiveValue);
        Assert.Equal([new Prioritised(Objective.Maximise(M))], single.Objective.Priorities);
        Assert.Empty(Problem.Satisfy(Linked).Objective.Priorities);
        Assert.Empty(Solver.FindConflict(several));
        Assert.IsAssignableFrom<IMultipleObjectiveProblem>(several.SubjectTo(M >= 1, N >= 1));
    }

    [Fact]
    public void AnObjectiveIsAValueThatCanBeSetAgainstOneConstraintAfterAnother() {
        var earliest = Objective.Minimise(M);
        var earliestThenFullest = earliest.Then(Objective.Maximise(N)).Then(Objective.Minimise(A.Indicator, absoluteTolerance: 1));

        Assert.Equal(0, Assert.IsType<Optimal>(Solver.Solve(Problem.Optimise(earliest).SubjectTo(Linked))).Solution.ObjectiveValue);
        Assert.Equal(2, Assert.IsType<Optimal>(Solver.Solve(Problem.Optimise(earliest).SubjectTo(Linked, M >= 2))).Solution.ObjectiveValue);
        Assert.Equal((0, 1), Read(Assert.IsType<Optimal>(Solver.Solve(Problem.Optimise(earliestThenFullest).SubjectTo(Linked))).Solution));
        Assert.Equal((3, 4), Read(Assert.IsType<Optimal>(Solver.Solve(Problem.Optimise(earliestThenFullest).SubjectTo(Linked, M >= 3))).Solution));
    }

    [Fact]
    public void TheKindOfObjectiveIsTheKindOfProblemAndStaysSoAsItIsBuiltUp() {
        Assert.Equal(new SatisfactionProblem(Linked), Problem.Satisfy(Linked));
        Assert.IsType<SatisfactionProblem>(Problem.Optimise(Objective.None));
        Assert.IsType<SatisfactionProblem>(Problem.Satisfy(Linked).SubjectTo(A).SubjectTo(M >= 1));
        Assert.Equal(Objective.None, Problem.Satisfy(Linked).SubjectTo(A).Objective);
        Assert.Equal(new SingleObjectiveProblem(new Optimisation(ObjectiveSense.Maximise, M), Linked), Problem.Maximise(M).SubjectTo(Linked));
        Assert.IsAssignableFrom<ILexicographicObjective>(Problem.Maximise(M).ThenMinimise(N).Objective);
        Assert.IsAssignableFrom<ISingleObjective>(Problem.Maximise(M).SubjectTo(Linked).Objective);
        Assert.Empty(Objective.None.Priorities);
        Assert.Equal(new NoObjective(), Objective.None);
        Assert.Equal(Problem.Satisfy(Linked), Problem.Optimise(Objective.None).SubjectTo(Linked));
        Assert.Equal([new Prioritised(Objective.Maximise(M))], Objective.Maximise(M).Priorities);
        // Only an objective that has its place among several carries a tolerance; alone, there is nothing for it to give way to.
        Assert.Equal(new Prioritised(new Optimisation(ObjectiveSense.Minimise, N), 0, 0.1), Objective.Minimise(N, relativeTolerance: 0.1));
        Assert.IsType<Optimisation>(Objective.Minimise(N));
    }
}
