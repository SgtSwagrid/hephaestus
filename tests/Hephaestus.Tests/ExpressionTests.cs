namespace Hephaestus.Tests;

/// <summary>Operators build plain data, exactly as written; nothing is simplified at construction.</summary>
public sealed class ExpressionTests {
    private static readonly ContinuousVariable X = Variable.Continuous("x");
    private static readonly ContinuousVariable Y = Variable.Continuous("y");
    private static readonly BinaryVariable A = Variable.Binary("a");
    private static readonly BinaryVariable B = Variable.Binary("b");

    [Fact]
    public void ArithmeticOperatorsBuildTheTreeThatWasWrittenWithNothingSimplified() {
        Assert.Equal(new Sum(X, Y), X + Y);
        Assert.Equal(new Sum(X, new Constant(5)), X + 5);
        Assert.Equal(new Sum(new Constant(5), X), 5 + X);
        Assert.Equal(new Sum(X, new Product(-1, Y)), X - Y);
        Assert.Equal(new Sum(X, new Constant(-5)), X - 5);
        Assert.Equal(new Product(-1, X), -X);
        Assert.Equal(new Product(2, X), 2 * X);
        Assert.Equal(new Product(2, X), X * 2);
        Assert.Equal(new Product(0.25, X), X / 4);
        Assert.Equal(new Sum(new Sum(X, new Constant(0)), new Product(-1, X)), X + 0 - X);
    }

    [Fact]
    public void ComparisonOperatorsBuildLinearRelations() {
        Assert.Equal(new LinearRelation(X, Relation.LessThanOrEqual, Y), X <= Y);
        Assert.Equal(new LinearRelation(X, Relation.GreaterThanOrEqual, new Constant(3)), X >= 3);
        Assert.Equal(new LinearRelation(new Constant(3), Relation.LessThan, X), 3 < X);
        Assert.Equal(new LinearRelation(X, Relation.GreaterThan, Y), X > Y);
        Assert.Equal(new LinearRelation(X, Relation.Equal, Y), X.EqualTo(Y));
        Assert.Equal(new LinearRelation(X, Relation.NotEqual, new Constant(1)), X.NotEqualTo(1));
    }

    [Fact]
    public void LogicalOperatorsBuildTheTreeThatWasWrittenWithBinaryVariablesAndPlainTruthValuesOnEitherSide() {
        Assert.Equal(new Conjunction<ILogic>(A, B), A & B);
        Assert.Equal(new Disjunction<ILogic>(A, B), A | B);
        Assert.Equal(new Negation<ILogic>(A), !A);
        Assert.Equal(new Negation<ILogic>(new Negation<ILogic>(A)), !!A);
        Assert.Equal(new Negation<ILogic>(new Equivalence<ILogic>(A, B)), A ^ B);
        Assert.Equal(new Implication<ILinearArithmetic>(A, X <= 1), A.Implies(X <= 1));
        Assert.Equal(new Equivalence<ILinearArithmetic>(A, X <= 1), A.Iff(X <= 1));
        Assert.Equal(new Disjunction<ILinearArithmetic>(new Negation<ILogic>(new Conjunction<ILogic>(A, B)), X <= Y), !(A & B) | (X <= Y));
        Assert.Equal(new Conjunction<ILogic>(A, BooleanConstant.True), A & true);
        Assert.Equal(new Conjunction<ILogic>(BooleanConstant.False, A), false & A);
        Assert.Equal(new Disjunction<ILogic>(A, BooleanConstant.False), A | false);
        Assert.Equal(new Disjunction<ILinearArithmetic>(BooleanConstant.True, X <= 1), true | (X <= 1));
        Assert.Equal(new Negation<ILogic>(new Equivalence<ILogic>(A, BooleanConstant.True)), A ^ true);
        Assert.Equal(new Negation<ILogic>(new Equivalence<ILogic>(BooleanConstant.False, A)), false ^ A);
        Assert.Equal(new Implication<ILogic>(A, BooleanConstant.False), A.Implies(false));
        Assert.Equal(new Equivalence<ILogic>(A, BooleanConstant.True), A.Iff(true));
        Assert.Equal(new Implication<ILinearArithmetic>(BooleanConstant.True, X <= 1), true.Implies(X <= 1));
        Assert.Equal(new Equivalence<ILogic>(BooleanConstant.False, A), false.Iff(A));
    }

