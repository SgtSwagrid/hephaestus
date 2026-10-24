<div align="center">

  <h1>⚒️ Hephaestus</h1>
  <p>A purely functional modelling layer for MILP and SMT solvers in C#.</p>

  <span>
    <a href="https://github.com/SgtSwagrid/hephaestus/actions/workflows/build-integrity.yml"><img src="https://github.com/SgtSwagrid/hephaestus/actions/workflows/build-integrity.yml/badge.svg" alt="Build status" /></a>
    <a href="https://www.nuget.org/packages/Hephaestus.Optimisation"><img src="https://img.shields.io/nuget/vpre/Hephaestus.Optimisation.svg" alt="NuGet" /></a>
    <a href="https://alecdorrington.com/hephaestus"><img src="https://img.shields.io/badge/docs-latest-blue.svg" alt="Documentation" /></a>
  </span>

</div>

## 💡 Overview

<br/>

> "Nothing at all takes place in the universe in which some rule of maximum or minimum does not appear." — Leonhard Euler.

<br/>

Optimisation models are specified as mathematics. Hephaestus lets the code *be* that mathematics, so that reviewing a constraint is a direct comparison with its specification:

```
use_A ∧ use_B  ⟹  (start_A + c ≤ start_B) ∨ (start_B + c ≤ start_A)
```

```csharp
var isSeparated    = (startA + changeover <= startB) | (startB + changeover <= startA);
var isConflictFree = !(usesA & usesB) | isSeparated;
```

There are no auxiliary booleans to declare, no gadget factories, and no big-M to work out by hand: the boolean structure is lowered to linear constraints automatically, and every big-M is derived from the bounds your constraints already state. The solver underneath is swappable.

## ⬇️ Installation

A model needs the core and one backend. Everything else is optional.

### The core

| Package | What it is |
| --- | --- |
| `Hephaestus.Optimisation` | Expressions, problems, the MILP encoding, the solver interfaces. No native dependencies. |

### Solver backends

Each puts a solver behind the same `ISolver`, so which one is underneath is a single line of your program.

| Package | What it is |
| --- | --- |
| `Hephaestus.Optimisation.Gurobi` | Native Gurobi backend. Conditional constraints become Gurobi indicator constraints, so no big-M is involved; the classic big-M formulation is available too. Needs a Gurobi licence. |
| `Hephaestus.Optimisation.Highs` | Standalone HiGHS backend: the leading permissively licensed MILP solver, in a few megabytes. |
| `Hephaestus.Optimisation.OrTools` | Google OR-Tools: CP-SAT through its own interface (whole-number problems, no big-M, often the fastest choice for either-or scheduling), plus SCIP, CBC and HiGHS as MILP solvers. Pure LP solvers (GLOP, CLP, PDLP) are accepted only for problems that need no whole-number variables. |
| `Hephaestus.Optimisation.Z3` | SMT backend on Microsoft Z3: native boolean structure, exact arithmetic, no encoding at all. |

### Integrations

Typed variables and expressions for a domain's own types, so that a duration is a `Duration` and not a number of seconds. The core already covers `TimeSpan`, `DateTime` and `DateTimeOffset`.

| Package | What it is |
| --- | --- |
| `Hephaestus.Optimisation.NodaTime` | `Duration`, `Instant`, `LocalDateTime`, `LocalDate`, `LocalTime`, `OffsetDateTime` and `ZonedDateTime`. |

```bash
dotnet add package Hephaestus.Optimisation --prerelease
dotnet add package Hephaestus.Optimisation.OrTools --prerelease
```

Hephaestus is in beta, so `--prerelease` is needed for now, and the API may change between versions.

(The bare name `Hephaestus` was already taken on nuget.org, hence `Hephaestus.Optimisation`. The namespace is simply `Hephaestus`.) Operators are C# 14 extension members, so consumers need the .NET 10 SDK.

## 📖 A complete example

