namespace Hephaestus.Tests;

public sealed class FormattingTests {
    private static readonly ContinuousVariable X = Variable.Continuous("x");
    private static readonly ContinuousVariable Y = Variable.Continuous("y");
    private static readonly BinaryVariable A = Variable.Binary("a");
    private static readonly BinaryVariable B = Variable.Binary("b");

    [Fact]
    public void LinearExpressionsReadAsWritten() {
        Assert.Equal("x + 120", (X + 120).Format());
        Assert.Equal("x - y", (X - Y).Format());
        Assert.Equal("x - 2*y", (X - 2 * Y).Format());
        Assert.Equal("x - 5", (X - 5).Format());
        Assert.Equal("-x + y", (-X + Y).Format());
        Assert.Equal("2*(x + y)", (2 * (X + Y)).Format());
        Assert.Equal("x - (y + 1)", (X - (Y + 1)).Format());
        Assert.Equal("0.5*x", (X / 2).Format());
    }

    [Fact]
    public void BracketsAppearOnlyWhereTheyAreNeeded() {
        // The changeover constraint reads like its specification.
        Assert.Equal("!(a & b) | (x + 120 <= y) | (y + 120 <= x)", (!(A & B) | (X + 120 <= Y) | (Y + 120 <= X)).Format());
        Assert.Equal("x <= y", (X <= Y).Format());
        Assert.Equal("a & b | !a", (A & B | !A).Format());
        Assert.Equal("a & (b | !a)", (A & (B | !A)).Format());
        Assert.Equal("a => b => a", A.Implies(B.Implies(A)).Format());
        Assert.Equal("(a => b) => a", A.Implies(B).Implies(A).Format());
        Assert.Equal("a <=> (x != 1)", A.Iff(X.NotEqualTo(1)).Format());
        Assert.Equal("!(a <=> b)", (A ^ B).Format());
        Assert.Equal("true & !false", (BooleanConstant.True & !BooleanConstant.False).Format());
    }

    [Fact]
    public void AffineFormsListTheirTermsInVariableOrder() {
        Assert.Equal("x - 2*y + 7", (7 - 2 * Y + X).Normalise().Format());
        Assert.Equal("-x", (-X).Normalise().Format());
        Assert.Equal("0", (X - X).Normalise().Format());
        Assert.Equal("-3.5", new Constant(-3.5).Normalise().Format());
    }
}
