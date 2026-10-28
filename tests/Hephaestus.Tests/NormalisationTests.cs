using System.Collections.Immutable;

namespace Hephaestus.Tests;

public sealed class NormalisationTests {
    private static readonly ContinuousVariable X = Variable.Continuous("x");
    private static readonly ContinuousVariable Y = Variable.Continuous("y");
    private static readonly IntegerVariable N = Variable.Integer("n");
    private static readonly BinaryVariable A = Variable.Binary("a");
    private static readonly BinaryVariable B = Variable.Binary("b");

    private static AffineForm Form(double constant, params (IVariable Variable, double Coefficient)[] terms) =>
        terms.Aggregate(AffineForm.Zero.Plus(constant), (form, term) => form.PlusTerm(term.Variable, term.Coefficient));

    [Fact]
    public void LinearExpressionsFlattenToTheirAffineFormAndTermsThatCancelDisappear() {
        Assert.Equal(Form(6, (X, 1)), (2 * (X + 3) - X).Normalise());
        Assert.Equal(Form(-1, (X, 0.5), (Y, -3)), ((X - 2) / 2 - 3 * Y).Normalise());
        Assert.Equal(Form(0, (X, 1)), X.Normalise());
        Assert.Equal(AffineForm.Zero, (X - X).Normalise());
        Assert.Equal(Form(0, (Y, 1)), (X + Y - X).Normalise());
        Assert.Equal(AffineForm.Zero, (0 * (X + 7)).Normalise());
    }

    [Fact]
    public void ExpressionsThatDenoteTheSameFunctionHaveEqualForms() {
        Assert.Equal((X + Y).Normalise(), (Y + X).Normalise());
        Assert.Equal((2 * (X + Y)).Normalise().GetHashCode(), (2 * Y + 2 * X).Normalise().GetHashCode());
        Assert.NotEqual((X + Y).Normalise(), (X - Y).Normalise());
    }

    [Fact]
    public void NonFiniteNumbersAreRejectedWhenNormalising() {
        Assert.Throws<ModellingException>(() => (X / 0).Normalise());
        Assert.Throws<ModellingException>(() => (X + double.NaN).Normalise());
    }

    [Fact]
    public void RangesFollowFromIntervalArithmetic() {
        var bounds = ImmutableDictionary<IVariable, Interval>.Empty.Add(X, new Interval(0, 10)).Add(Y, new Interval(-2, 3));

        Assert.Equal(new Interval(5 - 9, 5 + 20 + 6), (2 * X - 3 * Y + 5).Normalise().Range(variable => bounds[variable]));
        Assert.Equal(new Interval(double.NegativeInfinity, double.PositiveInfinity), X.Normalise().Range(_ => Interval.Unbounded));
    }

    [Fact]
    public void IntegralityIsRecognised() {
        Assert.True((2 * N + A.Indicator - 3).Normalise().IsIntegral);
        Assert.False((0.5 * N).Normalise().IsIntegral);
        Assert.False((N + X).Normalise().IsIntegral);
        Assert.False((N + 0.5).Normalise().IsIntegral);
    }

    [Fact]
    public void NegationsArePushedToTheLeavesAndNestedJunctionsOfTheSameKindAreFlattened() {
        var atom = new AffineRelation(Form(-1, (X, 1)), Relation.LessThanOrEqual);
        var flipped = new AffineRelation(Form(-1, (X, 1)), Relation.GreaterThan);
        var c = Variable.Binary("c");

        Assert.Equal(new All([new Literal(A, true), atom]), (A & (X <= 1)).Normalise());
        Assert.Equal(new Any([new Literal(A, false), flipped]), (!(A & (X <= 1))).Normalise());
        Assert.Equal(new Literal(A, true), (!!A).Normalise());
        Assert.Equal(new Any([new Literal(A, true), new Literal(B, true), new Literal(c, true)]), (A | (B | c)).Normalise());
        Assert.Equal(new Any([new Literal(A, false), new Literal(B, false), new Literal(c, true)]), (!(A & B) | c).Normalise());
        Assert.Equal(new Any([new Literal(A, false), new Literal(B, true)]), A.Implies(B).Normalise());
    }

