using System.Collections.Immutable;
using static Hephaestus.Piecewise;

namespace Hephaestus.Tests;

public sealed class NamesAndConflictsTests {
    private static readonly IntegerVariable M = Variable.Integer("m");
    private static readonly IntegerVariable N = Variable.Integer("n");
    private static readonly BinaryVariable A = Variable.Binary("a");
    private static readonly ContinuousVariable X = Variable.Continuous("x");

    /// <summary>Tries every whole-number assignment (from -12 to 12, where a column has no bounds), and counts how often it is asked to.</summary>
    private sealed record CountingBackend(List<int> Sizes) : IMilpBackend {
        public ISolveResult Solve(MilpProblem problem, IReadOnlyDictionary<IVariable, double> start, SolverOptions options, CancellationToken cancellationToken) {
            Sizes.Add(problem.Rows.Length);
            return Assignments(problem.Columns).Any(assignment => problem.Rows.All(row => Holds(row, assignment)))
                ? new Optimal(new Solution(Assignments(problem.Columns).First(assignment => problem.Rows.All(row => Holds(row, assignment))), 0))
                : new Infeasible();
        }

        private static IEnumerable<ImmutableSortedDictionary<IVariable, double>> Assignments(IEnumerable<Column> columns) =>
            columns.Aggregate(
                (IEnumerable<ImmutableSortedDictionary<IVariable, double>>)[ImmutableSortedDictionary.Create<IVariable, double>(VariableOrder.Comparer)],
                (assignments, column) =>
                    from assignment in assignments
                    from value in Enumerable.Range((int)Math.Max(column.LowerBound, -12), (int)(Math.Min(column.UpperBound, 12) - Math.Max(column.LowerBound, -12)) + 1)
                    select assignment.Add(column.Variable, value));

        private static bool Holds(LinearRow row, ImmutableSortedDictionary<IVariable, double> assignment) =>
            new AffineForm(row.Coefficients, 0).Evaluate(variable => assignment[variable]) is var value && row.LowerBound - 1e-9 <= value && value <= row.UpperBound + 1e-9;
    }

    private static readonly ISolver Solver = new MilpSolver(new CountingBackend([]));

    [Fact]
    public void ANamedExpressionIsWrittenByItsNameWhereverItAppears() {
        var separated = ((M + 2 <= N) | (N + 2 <= M)).WithName("separated");
        var conflictFree = !A | separated;
        var origin = new DateTime(2026, 9, 19, 8, 0, 0);
        var (finish, start) = (Variable.DateTime("finish", origin), Variable.DateTime("start", origin));

        Assert.Equal("separated", separated.Format());
        Assert.Equal("!a | separated", conflictFree.Format());
        Assert.Equal("2*turnaround + m <= 9", (2 * (N - M).WithName("turnaround") + M <= 9).Format());
        Assert.Equal("(m + 2 <= n) | (n + 2 <= m)", ((INamedConstraint<ILinearArithmetic>)separated).Expression.Format());
        // Typed expressions can be named too.
        Assert.Equal("runtime >= 45", ((start - finish).WithName("runtime") >= TimeSpan.FromSeconds(45)).Format());
        Assert.Equal("release <= 600", ((finish + TimeSpan.FromMinutes(2)).WithName("release") <= origin.AddMinutes(10)).Format());
        // An expression goes by its name, or else by the way it is written.
        Assert.Equal("m <= n + 1", (M <= N + 1).Name);
        Assert.Equal("precedence", (M <= N + 1).WithName("precedence").Name);
        Assert.Equal("a", A.Name);
        Assert.Equal("busy", A.WithName("busy").Name);
    }

    [Fact]
    public void ANameChangesNothingAboutWhatAnExpressionMeans() {
        var plain = M.Between(0, 5) & ((M >= 3) | A) & (Max(M, N) <= 4);
        var named = M.Between(0, 5).WithName("domain") & ((M.WithName("emm") >= 3).WithName("late") | A.WithName("busy")).WithName("either") & (Max(M, N).WithName("latest") <= 4);

        Assert.Equal(Problem.Minimise(M).SubjectTo(plain).Encode().Format(), Problem.Minimise(M.WithName("objective")).SubjectTo(named).Encode().Format());
        Assert.Equal<IVariable>([.. plain.Variables], [.. named.Variables]);
        Assert.True(new Solution(Solution.Empty.Values.Add(M, 3).Add(N, 1).Add(A, 0), 0).Value(named));
        Assert.Equal((M + 1).Normalise(), (M.WithName("emm") + 1).Normalise());
    }