```csharp
using Hephaestus;
using Hephaestus.OrTools;

var startA = Variable.Continuous("startA");
var startB = Variable.Continuous("startB");
var usesA  = Variable.Binary("usesA");
var usesB  = Variable.Binary("usesB");
const double changeover = 120;

var withinTheShift = startA.Between(0, 3600) & startB.Between(0, 3600);
var isSeparated    = (startA + changeover <= startB) | (startB + changeover <= startA);
var isConflictFree = !(usesA & usesB) | isSeparated;

var problem = Problem.Minimise(startA + startB)
    .SubjectTo(withinTheShift)
    .SubjectTo(isConflictFree & usesA & usesB);

var summary = OrToolsSolver.Create().Solve(problem) switch {
    Optimal(var solution)  => $"A starts at {solution.Value(startA)}, B at {solution.Value(startB)}",
    Feasible(var solution) => $"best found: {solution.ObjectiveValue}",
    Infeasible             => "no schedule exists",
    Unbounded              => "unbounded",
    Unknown(var reason)    => $"the solver gave up: {reason}",
    _                      => throw new NotSupportedException(),   // ISolveResult is an interface, so C# asks
};
```

Swap `OrToolsSolver.Create()` for `OrToolsSolver.Create(OrToolsSolverId.Highs)` or `new Z3Solver()` and nothing else changes. `samples/MachineScheduling` is a slightly larger version in NodaTime types.

## 🧠 The ideas

### Expressions are data; operators only build it

There are two algebraic data types, each an interface with a handful of sealed records:

- `ILinearExpression`: `Constant`, `Sum`, `Product`, `Indicator`, and the continuous and integer variables.
- `IBooleanExpression<TTheory>`: `BooleanConstant`, `BinaryVariable`, the theory's atoms (`LinearRelation`), and the connectives `Negation`, `Conjunction`, `Disjunction`, `Implication` and `Equivalence`.

A formula is generic in its theory, as in SMT, where the logic stays the same while the theory beneath it changes. Every theory extends `ILogic`, whose formulas are made of truths and binary variables alone; `ILinearArithmetic` adds relations between linear expressions, so `x <= y` is a `LinearRelation` and a formula of linear arithmetic is an `IBooleanExpression<ILinearArithmetic>`, which is what a problem's constraint is. The type is contravariant: a formula of a theory is a formula of every theory that extends it, so pure logic mixes with anything, and `flag & (x <= 4)` is a formula of linear arithmetic. Formulas compare equal whichever theory they are seen in.

Keeping them apart makes illegal compositions unrepresentable: `x * y`, `(x <= 1) + 1` and `if (x <= y)` do not compile. A `BinaryVariable` is a truth, combined with `&`, `|` and `!`; where a number is wanted, its `Indicator` is one when it is true and zero when not, so `usesA & usesB` and `usesA.Indicator + usesB.Indicator <= 1` are both fine. Any constraint has an indicator, not only a variable: `jobs.Sum(job => (job.Finish > deadline).Indicator)` counts the late ones.

Operators (`+ - * /`, `<= >= < >`, `& | ! ^`, plus `EqualTo`, `NotEqualTo`, `Between`, `Implies`, `Iff`) are extension members that do nothing but construct records: `a + b` *is* `new Sum(a, b)`. Plain values mix in on either side: `x + 5`, `5 + x`, `0 <= x`, and, for constraints that depend on known data, `job.IsUrgent.Implies(start <= cutoff)` or `isRush & (changeover >= 180)`. Nothing is flattened or simplified at construction time. All interpretation happens later, in separate passes over the data:

| Pass | Function |
| --- | --- |
| Normalise a linear expression to its canonical affine form | `expression.Normalise()` |
| Render as written, for logs and review | `expression.Format()` |
| Collect variables | `expression.Variables` |
| Lower a problem to a plain MILP | `problem.Encode()` |
| Read an expression off a solution | `solution.Value(expression)` |

