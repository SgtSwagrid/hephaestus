using System.Collections.Concurrent;
using Hephaestus.Gurobi;
using Hephaestus.OrTools;
using Hephaestus.Z3;

namespace Hephaestus.Solvers.Tests;

/// <summary>What each backend reports about a solve, and what it lets the caller tune.</summary>
public sealed class StatisticsAndOptionsTests {
    private static readonly IntegerVariable[] Slots = [.. Enumerable.Range(0, 5).Select(index => Variable.Integer($"slot{index}"))];

    /// <summary>Five jobs, one machine, ninety seconds apart: a whole-number problem that every backend here can take, with 90 * 10 as its optimum.</summary>
    private static readonly IOneShotProblem Queue = Problem.Minimise(Slots.Sum()).SubjectTo(Slots.AllOf(slot => slot.Between(0, 3600))
            & Slots.SelectMany((first, index) => Slots.Skip(index + 1).Select(second => (first + 90 <= second) | (second + 90 <= first))).AllOf());

    public static TheoryData<string> BoundingSolvers => ["SCIP", "HiGHS", "CP-SAT", "Gurobi", "Gurobi (big-M)"];

    private static ISolver Create(string solver, SolverOptions? options = null) =>
        solver switch {
            "SCIP" => OrToolsSolver.Create(OrToolsSolverId.Scip, options: options),
            "HiGHS" => OrToolsSolver.Create(OrToolsSolverId.Highs, options: options),
            "CP-SAT" => CpSatSolver.Create(options: options),
            "Gurobi" => GurobiLicence.Require(GurobiSolver.Create(options: options)),
            "Gurobi (big-M)" => GurobiLicence.Require(GurobiSolver.Create(options: options, useIndicators: false)),
            "Z3" => new Z3Solver(options),
            _ => throw new ArgumentOutOfRangeException(nameof(solver)),
        };

    private static readonly SolverOptions CommonOptions = new(TimeLimit: TimeSpan.FromSeconds(30), RelativeGap: 0, Threads: 2, AbsoluteGap: 0, Seed: 7);

    [Theory]
    [MemberData(nameof(BoundingSolvers))]
    public void TheCommonOptionsAreAcceptedAndAnOptimalResultIsTightAgainstItsBound(string solver) {
        var result = Assert.IsType<Optimal>(Create(solver, CommonOptions).Solve(Queue));

        Assert.Equal(900, result.Solution.ObjectiveValue);
        Assert.Equal(900, result.Statistics.BestBound!.Value, precision: 3);
        Assert.Equal(0, result.AbsoluteGap!.Value, precision: 3);
        Assert.Equal(0, result.RelativeGap!.Value, precision: 6);
        Assert.True(result.Statistics.SolvingTime > TimeSpan.Zero);
        Assert.True(result.Statistics.EncodingTime > TimeSpan.Zero);
    }

    [Fact]
    public void ASolverWithoutBoundsAcceptsTheCommonOptionsAndReportsItsTimesAndNoGap() {
        var result = Assert.IsType<Optimal>(Create("Z3", CommonOptions).Solve(Queue));

        Assert.Equal(900, result.Solution.ObjectiveValue);
        Assert.True(result.Statistics.SolvingTime > TimeSpan.Zero);
        Assert.Null(result.Statistics.BestBound);
        Assert.Null(result.RelativeGap);
    }

    [Fact]
    public void TheBoundAllowsForTheConstantInTheObjective() =>
        Assert.Equal(1900, Assert.IsType<Optimal>(Create("CP-SAT").Solve(Problem.Minimise(2 * Slots.Sum() + 100).SubjectTo(Queue.Constraint))).Statistics.BestBound!.Value, precision: 3);

    [Fact]
    public void InfeasibilityStillReportsItsTimes() =>
        Assert.True(Assert.IsType<Infeasible>(Create("SCIP").Solve(Problem.Satisfy(Queue.Constraint & (Slots.Sum() <= 899)))).Statistics.SolvingTime > TimeSpan.Zero);

    [Theory]
    [InlineData("SCIP", "limits/nodes", "100000")]
    [InlineData("CP-SAT", "linearization_level", "2")]
    [InlineData("Gurobi", "MIPFocus", "1")]
    [InlineData("Z3", "opt.priority", "lex")]
    public void TheSolversOwnParametersAreAccepted(string solver, string parameter, string value) =>
        Assert.Equal(900, Assert.IsType<Optimal>(Create(solver, SolverOptions.Default.With(parameter, value)).Solve(Queue)).Solution.ObjectiveValue);

    [Theory]
    [InlineData("SCIP")]
    [InlineData("Gurobi")]
    public void AParameterTheSolverDoesNotKnowIsAnError(string solver) =>
        Assert.ThrowsAny<Exception>(() => Create(solver, SolverOptions.Default.With("no_such_parameter", "1")).Solve(Queue));

    [Theory]
    [InlineData("CP-SAT")]
    [InlineData("Gurobi")]
    public void TheLogIsDeliveredToTheCaller(string solver) {
        var lines = new ConcurrentQueue<string>();

        Assert.IsType<Optimal>(Create(solver, new SolverOptions(Log: lines.Enqueue)).Solve(Queue));

        Assert.NotEmpty(lines);
    }

    [Fact]
    public void ALimitThatStopsTheSearchEarlyLeavesAGapToReport() {
        var slots = Enumerable.Range(0, 30).Select(index => Variable.Integer($"slot{index}")).ToList();
        var constraint = slots.AllOf(slot => slot.Between(0, 36000)) & slots.SelectMany((first, index) => slots.Skip(index + 1).Select(second => (first + 90 <= second) | (second + 90 <= first))).AllOf();

        var result = Create("SCIP", SolverOptions.Default.With("limits/solutions", "1")).Solve(Problem.Minimise(slots.Sum()).SubjectTo(constraint));

        Assert.True(Assert.IsType<Feasible>(result).RelativeGap > 0);
    }
}