    [Fact]
    public void TheConstraintsOfAProblemAreTheConjunctsOfItsConstraint() {
        var grouped = ((M >= 1) & (N >= 1)).WithName("both positive");
        var constraint = M.Between(0, 5) & (A | (M <= N)) & grouped & BooleanConstant.True & new[] { N <= 4, N >= 0, M + N <= 7 }.AllOf();

        Assert.Equal(["0 <= m", "m <= 5", "a | (m <= n)", "both positive", "n <= 4", "n >= 0", "m + n <= 7"], constraint.Conjuncts.Select(conjunct => conjunct.Name));
        Assert.Equal(["a | (m <= n)"], (A | (M <= N)).Conjuncts.Select(conjunct => conjunct.Name));
        Assert.Empty(BooleanConstant.True.Conjuncts);
    }

    [Fact]
    public void AConflictIsASetOfConstraintsThatCannotAllHoldAndNoneOfWhichCanBeSpared() {
        var constraint =
            M.Between(0, 9) & N.Between(0, 9)
            & (M + 2 <= N).WithName("m before n")
            & (N <= 3).WithName("n early")
            & (M + N >= 1)
            & (M >= 2).WithName("m late")
            & (A | (N >= 1));

        var conflict = Solver.FindConflict(constraint);

        Assert.Equal(["m before n", "n early", "m late"], conflict.Select(conjunct => conjunct.Name));
        Assert.IsType<Infeasible>(Solver.Solve(Problem.Satisfy(conflict.AllOf())));
        Assert.All(conflict, spared => Assert.IsNotType<Infeasible>(Solver.Solve(Problem.Satisfy(conflict.Remove(spared).AllOf()))));
    }

    [Fact]
    public void BoundsTakePartInConflictsAndASingleConstraintCanBeOneAllByItselfButAFeasibleProblemHasNone() {
        Assert.Equal(["5 <= m", "n <= 6", "m + 2 <= n"], Solver.FindConflict(Problem.Minimise(M).SubjectTo(M.Between(5, 5) & N.Between(0, 6) & (M + 2 <= N))).Select(conjunct => conjunct.Name));
        Assert.Equal(["impossible"], Solver.FindConflict(M.Between(0, 3) & ((M >= 2) & (M <= 1)).WithName("impossible") & (N >= 0)).Select(conjunct => conjunct.Name));
        Assert.Empty(Solver.FindConflict(M.Between(0, 3) & (M >= 2)));
    }

    [Fact]
    public void AConflictAmongManyConstraintsIsFoundInFewSolves() {
        var sizes = new List<int>();
        var padding = Enumerable.Range(0, 200).Select(index => Variable.Binary($"flag{index}").Indicator + Variable.Binary($"flag{index + 1}").Indicator <= 2);
        var constraint = padding.Take(120).AllOf() & (M >= 1).WithName("low") & M.Between(0, 1) & padding.Skip(120).AllOf() & (M <= 0).WithName("high");

        var conflict = new MilpSolver(new CountingBackend(sizes)).FindConflict(constraint);

        Assert.Equal(["low", "high"], conflict.Select(conjunct => conjunct.Name));
        Assert.InRange(sizes.Count, 1, 40);
    }

    /// <summary>Never makes up its mind.</summary>
    private sealed record WaveringBackend : IMilpBackend {
        public ISolveResult Solve(MilpProblem problem, IReadOnlyDictionary<IVariable, double> start, SolverOptions options, CancellationToken cancellationToken) => new Unknown("Out of time.");
    }

    [Fact]
    public void ASolverThatCannotDecideCannotVouchForAConflict() =>
        Assert.Contains("Out of time", Assert.Throws<InvalidOperationException>(() => new MilpSolver(new WaveringBackend()).FindConflict(M.Between(0, 3) & (M >= 4))).Message);