Because expressions are immutable values with structural equality, a constraint can be composed from smaller named pieces, logged, compared and unit-tested without a solver or a "problem" object in sight. (`==` is reserved by C# for that structural equality, which is why the equality *constraint* is spelt `EqualTo`.)

### A problem is one constraint

```csharp
Problem.Satisfy(constraint)
Problem.Minimise(objective).SubjectTo(constraint)
Problem.Maximise(objective).SubjectTo(constraint).SubjectTo(another, andAnother)
```

A problem is a value, and every step of building one gives another: `SubjectTo` conjoins a further constraint onto the one the problem has, so `.SubjectTo(a).SubjectTo(b)`, `.SubjectTo(a, b)` and `.SubjectTo(a & b)` are the same problem, and a whole collection can be passed at once. A problem can be handed around and constrained further by whoever receives it, and the original is untouched.

Conjoin a collection with `constraints.AllOf()` (and `AnyOf()`, and `terms.Sum()`); these build balanced trees, so a hundred thousand constraints are no deeper than seventeen levels. "Rollback" is keeping the old value.

The constraints of a model are still there to be had: `constraint.Conjuncts` takes the tree of `&` apart again, and those are the units in which an infeasibility is explained (below).

### Names, and explaining infeasibility

```csharp
var isSeparated    = ((startA + changeover <= startB) | (startB + changeover <= startA)).WithName("separated");
var isConflictFree = (!(usesA & usesB) | isSeparated).WithName("changeover A/B");

isConflictFree.Format()                     // "changeover A/B"
((NamedConstraint)isConflictFree).Expression.Format()    // "!(usesA & usesB) | separated"
```

Any expression, boolean, linear or typed, can be given a name with `WithName`. A name changes nothing about what an expression means; it is what the expression is called wherever it is written out, including inside a larger one. An expression without one goes by the way it is written, so every constraint has a usable `Name` from the start.

```csharp
if (solver.Solve(problem) is Infeasible) {
    var conflict = solver.FindConflict(problem);        // "changeover A/B", "A starts early", "B starts early", "0 <= startA", ...
}
```

`FindConflict` returns conjuncts that cannot all hold and none of which can be spared: drop any one and the rest can. It asks nothing of a solver but to tell feasible from infeasible, so it works with every backend, and takes about `k · log(n / k)` solves to find `k` constraints among `n`. Bounds take part like any other constraint, since that is all they are. Gurobi is asked to narrow the search down first, with an irreducible infeasible subsystem of its own, so on a large model the search is over a handful of constraints rather than all of them; the answer is the same either way.

### Shadow prices

```csharp
var machineFree = (start >= release).WithName("machine free");
var problem      = Problem.Minimise(totalDelay).SubjectTo(machineFree & ...);

var solution = solver.Solve(problem).SolutionOrNull!;
var prices   = problem.ShadowPrices(solution, new GurobiBackend());

prices.Of(machineFree)      // seconds of total delay per second by which `release` is raised
prices.ByName                // every constraint, by name
```

The price of a constraint `lhs <= rhs` (or `>=`, or `==`) is the change in the optimal objective for each unit by which its right-hand side, as written, is raised; a constraint that is not binding has a price of zero. Prices belong to linear programmes, and a problem with logic or whole numbers is not one, but at a solution it comes down to one: every disjunction has a side that holds, and every whole-number variable has its value. Keep those, and what is left is the linear programme of the continuous variables; its dual values are the prices. They say what each constraint costs given the discrete choices that were made, not what it would cost if those could be made again. The backend that supplies the dual values need not be the solver that found the solution: Gurobi, HiGHS and OR-Tools' GLOP (`new OrToolsBackend(OrToolsSolverId.Glop)`) report them, and a solution found by Z3 or CP-SAT can be priced by any of them.

### Variables have no bounds; bounds are constraints

A variable is a name and a kind (`Continuous`, `Integer`, `Binary`). `0 <= x & x <= 3600` is a constraint like any other. The encoder recognises unconditional single-variable constraints and hands them to the solver as native column bounds, so this costs nothing.

### Big-M is derived, never supplied

The MILP encoding runs in four pure steps:

1. **Lower and normalise** (`problem.Linearise()`): piecewise-linear functions become variables, and each constraint becomes negation normal form over exact affine relations, `affine form ~ 0`, facing the way it was written. This is what every backend is handed, Z3 included; the MILP steps below take it from here, turning each relation into a row that faces zero from below.
2. **Encode the logic** as *guarded rows*, "if these literals all hold then `e <= 0`", introducing as few auxiliary binaries as possible. In a disjunction the existing literals become guards, and only the compound disjuncts *other than the last* need an auxiliary. `(a + h <= b) | (b + h <= a)` comes out as the textbook either-or with one binary; `flag.Iff(x >= 5)` needs none. Equal subformulas share one auxiliary.
3. **Propagate bounds** (feasibility-based bound tightening) over the unconditional rows. `x <= 3600` bounds `x` directly; `finish == start + runtime` bounds `finish` once `start` and `runtime` are bounded; and so on down the line until nothing changes.
4. **Relax each guard** with `e <= Σ Mᵢ · (1 if guard i is off, else 0)`. The row only has to give way once some guard is off, so `Mᵢ` need only be the largest value `e` can take *while guard i is off* — the tightest valid choice, derived separately for every guard of every row. A guard the row holds without costs nothing at all. Rows that can never bind are dropped; rows that can never hold forbid their guards outright.

If some `M` is infinite, encoding fails with an error naming the variables that lack bounds. That is deliberate: a guessed big-M that is too small silently cuts off solutions, and the whole point is that wrong constraints should not fail silently. If you really want a guess, opt in with `new EncodingOptions(FallbackBigM: 1e6)`.

The opposite trouble is a big-M that is derived perfectly correctly and is simply enormous: at a hundred million it swamps the solver's feasibility tolerance, and the answers stop meaning much. Nothing says so, because the bounds it came from looked reasonable. `new EncodingOptions(MaximumBigM: 1e6)` turns that into an error naming the row and the variables whose bounds are that wide.

The first two steps are `problem.EncodeLogic()`, giving an `IndicatorProblem`; the last two are `.RelaxGuards()`, giving a `MilpProblem`; `problem.Encode()` is both. Either result can be printed with `.Format()`. Solvers with indicator or half-reified constraints (Gurobi, CP-SAT) stop after the first half and never see a big-M, and the Z3 backend skips all of it, because an SMT solver takes the boolean structure as it stands.

### Strictness

Over whole-valued expressions, `n < 5` is exactly `n <= 4`. Over the reals a MILP cannot express strictness, so `x < 5` becomes `x + ε <= 5` with `EncodingOptions.StrictnessEpsilon` (default `1e-4`, comfortably above solver tolerances). Negation is where this usually bites: `!(x <= 5)` is `x > 5`. Z3 has true strict inequalities and no epsilon.

### Reified truth values

To use the truth of a constraint as a number (say, to count violated soft constraints), tie it to a binary variable yourself: `isLate.Iff(finish >= deadline)`, then use `isLate` in the objective. `Iff` binds in both directions.

### Min, max, absolute value and conditionals

```csharp
using static Hephaestus.Piecewise;

var isOnTime   = Abs(finish - promised) <= tolerance;
var makespan   = Max(finishes);
var problem    = Problem.Minimise(makespan + 10 * Abs(finish - promised)).SubjectTo(...);
```

`Max`, `Min` and `Abs` are records like everything else, and work on plain and typed expressions alike. When a problem is encoded, each becomes an auxiliary variable tied to its operands (all three are maxima: `min(a, b) = -max(-a, -b)` and `|e| = max(e, -e)`), and equal ones share a variable. How it is tied depends on how the problem leans on it. Minimising a maximum, or bounding an absolute value from above, only tempts the solver to make the variable too small, so `m >= a & m >= b` is enough and no binary variable is spent; that is the usual linear-programming idiom, found for you. Only a use that rewards a larger value (`Abs(x - y) >= 5`, or maximising a maximum) adds `m <= a | m <= b`, which costs one binary. The variable is bounded by the bounds of its operands, so its big-M is derived like any other.

```csharp
var setupCost = needsSetup.Indicator * setupTime;              // the setup time if it is needed, else nothing
var penalty   = If(finish >= deadline, 50 + 2 * lateness, 0);  // one expression or another
```

`If(condition, then, otherwise)` is lowered by the same pass, and the product of an indicator and an expression is `If(condition, expression, 0)`: the one product of two expressions that stays linear, and the only one that compiles. An indicator on its own is `If(condition, 1, 0)`, except that the indicator of a binary variable is simply its column (and of its negation, one minus that). With a binary variable for a condition it costs two conditional rows and no further binary, which Gurobi and CP-SAT take as they stand and the others get as the textbook big-M rows, with M derived from the bounds of the expression. Like a maximum, it is only held from the side on which the problem could otherwise cheat.

### Typed expressions

A `Quantity<T>` is a linear expression read as an *amount* of type `T` (a duration); a `Point<T, TDelta>` is one read as a *position* (a date-time) whose differences are amounts of `TDelta`. Each pairs an ordinary `ILinearExpression` with an `IProjection<T>`, an affine map between `T` and the solver's number line ("seconds since 08:00"). Both are `ILinearlyEncodable<T>`, which is that pair and is where comparison, reading, naming and optimising are defined (comparison and reading once for everything typed, as `IEncodable<T>`, below); only the arithmetic differs between them, and that is all each type carries. A record of your own that implements it is treated alike.

The two types carry the right algebra, once, generically:

```csharp
Quantity<Duration>               elapsed  = finish - start;          // point - point
Point<LocalDateTime, Duration>   earliest = start + runtime + slack;     // point + quantity
IBooleanExpression<ILinearArithmetic> onTime = finish <= deadline;    // compare with plain values
Point<LocalDateTime, Duration>   release  = shiftStart + runtime;           // plain value + quantity
//                                          finish + start            // does not compile
//                                          2 * finish                // does not compile
//                                          finish <= runtime         // does not compile
```

Plain values of the right type are welcome wherever an expression is, on either side (`Duration.FromMinutes(2) + start`, `shiftStart <= start`, `runtime.Between(minimum, finish - start)`), and `delay.In(Duration.FromMinutes(1))` turns a quantity back into a plain linear expression ("delay in minutes") for a cost function.

Plain numbers can be typed too: `Variable.Integer<int>("jobs")` is a `Quantity<int>` and `Variable.Continuous<decimal>("cost")` a `Quantity<decimal>`, for any integer or floating-point type. A count then reads back as an `int`, already made whole (a solver's 2.9999999 is a three), and kinds of number do not mix: `jobs + 1.5` does not compile. Where they must mix, `jobs.Expression` is the ordinary expression underneath.

Solutions are read back in the same types: `solution.Value(finish)` is a `LocalDateTime`, and so is `solution.Value(start + runtime)`; any expression can be read, not just variables. It is one method whatever is asked of it — a number for a linear expression, a truth for a constraint, its own type for anything read through a projection. Expressions under different projections (minutes against seconds, different origins) are reconciled automatically.

The core ships projections for `TimeSpan`, `DateTime` and `DateTimeOffset`; `Hephaestus.Optimisation.NodaTime` adds `Duration`, `Instant`, `LocalDateTime`, `LocalDate` (in `Period`s of whole days), `LocalTime`, `OffsetDateTime` and `ZonedDateTime`:

```csharp
var start   = Variable.LocalDateTime("start", origin: shiftStart);
var runtime     = Variable.Duration("runtime", unit: Duration.FromSeconds(30), inWholeUnits: true);  // quantised
```

Supporting another type means writing one small record that implements `IProjection<T>` (or `IPointProjection<T, TDelta>`), plus, for a point type, the three one-line `T ± Quantity<TDelta>` operators that C# will not let the core declare generically (they delegate to `quantity.Beyond(origin, projection)`). A projection can also be had from one you have: `seconds.Biselect(TimeSpan.FromSeconds, span => (long)span.TotalSeconds)` re-views it as another type, and `Select` gives a decoder alone — a reading no constraint can mention. Projections built that way hold functions, so they do not compare equal; write a record where that matters. A two-state type is carried by an indicator, projected onto zero and one: a record `Lift(goesUp.Indicator, directions)` that implements `ILinearlyEncodable<Direction>` is compared, read and zipped like any other, and `constraint.AsEncodable()` is the same thing for a plain `bool`. Typed reads round the underlying number to `Evaluation.DecimalPlaces` of the unit, so that solver noise does not turn 08:04:00 into 08:03:59.99999999; read the underlying expression for the unrounded number. A constraint is read forgiving violations up to `Evaluation.Tolerance`, or whatever you pass instead.

### Zips, sequences and vectors

Typed expressions combine. `a.Zip(b)` puts any two side by side as one value of a pair, whatever each is made of: a duration and a date-time, or a number and a truth.

```csharp
var job      = start.Zip(isRunning.AsEncodable());     // IEncodable<(LocalDateTime, bool)>
var pinned   = job.EqualTo((shiftStart, true));        // (start == 0) & (isRunning <=> true)
var location = x.Zip(y).Biselect((across, down) => new Location(across, down), place => (place.X, place.Y));
var schedule = starts.Sequence();                      // IEncodable<ImmutableArray<LocalDateTime>>
```

What they have in common is `IEncodable<T>`: a list of components, each a linear expression, and a projection between `T` and the vector of their numbers. A quantity and a point are an `IEncodable` of one number, a two-state type one of one indicator, and a zip or a sequence one of as many as it is made of; `AsEncodable()` lets a plain linear or boolean expression join in, a truth by its indicator. Comparison, `EqualTo`, `NotEqualTo`, `Between`, `solution.Value` and `solution.With` are written once against it, and work entry by entry: a relation holds when it holds in every entry, and two values differ where any entry does. Between truths that is logic, since they are ones and zeroes: `[a] <= [b]` is `a => b`, and `[a] == [b]` is `a <=> b`. Values under different projections are reconciled entry by entry, as quantities are, even when their entries come in another order. So `solution.Value(schedule)` is the whole array, and `schedule.EqualTo(plan)` pins it. There is no `pure` among them: writing a constant through a projection would forget which constant it was.

A `Vector<T>` holds expressions rather than reading them as values, and is combined entry by entry: `Zip` pairs the entries of two vectors and `Select` maps each, so the function sees the quantities themselves and builds expressions out of them.

```csharp
var u = Vector.Of(Variable.Continuous<double>("u0"), Variable.Continuous<double>("u1"), Variable.Continuous<double>("u2"));

var sums     = u.Zip(v).Select((a, b) => a + b);          // u0 + v0, u1 + v1, u2 + v2
var fits     = u + v < [4, 5, 6];                         // (u0 + v0 < 4) & (u1 + v1 < 5) & (u2 + v2 < 6)
var runtimes = finishes.Zip(starts).Select((f, s) => f - s);
//             u.Zip(v).Select((a, b) => a * b)           // does not compile: not linear
```

Vectors of quantities add, subtract, scale, `Sum`, take a `Dot` product with plain weights, and compare with each other, with plain arrays and with plain values, which stand for themselves in every entry. A comparison holds when it holds in every entry, so `!(u <= w)` is that some entry exceeds, not `u > w`. A vector is read as it is mapped: `u.Select(solution.Value)` is the vector of values, whatever its entries are. Vectors of anything else, points included, are combined through `Zip` and `Select`.

### Swapping the solver

`ISolver` is the seam: `ISolveResult Solve(IOneShotProblem problem, CancellationToken cancellationToken = default)`. A backend joins at whichever level suits the solver:

| A solver that takes... | implements | and sees | Examples |
| --- | --- | --- | --- |
| logic as it stands | `ISolver` | the `IOneShotProblem` itself | `Z3Solver` |
| conditional linear constraints | `IIndicatorBackend` | an `IndicatorProblem`: linear rows guarded by literals, no big-M | `GurobiBackend`, `CpSatBackend` |
| linear constraints only | `IMilpBackend` | a `MilpProblem`: bounded columns, linear rows, a linear objective | `HighsBackend`, `OrToolsBackend` |

`IndicatorSolver` and `MilpSolver` wrap the latter two into an `ISolver`: they encode, solve, hide the auxiliaries and snap whole-number variables. Both forms of lowered problem are an `ILoweredProblem` — bounded columns, a sense, a linear objective, and whether infeasibility is already evident — which is everything those wrappers need either side of the backend, so they are one routine given two arguments. A backend is about a hundred lines.

```csharp
GurobiSolver.Create()                       // indicator constraints, no big-M
GurobiSolver.Create(useIndicators: false)   // classic big-M, derived per row
CpSatSolver.Create()                        // whole-number problems only
HighsSolver.Create()
OrToolsSolver.Create(OrToolsSolverId.Scip)
new Z3Solver()
```

The test suite runs one contract against SCIP, CBC, HiGHS (standalone and through OR-Tools), Gurobi (both formulations) and Z3, and a second, whole-number contract against those and CP-SAT. The Gurobi tests are skipped where no licence is found.

### Tuning a solve, and what it reports

```csharp
var options = new SolverOptions(TimeLimit: TimeSpan.FromMinutes(5), RelativeGap: 0.01, Seed: 7, Log: Console.Write)
    .With("MIPFocus", "1");                       // the solver's own parameters, by its own names

var result = GurobiSolver.Create(options: options).Solve(problem);

result.Statistics.SolvingTime                     // and EncodingTime, BestBound, Nodes, Iterations
result.RelativeGap                                // how far a Feasible result might be from optimal
```

`TimeLimit`, `RelativeGap`, `AbsoluteGap`, `Threads` and `Seed` mean the same to every solver that has them. `Parameters` go to the solver verbatim, and one it does not recognise is an error rather than a silent no-op. Every result carries `Statistics`, whatever its outcome; a figure that a solver does not report is null (Z3 proves optimality without bounds, so it has no gap to give). Gurobi and CP-SAT deliver their log to `Log`; the others can only write to standard output, and do so when it is set.

### Several objectives

```csharp
var problem = Problem.Minimise(totalDelay, machineChanges)       // in order of priority
    .ThenMaximise(slack)
    .SubjectTo(constraint);

var result = solver.Solve(problem);
```

Objectives that are to be traded off against each other need nothing special: weigh them into one expression. Several expressions given to `Minimise` or `Maximise` are objectives in order of priority, as are those added with `ThenMinimise` and `ThenMaximise`. Either way the result is an `IMultipleObjectiveProblem`, where a problem with one objective or none is an `IOneShotProblem`; both are `IProblem`s, and `solver.Solve` and `FindConflict` take either. With several objectives, each is optimised in turn, among the solutions that are best for those before it. That is done with a sequence of ordinary solves, so it works with every solver: each stage is held to the values already found and starts from the solution before. An objective can give ground to those after it: `.ThenMinimise(changes, relativeTolerance: 0.01)`, or, for the general case, `.Then(Objective.Minimise(delay, tolerance: Duration.FromMinutes(2)))`. `Problem.Lexicographic([...])` takes the whole list at once, which is also how the first objective is given a tolerance. `SubjectTo` may come anywhere in the chain.

An objective is a value in its own right, so it can be built once and set against one set of constraints after another:

```csharp
var promptThenCheap = Objective.Minimise(totalDelay).ThenMinimise(cost, relativeTolerance: 0.05);

var baseline  = solver.Solve(Problem.Optimise(promptThenCheap).SubjectTo(schedule));
var breakdown = solver.Solve(Problem.Optimise(promptThenCheap).SubjectTo(schedule, machineTwoIsDown));
```

The kinds of objective are the kinds of problem. An `ISingleObjective` is `Objective.None` or an `Optimisation` (a sense and an expression); an `ILexicographicObjective` is a list of those, each with the tolerance that only makes sense among several. A problem is its objective and its constraint, and is an `IOneShotProblem` or an `IMultipleObjectiveProblem` accordingly — the first being a `SatisfactionProblem` where there is nothing to optimise and a `SingleObjectiveProblem` where there is, so that `new SingleObjectiveProblem(Objective.None, ...)` does not compile. The result is `Optimal` only if every stage was, its objective value is that of the first objective (read the others with `solution.Value(...)`), and the solver's limits apply to each stage separately.

### Writing a model to a file

```csharp
File.WriteAllText("schedule.lp",  problem.Encode().ToLp());        // big-M rows
File.WriteAllText("schedule.mps", problem.Encode().ToMps());
File.WriteAllText("schedule.lp",  problem.EncodeLogic().ToLp());   // indicator constraints, no big-M
```

LP and MPS are the formats that every solver reads, so a model can be opened in `gurobi_cl`, tuned with `grbtune`, kept as a benchmark, or sent to a solver's support desk. Rows know which constraint they were encoded from (`row.Origin`): a constraint that was given a name lends it to its rows, and in LP the constraint as written sits in a comment above each. Names are reduced to letters, digits and underscores and kept unique, since neither format allows much more. The files are tested by reading them back with HiGHS and Gurobi and reaching the same optimum.

### Warm starts

```csharp
var yesterday = solver.Solve(schedule).SolutionOrNull;
var today     = solver.Solve(scheduleWithOneMoreJob, startingFrom: yesterday);

var byHand    = Solution.Empty.With(startA, shiftStart.PlusMinutes(5)).With(usesA, true);
```

A starting solution is a hint and nothing more: it may cover only some of the variables, mention ones the problem lacks, or be infeasible, and the answer is the same, only perhaps sooner. Only the modeller's own variables are passed on; the auxiliaries of the encoding are left for the solver to fill in, which keeps a start valid across problems whose encodings differ. Gurobi, CP-SAT, SCIP, CBC and standalone HiGHS take the hint; Z3 has no use for one.

## ⚠️ Things worth knowing

- Sum and conjoin collections with `Sum()`, `AllOf()` and `AnyOf()` rather than folding `+` or `&` yourself. (Folding still works: deep trees are handled without overflowing the stack. It is merely slower, and the records' built-in `ToString`/`Equals` do recurse.)
- An equivalence duplicates its operands when negations are pushed inwards, so *deeply nested* `Iff`s grow exponentially. Name the inner ones with binary variables.
- Variables are identified by name and kind. Two variables of different kinds sharing a name is an error.
- Do not reference `Hephaestus.Optimisation.Highs` and `Hephaestus.Optimisation.OrTools` from the same application. Both upstream packages ship a native `highs.dll`, of different versions, and whichever is copied last breaks the other.
- CP-SAT works in whole numbers over finite domains. It refuses continuous variables by name, needs every variable bounded (directly or by implication), and scales fractional coefficients by a power of ten, refusing those that none makes whole (a third, say).
- HiGHS, as driven by OR-Tools, prints a one-line banner to standard output on every solve. That is upstream behaviour; the standalone HiGHS backend and the other solvers are silent.

## 🛠️ Building

```bash
dotnet build
dotnet test
dotnet run --project samples/MachineScheduling
dotnet pack --configuration Release --output artefacts
```

The test projects use xunit.v3 on Microsoft.Testing.Machine (see `global.json`). `tests/Hephaestus.Tests/EncodingEquivalenceTests.cs` is the one to read first: it checks, exhaustively over a grid and for hundreds of random formulas, that an assignment satisfies a formula exactly when it extends to a solution of the encoded rows.

Publishing a GitHub release tagged `v1.2.3` tests, packs and publishes version `1.2.3` of every package to nuget.org, and publishes the API documentation. See [CONTRIBUTING.md](CONTRIBUTING.md#-publishing-workflow).

## 👮‍♂️ Licence

MIT; see [LICENSE.md](LICENSE.md).

## 👁️ See also

- This library was made using [C# Library Template](https://github.com/SgtSwagrid/cs-library-template).
