using System.Collections.Immutable;

namespace Hephaestus.Tests;

/// <summary>
/// The property the whole library stands on: an assignment of the modeller's variables satisfies a
/// formula exactly when it can be extended, by some choice of auxiliary binaries, to satisfy the
/// encoded rows. Checked exhaustively over a small grid, against an evaluator that shares no code
/// with the encoder.
/// </summary>
public sealed class EncodingEquivalenceTests {
    private static readonly BinaryVariable A = Variable.Binary("a");
    private static readonly BinaryVariable B = Variable.Binary("b");
    private static readonly IntegerVariable M = Variable.Integer("m");
    private static readonly IntegerVariable N = Variable.Integer("n");
    private static readonly ContinuousVariable X = Variable.Continuous("x");

    private static readonly IBooleanExpression<ILinearArithmetic> Domain = M.Between(0, 3) & N.Between(0, 3) & X.Between(0, 3);

    /// <summary>Whole numbers beyond the stated domain are included so that the bounds themselves are put to the test.</summary>
    private static readonly ImmutableArray<Solution> Grid = [
        .. from a in new[] { 0.0, 1.0 }
           from b in new[] { 0.0, 1.0 }
           from m in new[] { -1.0, 0.0, 1.0, 2.0, 3.0, 4.0 }
           from n in new[] { 0.0, 1.0, 2.0, 3.0 }
           from x in new[] { 0.0, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5 }
           select Assignment((A, a), (B, b), (M, m), (N, n), (X, x)),
    ];

    public static TheoryData<string> HandWrittenCases => [.. HandWritten.Keys];

    private static readonly ImmutableDictionary<string, IBooleanExpression<ILinearArithmetic>> HandWritten = new Dictionary<string, IBooleanExpression<ILinearArithmetic>> {
        ["guarded disjunction"] = !(A & B) | (X + 1 <= M) | (M + 1 <= X),
        ["implications, of an inequality and of an equality"] = A.Implies(X >= 2) & B.Implies((M + N).EqualTo(3)),
        ["equivalence reifies in both directions"] = A.Iff(M + N >= 4),
        ["exclusive or"] = A ^ (X <= 1),
        ["strict over reals"] = (X < 2) | B,
        // Split into two strict inequalities, each with a gap of one.
        ["disequality"] = M.NotEqualTo(N),
        ["negated equality under a guard"] = A | !M.EqualTo(2),
        ["conjunction under a disjunction"] = ((M >= 2) & (N >= 2)) | ((M <= 0) & (X <= 1)),
        ["nested alternation"] = A | ((M >= 1) & (B | ((N >= 1) & ((X >= 2) | (X <= 1))))),
        ["negated compound"] = !((M >= 2) & (A | (X <= 1))),
        ["shared subformula"] = (A | (M + N <= 3)) & (B | (M + N <= 3)),
        ["tautology, and contradiction under a disjunction"] = (A | !A) & ((A & !A) | (M >= 3)),
        ["unsatisfiable"] = (M >= 2) & (M <= 1),
        ["always-true and never-true comparisons in disjunctions"] = (B | (X <= 5)) & (A | (X >= 5)),
        ["binaries as numbers"] = (A.Indicator + B.Indicator).EqualTo(1) | (M >= 3),
        ["iff of iffs"] = A.Iff(B.Iff(M >= 2)),
    }.ToImmutableDictionary();

    [Theory]
    [MemberData(nameof(HandWrittenCases))]
    public void TheEncodingAcceptsExactlyWhatTheFormulaDoes(string name) {
        var verdict = Check(Domain & HandWritten[name]);

        Assert.True(verdict.WasChecked, "The formula needs too many auxiliaries to enumerate.");
        Assert.Null(verdict.Disagreement);
    }

    [Fact]
    public void TheEncodingAcceptsExactlyWhatARandomFormulaDoes() {
        var verdicts = Enumerable.Range(1, 4)
            .Select(seed => new Random(seed))
            .SelectMany(random => Enumerable.Range(0, 40).Select(_ => Check(Domain & RandomFormula(random, depth: 2))))
            .ToList();

        Assert.All(verdicts, verdict => Assert.Null(verdict.Disagreement));
        Assert.True(verdicts.Count(verdict => verdict.WasChecked) >= verdicts.Count / 2, "Too few random formulas were small enough to check.");
    }

    /// <summary>Whether the formula was small enough to check, and if so where it and its encoding part ways (nowhere, one hopes).</summary>
    private sealed record Verdict(
        bool WasChecked,
        string? Disagreement
    );

