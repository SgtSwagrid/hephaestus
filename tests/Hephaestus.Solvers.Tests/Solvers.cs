using Hephaestus.Contracts;
using Hephaestus.Gurobi;
using Hephaestus.OrTools;
using Hephaestus.Z3;

namespace Hephaestus.Solvers.Tests;

public sealed class ScipContract : SolverContract {
    protected override ISolver Solver => OrToolsSolver.Create(OrToolsSolverId.Scip);
}

public sealed class CbcContract : SolverContract {
    protected override ISolver Solver => OrToolsSolver.Create(OrToolsSolverId.Cbc);
}

/// <summary>HiGHS as bundled with OR-Tools. (The standalone backend is tested in its own project: the two ship clashing native libraries.)</summary>
public sealed class OrToolsHighsContract : SolverContract {
    protected override ISolver Solver => OrToolsSolver.Create(OrToolsSolverId.Highs);
}

public sealed class Z3Contract : SolverContract {
    protected override ISolver Solver => new Z3Solver();
}

/// <summary>Gurobi with conditional constraints as native indicator constraints: no big-M anywhere.</summary>
public sealed class GurobiIndicatorContract : SolverContract {
    protected override ISolver Solver => GurobiLicence.Require(GurobiSolver.Create());
}

/// <summary>Gurobi with the classic formulation: a big-M derived for every conditional row.</summary>
public sealed class GurobiBigMContract : SolverContract {
    protected override ISolver Solver => GurobiLicence.Require(GurobiSolver.Create(useIndicators: false));
}

/// <summary>Gurobi is commercial; where no licence can be found its tests are skipped rather than failed.</summary>
internal static class GurobiLicence {
    private static readonly Lazy<string?> Missing = new(() => {
        try {
            return new GurobiBackend().Solve(Problem.Satisfy(BooleanConstant.True).EncodeLogic(), Solution.Empty.Values, SolverOptions.Default, CancellationToken.None) is null ? "unreachable" : null;
        } catch (global::Gurobi.GRBException exception) {
            return exception.Message;
        }
    });

    public static ISolver Require(ISolver solver) {
        Assert.SkipWhen(Missing.Value is not null, $"Gurobi is not licensed here: {Missing.Value}");
        return solver;
    }
}

/// <summary>
/// Pure LP solvers cannot take part in the contract. OR-Tools would let them accept whole-number
/// variables and silently relax them, reporting a wrong "optimum"; the backend must refuse instead.
/// </summary>
public sealed class LinearProgrammingSolverTests {
    private static readonly ContinuousVariable X = Variable.Continuous("x");
    private static readonly ContinuousVariable Gap = Variable.Continuous("gap");
    private static readonly IBooleanExpression<ILinearArithmetic> Domain = X.Between(0, 10) & Gap.Between(0, 10) & (Gap >= X - 5) & (Gap >= 5 - X);

    [Fact]
    public void ADisjunctionIsRefusedRatherThanSilentlyRelaxed() =>
        Assert.All(
            new[] { OrToolsSolverId.Glop, "CLP", "PDLP" },
            solverId => Assert.Contains("_aux0", Assert.Throws<NotSupportedException>(() => OrToolsSolver.Create(solverId).Solve(Problem.Minimise(Gap).SubjectTo(Domain & ((X <= 3) | (X >= 7))))).Message));

    [Fact]
    public void APurelyContinuousConjunctiveProblemIsStillWelcome() =>
        Assert.Equal(1, Assert.IsType<Optimal>(OrToolsSolver.Create(OrToolsSolverId.Glop).Solve(Problem.Minimise(Gap).SubjectTo(Domain & (X <= 4)))).Solution.ObjectiveValue, precision: 6);

    [Fact]
    public void AnUnavailableSolverIsALoudError() =>
        Assert.Throws<NotSupportedException>(() => OrToolsSolver.Create("NO_SUCH_SOLVER").Solve(Problem.Satisfy(X >= 0)));
}