    [Fact]
    public void AnIndicatorIsWrittenInIversonBracketsReadAsOneOrZeroAndForABinaryVariableIsItsColumn() {
        var solution = Solution.Empty.With(X, 3).With(A, true);

        Assert.Equal(new LinearRelation(new Sum(new Indicator(A), new Indicator(B)), Relation.LessThanOrEqual, new Constant(1)), A.Indicator + B.Indicator <= 1);
        Assert.Equal("3*[a] + [x >= 2] - [!a]", (3 * A.Indicator + (X >= 2).Indicator - (!A).Indicator).Format());
        Assert.Equal(1, solution.Value(A.Indicator));
        Assert.Equal(0, solution.Value((!A).Indicator));
        Assert.Equal(1, solution.Value((X >= 2).Indicator));
        Assert.Equal(0, solution.Value((X >= 5).Indicator));
        Assert.Equal(AffineForm.Zero.PlusTerm(A, 2), (2 * A.Indicator).Normalise());
        Assert.Equal(AffineForm.Zero.PlusTerm(A, -2).Plus(2), (2 * (!A).Indicator).Normalise());
        Assert.Equal(AffineForm.Zero.Plus(1), BooleanConstant.True.Indicator.Normalise());
        Assert.Throws<ModellingException>(() => (X >= 2).Indicator.Normalise());
    }

    [Fact]
    public void ExpressionsAreValuesWithStructuralEquality() {
        Assert.Equal(X + 2 * Y <= 10, X + 2 * Y <= 10);
        Assert.NotEqual<ILinearExpression>(X + Y, Y + X);
        Assert.Equal((X + Y).Normalise(), (Y + X).Normalise());
        Assert.Equal(Variable.Continuous("x"), X);
        Assert.NotEqual<IVariable>(Variable.Integer("x"), X);
    }

    [Fact]
    public void SumsAndJunctionsOverCollectionsAreBalancedSoTheyStayShallowWithTheRightIdentities() {
        var terms = Enumerable.Range(0, 4).Select(index => Variable.Continuous($"t{index}")).ToList();
        var flags = Enumerable.Range(0, 4).Select(index => Variable.Binary($"f{index}")).ToList();

        Assert.Equal(new Sum(new Sum(terms[0], terms[1]), new Sum(terms[2], terms[3])), terms.Sum());
        Assert.Equal(new Sum(new Product(2, terms[0]), new Product(2, terms[1])), terms.Take(2).Sum(term => 2 * term));
        Assert.Equal(new Constant(0), Array.Empty<ILinearExpression>().Sum());
        Assert.Equal(new Conjunction<ILogic>(new Conjunction<ILogic>(flags[0], flags[1]), new Conjunction<ILogic>(flags[2], flags[3])), flags.AllOf());
        Assert.Equal(new Disjunction<ILogic>(new Disjunction<ILogic>(flags[0], flags[1]), new Disjunction<ILogic>(flags[2], flags[3])), flags.AnyOf());
        Assert.Equal(new Conjunction<ILogic>(!flags[0], !flags[1]), flags.Take(2).AllOf(flag => !flag));
        Assert.Equal(BooleanConstant.True, Array.Empty<IBooleanExpression<ILinearArithmetic>>().AllOf());
        Assert.Equal(BooleanConstant.False, Array.Empty<IBooleanExpression<ILinearArithmetic>>().AnyOf());
    }

    [Fact]
    public void BetweenIsAPairOfInequalitiesWhoseBoundsMayBeNumbersOrExpressions() {
        Assert.Equal(new Conjunction<ILinearArithmetic>(0 <= X, X <= 10), X.Between(0, 10));
        Assert.Equal(new Conjunction<ILinearArithmetic>(Y <= X, X <= 10), X.Between(Y, 10));
        Assert.Equal(new Conjunction<ILinearArithmetic>(0 <= X, X <= Y), X.Between(0, Y));
        Assert.Equal(new Conjunction<ILinearArithmetic>(Y <= X, X <= Y + 5), X.Between(Y, Y + 5));
    }
}
