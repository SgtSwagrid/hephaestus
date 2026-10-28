using System.Collections.Immutable;

namespace Hephaestus.Tests;

/// <summary>Formulas typed by their theory, pure logic and the linear arithmetic that extends it, and the normal form that lowering gives.</summary>
public sealed class TheoryTests {
    private static readonly ContinuousVariable X = Variable.Continuous("x");
    private static readonly ContinuousVariable Y = Variable.Continuous("y");
    private static readonly BinaryVariable A = Variable.Binary("a");
    private static readonly BinaryVariable B = Variable.Binary("b");

    [Fact]
    public void PureLogicIsAFormulaOfEveryTheoryAndTheSameFormulaInWhicheverItIsSeen() {
        IBooleanExpression<ILogic> logic = !(A & B) | A.Implies(B);
        IBooleanExpression<ILinearArithmetic> mixed = logic & (X <= Y);
        IBooleanExpression<ILinearArithmetic> wide = new Conjunction<ILinearArithmetic>(A, B);
        IBooleanExpression<ILogic> narrow = A & B;

        Assert.Equal("(!(a & b) | (a => b)) & (x <= y)", mixed.Format());
        Assert.IsNotAssignableFrom<IBooleanExpression<ILogic>>(X <= Y);
        Assert.Equal(wide, narrow);
        Assert.Equal(narrow, wide);
        Assert.Equal(wide.GetHashCode(), narrow.GetHashCode());
        Assert.NotEqual(wide, A | B);
        Assert.Equal(Problem.Minimise(X).SubjectTo(A & B), Problem.Minimise(X).SubjectTo(A, B));
    }

    [Fact]
    public void LoweringGivesEachConstraintInNormalForm() {
        var linearised = Problem.Minimise(Piecewise.Max(X, Y)).SubjectTo(A.Implies(X - Y <= 3), Piecewise.Abs(X) <= 5).Linearise();

        Assert.Equal(new Any([new Literal(A, false), new AffineRelation(AffineForm.Zero.PlusTerm(X, 1).PlusTerm(Y, -1).Plus(-3), Relation.LessThanOrEqual)]), linearised.Constraints[0].Lowered);
        Assert.Equal("!a | (x - y <= 3)", linearised.Constraints[0].Lowered.Format());
        Assert.Equal("_max0 <= 5", linearised.Constraints[1].Lowered.Format());
        Assert.NotEmpty(linearised.Definitions);
        // A normal form keeps strictness and disequation exact.
        Assert.Equal(["x - y < 0", "x != 3"], Problem.Satisfy((X < Y) & X.NotEqualTo(3)).Linearise().Constraints.Select(constraint => constraint.Lowered.Format()));
    }

    [Fact]
    public void AConflictIsMadeOfTheConjunctsAsWritten() {
        IBooleanExpression<ILinearArithmetic> constraint = (X >= 5).WithName("high") & (X <= 3).WithName("low") & Y.Between(0, 1);

        ImmutableArray<IBooleanExpression<ILinearArithmetic>> conflict = new Enumerating().FindConflict(constraint);

        Assert.Equal(["high", "low"], conflict.Select(conjunct => conjunct.Name));
    }

    /// <summary>Decides feasibility by evaluating a few candidate points: enough for the conflict above, and no solver needed.</summary>
    private sealed record Enumerating : ISolver {
        public ISolveResult Solve(IOneShotProblem problem, Solution? startingFrom = null, CancellationToken cancellationToken = default) =>
            new[] { 0.0, 4.0, 6.0 }
                .Select(value => Solution.Empty.With(X, value).With(Y, 0))
                .FirstOrDefault(solution => solution.Value(problem.Constraint)) is { } found
                ? new Optimal(found)
                : new Infeasible();
    }
}