    [Fact]
    public void ConstantsAreAbsorbed() {
        Assert.Equal(BooleanNormalisation.True, (A | BooleanConstant.True).Normalise());
        Assert.Equal(new Literal(A, true), (A & BooleanConstant.True).Normalise());
        Assert.Equal(BooleanNormalisation.False, (A & (new Constant(2) <= 1)).Normalise());
        Assert.Equal(new Literal(A, true), (A | (X - X > 0)).Normalise());
    }

    [Fact]
    public void StrictnessIsKeptExactlyAndEveryRelationFacesTheWayItWasWritten() {
        Assert.Equal(new AffineRelation(Form(-3, (N, 1)), Relation.LessThan), (N < 3).Normalise());
        Assert.Equal(new AffineRelation(Form(-3, (X, 1)), Relation.LessThan), (X < 3).Normalise());
        Assert.Equal(new AffineRelation(Form(-3, (N, 1)), Relation.GreaterThan), (!(N <= 3)).Normalise());
        Assert.Equal(new AffineRelation(Form(3, (X, -1)), Relation.LessThanOrEqual), (3 - X <= 0).Normalise());
    }

    [Fact]
    public void TheEncodingKeepsStrictnessWithAGapOfOneOverWholeNumbersAndEpsilonOverReals() {
        var m = Variable.Integer("m");

        Assert.Equal(["-m + n <= 2"], Problem.Satisfy(N - m < 3).EncodeLogic().Rows.Select(row => row.Format()));
        Assert.Equal(["x - y <= 2.9999"], Problem.Satisfy(X - Y < 3).EncodeLogic().Rows.Select(row => row.Format()));
    }

    [Fact]
    public void AnEquationUnderNegationIsADisequationAndMeansTheSameEitherWayRound() {
        Assert.Equal(new AffineRelation(Form(-3, (N, 1)), Relation.NotEqual), N.NotEqualTo(3).Normalise());
        Assert.Equal(new AffineRelation(Form(-3, (N, 1)), Relation.NotEqual), (!N.EqualTo(3)).Normalise());
        Assert.Equal(NormalForms.Canonical(X.EqualTo(Y).Normalise()), NormalForms.Canonical(Y.EqualTo(X).Normalise()));
        Assert.Equal(NormalForms.Canonical((X >= Y).Normalise()), NormalForms.Canonical((Y <= X).Normalise()));
    }

    // Each case checks both ways round, so between them they cover all six relations.
    [Theory]
    [InlineData(Relation.LessThan, Relation.GreaterThanOrEqual)]
    [InlineData(Relation.LessThanOrEqual, Relation.GreaterThan)]
    [InlineData(Relation.Equal, Relation.NotEqual)]
    public void EveryRelationHasAnOpposite(Relation relation, Relation opposite) {
        Assert.Equal(opposite, BooleanNormalisation.Opposite(relation));
        Assert.Equal(relation, BooleanNormalisation.Opposite(opposite));
    }

    [Fact]
    public void AnUnknownRelationHasNoOpposite() =>
        Assert.Throws<NotSupportedException>(() => BooleanNormalisation.Opposite((Relation)99));

    [Fact]
    public void NormalisingOrWritingOutAVeryLongChainOfConjunctionsDoesNotOverflowTheStack() {
        var chain = Enumerable.Range(0, 100_000).Aggregate<int, IBooleanExpression<ILinearArithmetic>>(BooleanConstant.True, (all, index) => all & (Variable.Continuous($"v{index}") <= index));

        Assert.Equal(100_000, Assert.IsType<All>(chain.Normalise()).Operands.Count);
        Assert.EndsWith("(v99999 <= 99999)", chain.Format(), StringComparison.Ordinal);
    }

    [Fact]
    public void NorDoesNormalisingOrReadingOffASolutionAVeryLongChainOfSums() {
        var chain = Enumerable.Range(1, 100_000).Aggregate<int, ILinearExpression>(new Constant(0), (sum, index) => sum + index * (ILinearExpression)X);

        Assert.Equal(100_000L * 100_001 / 2, chain.Normalise().Coefficients[X]);
        Assert.Equal(100_000L * 100_001 / 2, Solution.Empty.With(X, 1).Value(chain));
    }
}
