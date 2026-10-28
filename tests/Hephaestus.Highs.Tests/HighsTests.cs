using Hephaestus.Contracts;

namespace Hephaestus.Highs.Tests;

public sealed class HighsContract : SolverContract {
    protected override ISolver Solver => HighsSolver.Create();
}

public sealed class HighsWholeNumberContract : WholeNumberContract {
    protected override ISolver Solver => HighsSolver.Create();
}

public sealed class HighsSensitivityContract : SensitivityContract {
    protected override ISolver Solver => HighsSolver.Create();

    protected override IMilpBackend Backend => new HighsBackend();
}

public sealed class HighsBackendTests {
    private static readonly ContinuousVariable X = Variable.Continuous("x");
    private static readonly IntegerVariable N = Variable.Integer("n");

    [Fact]
    public void AProblemWithNoRowsAtAllIsSolved() =>
        Assert.Equal(7, Assert.IsType<Optimal>(HighsSolver.Create().Solve(Problem.Maximise(X + N).SubjectTo(X.Between(0, 2) & N.Between(0, 5)))).Solution.ObjectiveValue, precision: 6);

    [Fact]
    public void ACancelledSolveNeverStarts() =>
        Assert.Throws<OperationCanceledException>(() => HighsSolver.Create().Solve(Problem.Satisfy(X >= 0), cancellationToken: new CancellationToken(canceled: true)));

    [Fact]
    public void ItsLimitsAreAcceptedAndAnOptimalResultIsTightAgainstItsBound() {
        var result = Assert.IsType<Optimal>(HighsSolver.Create(options: new SolverOptions(TimeLimit: TimeSpan.FromSeconds(10), RelativeGap: 0, Threads: 2, AbsoluteGap: 0, Seed: 7)).Solve(Problem.Maximise(X + N).SubjectTo(X.Between(0, 1.5) & N.Between(0, 5) & (X + N <= 5.25))));

        Assert.Equal(5.25, result.Statistics.BestBound!.Value, precision: 6);
        Assert.Equal(0, result.RelativeGap!.Value, precision: 6);
        Assert.True(result.Statistics.SolvingTime > TimeSpan.Zero);
    }

    [Fact]
    public void ItsOwnOptionsAreAcceptedWhateverTheirType() =>
        Assert.IsType<Optimal>(HighsSolver.Create(options: SolverOptions.Default.With("mip_heuristic_effort", "0.1").With("mip_max_nodes", "1000").With("presolve", "off").With("mip_detect_symmetry", "false")).Solve(Problem.Maximise(N).SubjectTo(N.Between(0, 5))));

    [Fact]
    public void AnOptionItDoesNotKnowIsAnError() =>
        Assert.Contains("no_such_option", Assert.Throws<InvalidOperationException>(() => HighsSolver.Create(options: SolverOptions.Default.With("no_such_option", "1")).Solve(Problem.Satisfy(X >= 0))).Message);
}
