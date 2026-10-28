using Hephaestus.Contracts;
using Hephaestus.Gurobi;
using Hephaestus.OrTools;
using Hephaestus.Z3;

namespace Hephaestus.Solvers.Tests;

public sealed class CpSatWholeNumberContract : WholeNumberContract {
    protected override ISolver Solver => CpSatSolver.Create();
}

public sealed class ScipWholeNumberContract : WholeNumberContract {
    protected override ISolver Solver => OrToolsSolver.Create(OrToolsSolverId.Scip);
}

public sealed class Z3WholeNumberContract : WholeNumberContract {
    protected override ISolver Solver => new Z3Solver();
}

public sealed class GurobiWholeNumberContract : WholeNumberContract {
    protected override ISolver Solver => GurobiLicence.Require(GurobiSolver.Create());
}

/// <summary>What CP-SAT cannot take, it must say so; and where it needs bounds, it must say which.</summary>
public sealed class CpSatRefusalTests {
    private static readonly IntegerVariable N = Variable.Integer("n");

    [Fact]
    public void ContinuousVariablesAreRefusedByName() {
        var exception = Assert.Throws<NotSupportedException>(() => CpSatSolver.Create().Solve(Problem.Minimise(N).SubjectTo(N.Between(0, 5) & (Variable.Continuous("x") >= 1))));

        Assert.Contains("'x'", exception.Message);
    }

    [Fact]
    public void VariablesWithoutFiniteDomainsAreNamed() {
        var exception = Assert.Throws<ModellingException>(() => CpSatSolver.Create().Solve(Problem.Minimise(N).SubjectTo((N >= 0) & (Variable.Integer("m") <= N))));

        Assert.Contains("'n'", exception.Message);
        Assert.Contains("'m'", exception.Message);
    }

    [Fact]
    public void CoefficientsThatNoPowerOfTenMakesWholeAreRefused() =>
        Assert.Throws<NotSupportedException>(() => CpSatSolver.Create().Solve(Problem.Satisfy(N.Between(0, 5) & (N / 3 + Variable.Integer("m") <= 1) & Variable.Integer("m").Between(0, 5))));
}

/// <summary>CP-SAT works in whole numbers, so the bounds it is given must be narrowed to whole numbers the right way.</summary>
public sealed class CpSatDomainTests {
    private static readonly IntegerVariable N = Variable.Integer("n");

    [Theory]
    [InlineData(-4.5, -0.5, -4, -1)]
    [InlineData(0.5, 4.5, 1, 4)]
    public void FractionalBoundsOnAWholeNumberColumnRoundInwards(double lower, double upper, double smallest, double largest) {
        var problem = new IndicatorProblem([new Column(N, lower, upper, IsAuxiliary: false)], [], ObjectiveSense.Minimise, AffineForm.Zero);

        Assert.Equal(smallest, Optimum(problem with { Sense = ObjectiveSense.Minimise }));
        Assert.Equal(largest, Optimum(problem with { Sense = ObjectiveSense.Maximise }));
    }

    private static double Optimum(IndicatorProblem problem) =>
        Assert.IsType<Optimal>(new CpSatBackend().Solve(
            problem with { Objective = AffineForm.Zero.PlusTerm(N, 1) },
            Solution.Empty.Values,
            SolverOptions.Default,
            CancellationToken.None)).Solution.Values[N];
}