    [Fact]
    public void BoundsKnowTheConstraintThatStatesThemAndWhereTwoStateTheSameBoundTheOneWrittenFirst() {
        var (loose, tight, floor) = ((M <= 8).WithName("loose"), (M <= 5).WithName("tight"), (M >= 1).WithName("floor"));
        var encoded = Problem.Minimise(Max(M, N)).SubjectTo(loose & tight & floor & N.EqualTo(3).WithName("fixed") & A).EncodeLogic();
        var twice =
            (M <= 5).WithName("upper first") & (M <= 5).WithName("upper second")
            & (M >= 1).WithName("lower first") & (M >= 1).WithName("lower second");
        var origins = Problem.Minimise(M).SubjectTo(twice).EncodeLogic().BoundOrigins[M];

        Assert.Equal(new BoundOrigin(floor, tight), encoded.BoundOrigins[M]);
        Assert.Equal("fixed", encoded.BoundOrigins[N].Lower!.Name);
        Assert.Same(encoded.BoundOrigins[N].Lower, encoded.BoundOrigins[N].Upper);
        Assert.Equal("a", encoded.BoundOrigins[A].Lower!.Name);
        Assert.Null(encoded.BoundOrigins[A].Upper);
        // The variable that stands for the maximum is bounded by derivation, which is nobody's constraint.
        Assert.DoesNotContain(encoded.Columns.Single(column => column.Variable.Name == "_max0").Variable, encoded.BoundOrigins.Keys);
        Assert.Equal("lower first", origins.Lower!.Name);
        Assert.Equal("upper first", origins.Upper!.Name);
    }

    /// <summary>Offers a fixed answer when asked to narrow a conflict down, and otherwise solves by trying everything.</summary>
    private sealed record OfferingBackend(Func<IndicatorProblem, ImmutableArray<IBooleanExpression<ILinearArithmetic>>> Offer, List<int> Sizes) : IMilpBackend, IConflictBackend {
        public ISolveResult Solve(MilpProblem problem, IReadOnlyDictionary<IVariable, double> start, SolverOptions options, CancellationToken cancellationToken) =>
            new CountingBackend(Sizes).Solve(problem, start, options, cancellationToken);

        public ImmutableArray<IBooleanExpression<ILinearArithmetic>> FindConflict(IndicatorProblem problem, SolverOptions options, CancellationToken cancellationToken) => Offer(problem);
    }

    private static IBooleanExpression<ILinearArithmetic> Padded(IBooleanExpression<ILinearArithmetic> low, IBooleanExpression<ILinearArithmetic> high) =>
        Enumerable.Range(0, 100).Select(index => Variable.Binary($"flag{index}").Indicator + Variable.Binary($"flag{index + 1}").Indicator <= 2).AllOf() & low & M.Between(0, 1) & (N >= 0) & high;

    [Fact]
    public void ASolverThatCanNarrowTheSearchDownIsOnlyAskedAboutWhatItOffers() {
        var (low, high, sizes) = ((M >= 1).WithName("low"), (M <= 0).WithName("high"), new List<int>());
        var solver = new MilpSolver(new OfferingBackend(problem => [low, problem.BoundOrigins[N].Lower!, high], sizes));

        var conflict = solver.FindConflict(Padded(low, high));

        Assert.Equal(["low", "high"], conflict.Select(conjunct => conjunct.Name));
        // None of the solves was of more than the three constraints on offer, of which one turned out to be spare.
        Assert.All(sizes, size => Assert.InRange(size, 0, 3));
    }

    [Fact]
    public void AnOfferThatIsNotInfeasibleAfterAllIsSetAside() {
        var (low, high) = ((M >= 1).WithName("low"), (M <= 0).WithName("high"));
        var solver = new MilpSolver(new OfferingBackend(problem => [low, problem.BoundOrigins[N].Lower!], []));

        Assert.Equal(["low", "high"], solver.FindConflict(Padded(low, high)).Select(conjunct => conjunct.Name));
        Assert.Empty(new MilpSolver(new OfferingBackend(_ => [low], [])).FindConflict(M.Between(0, 3) & low));
    }

    [Fact]
    public void WhatIsEvidentIsAnsweredWithoutAskingTheBackend() {
        var (low, high, never) = ((M >= 4).WithName("low"), (M <= 2).WithName("high"), (M + 1 <= M).WithName("never"));
        var solver = new MilpSolver(new OfferingBackend(_ => throw new InvalidOperationException("The backend should not have been asked."), []));

        Assert.Equal([low, high], solver.NarrowConflict(Problem.Satisfy((N >= 0) & low & high)));
        Assert.Equal([never], solver.NarrowConflict(Problem.Satisfy((N >= 0) & never & (M >= 0))));
    }
}