    /// <summary>
    /// Every form a backend may be handed is checked: the big-M programme (turned back into an
    /// unguarded indicator problem), the indicator problem as encoded, with single guards (as Gurobi
    /// takes it), and with propagated bounds (as CP-SAT takes it).
    /// </summary>
    private static Verdict Check(IBooleanExpression<ILinearArithmetic> formula) {
        var logic = Problem.Satisfy(formula).EncodeLogic();
        var forms = new[] { logic.RelaxGuards().AsIndicatorProblem(), logic, logic.WithSingleGuards(), logic.WithPropagatedBounds() };
        return forms.All(form => form.Columns.Count(column => column.IsAuxiliary) <= 10)
            ? new Verdict(true, forms.Select(form => Disagreement(formula, form)).FirstOrDefault(disagreement => disagreement is not null))
            : new Verdict(false, null);
    }

    private static string? Disagreement(IBooleanExpression<ILinearArithmetic> formula, IndicatorProblem encoded) {
        var auxiliaries = encoded.Columns.Where(column => column.IsAuxiliary).ToImmutableArray();
        var masks = Enumerable.Range(0, 1 << auxiliaries.Length)
            .Where(mask => auxiliaries.Select((column, index) => Within((mask >> index) & 1, column.LowerBound, column.UpperBound)).All(within => within))
            .ToImmutableArray();
        var disagreements = Grid
            .Where(assignment => assignment.Value(formula, tolerance: 0) != IsExtendable(encoded, auxiliaries, masks, assignment))
            .Select(assignment => string.Join(", ", assignment.Values.Select(entry => $"{entry.Key.Name}={entry.Value}")))
            .Take(5)
            .ToList();
        return disagreements.Count == 0
            ? null
            : $"'{formula.Format()}' and its encoding disagree at: {string.Join(" | ", disagreements)}{Environment.NewLine}{encoded.Format()}";
    }

    private static bool IsExtendable(IndicatorProblem encoded, ImmutableArray<Column> auxiliaries, ImmutableArray<int> masks, Solution assignment) =>
        encoded.Columns.Where(column => !column.IsAuxiliary).All(column => Within(assignment.Values[column.Variable], column.LowerBound, column.UpperBound))
        && masks.Any(mask => Satisfies(encoded, Extended(assignment, auxiliaries, mask)));

    private static ImmutableSortedDictionary<IVariable, double> Extended(Solution assignment, ImmutableArray<Column> auxiliaries, int mask) =>
        assignment.Values.SetItems(auxiliaries.Select((column, index) => KeyValuePair.Create(column.Variable, (double)((mask >> index) & 1))));

    /// <summary>A guarded row is read as the implication it stands for, not through any relaxation.</summary>
    private static bool Satisfies(IndicatorProblem encoded, ImmutableSortedDictionary<IVariable, double> values) =>
        encoded.Rows.All(row =>
            row.Guards.Any(guard => values[guard.Variable] > 0.5 != guard.IsPositive)
            || (row.Expression.Evaluate(variable => values[variable]) is var value && (row.IsEquality ? Math.Abs(value) <= 1e-9 : value <= 1e-9)));

    private static bool Within(double value, double lower, double upper) => lower - 1e-9 <= value && value <= upper + 1e-9;

    private static Solution Assignment(params (IVariable Variable, double Value)[] values) =>
        new(values.ToImmutableSortedDictionary(entry => entry.Variable, entry => entry.Value, VariableOrder.Comparer), 0);

    private static IBooleanExpression<ILinearArithmetic> RandomFormula(Random random, int depth) =>
        depth == 0 || random.Next(4) == 0
            ? RandomLeaf(random)
            : random.Next(6) switch {
                0 => !RandomFormula(random, depth - 1),
                1 => RandomFormula(random, depth - 1) & RandomFormula(random, depth - 1),
                2 => RandomFormula(random, depth - 1) | RandomFormula(random, depth - 1),
                3 => RandomFormula(random, depth - 1).Implies(RandomFormula(random, depth - 1)),
                4 => RandomFormula(random, depth - 1).Iff(RandomFormula(random, depth - 1)),
                _ => RandomFormula(random, depth - 1) ^ RandomFormula(random, depth - 1),
            };

    private static IBooleanExpression<ILinearArithmetic> RandomLeaf(Random random) =>
        random.Next(5) switch {
            0 => A,
            1 => B,
            _ => new LinearRelation(RandomLinear(random), (Relation)random.Next(6), RandomLinear(random)),
        };

    // Coefficients and constants are multiples of a half, as is the grid, so comparisons never come within epsilon of a tie.
    private static ILinearExpression RandomLinear(Random random) =>
        new ILinearExpression[] { A.Indicator, M, N, X }
            .Where(_ => random.Next(2) == 0)
            .Select(term => random.Next(-2, 3) * term)
            .Append(new Constant(random.Next(-4, 5) / 2.0))
            .Sum();
}
