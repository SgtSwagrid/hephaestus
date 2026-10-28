using Hephaestus.OrTools;
using Hephaestus.Z3;
using NodaTime;

namespace Hephaestus.NodaTime.Tests;

/// <summary>The typed layer end to end: constraints written in NodaTime types, solved, and read back in NodaTime types.</summary>
public sealed class SchedulingTests {
    private static readonly LocalDateTime ShiftStart = new(2026, 9, 19, 8, 0);
    private static readonly Duration Changeover = Duration.FromMinutes(2);
    private static readonly Duration Moment = Duration.FromMilliseconds(1);

    // NodaTime types only build linear expressions, so one solver per scenario is enough: the solver contracts show that they agree.

    private static void AssertClose(LocalDateTime expected, LocalDateTime actual) =>
        Assert.True(Period.Between(expected, actual, PeriodUnits.Nanoseconds).ToDuration() is var gap && gap < Moment && gap > -Moment, $"Expected {expected}, got {actual}.");

    [Fact]
    public void TwoJobsOnOneMachineKeepTheirChangeover() {
        var startA = Variable.LocalDateTime("startA", origin: ShiftStart);
        var startB = Variable.LocalDateTime("startB", origin: ShiftStart);
        var separated = (startA + Changeover <= startB) | (startB + Changeover <= startA);
        var constraint =
            startA.Between(ShiftStart.PlusMinutes(5), ShiftStart.PlusHours(1))
            & startB.Between(ShiftStart.PlusMinutes(4), ShiftStart.PlusHours(1))
            & separated;

        var solution = Assert.IsType<Optimal>(OrToolsSolver.Create(OrToolsSolverId.Scip).Solve(Problem.Minimise((startA - ShiftStart) + (startB - ShiftStart)).SubjectTo(constraint))).Solution;

        AssertClose(ShiftStart.PlusMinutes(4), solution.Value(startB));
        AssertClose(ShiftStart.PlusMinutes(6), solution.Value(startA));
        Assert.True(solution.Value(separated));
    }

    [Fact]
    public void QuantisedDurationsComeBackAsWholeUnits() {
        var runtime = Variable.Duration("runtime", unit: Duration.FromSeconds(30), inWholeUnits: true);
        var finish = Variable.LocalDateTime("finish", origin: ShiftStart);
        var start = Variable.LocalDateTime("start", origin: ShiftStart);
        var constraint =
            finish.EqualTo(ShiftStart.PlusMinutes(10))
            & start.Between(ShiftStart, ShiftStart.PlusHours(1))
            & (start - finish).EqualTo(runtime)
            & (runtime >= Duration.FromSeconds(50));

        var solution = Assert.IsType<Optimal>(new Z3Solver().Solve(Problem.Minimise(start).SubjectTo(constraint))).Solution;

        Assert.Equal(Duration.FromSeconds(60), solution.Value(runtime));
        AssertClose(ShiftStart.PlusMinutes(11), solution.Value(start));
    }

    [Fact]
    public void DatesAndPeriodsWorkInWholeDays() {
        var today = new LocalDate(2026, 9, 19);
        var possession = Variable.LocalDate("possession", origin: today);
        var reopening = Variable.LocalDate("reopening", origin: today);
        var constraint =
            possession.Between(today.PlusDays(3), today.PlusDays(30))
            & (reopening - possession >= Period.FromWeeks(1))
            & (possession.NotEqualTo(today.PlusDays(3)));

        var solution = Assert.IsType<Optimal>(OrToolsSolver.Create(OrToolsSolverId.Highs).Solve(Problem.Minimise(reopening).SubjectTo(constraint))).Solution;

        Assert.Equal(today.PlusDays(4), solution.Value(possession));
        Assert.Equal(today.PlusDays(11), solution.Value(reopening));
        Assert.Equal(Period.FromDays(7), solution.Value(reopening - possession));
    }

    [Fact]
    public void InstantsAndZonedDateTimesShareTheGlobalTimeLine() {
        var origin = Instant.FromUtc(2026, 9, 19, 0, 0);
        var sydney = DateTimeZoneProviders.Tzdb["Australia/Sydney"];
        var handover = Variable.Instant("handover", origin, unit: Duration.FromMinutes(1));
        var briefing = Variable.ZonedDateTime("briefing", origin.InZone(sydney), unit: Duration.FromMinutes(1));
        var constraint =
            handover.Between(origin, origin + Duration.FromHours(12))
            & briefing.Between(origin.InZone(sydney), (origin + Duration.FromHours(12)).InZone(sydney))
            & (handover >= origin + Duration.FromHours(3))
            & ((briefing - origin.InZone(sydney)) >= (handover - origin) + Duration.FromMinutes(30));

        var solution = Assert.IsType<Optimal>(new Z3Solver().Solve(Problem.Minimise(briefing).SubjectTo(constraint))).Solution;

        Assert.Equal(origin + Duration.FromHours(3), solution.Value(handover));
        Assert.Equal((origin + Duration.FromMinutes(210)).InZone(sydney), solution.Value(briefing));
    }

    [Fact]
    public void TimesOfDayStayWithinTheirDayWhenConstrainedTo() {
        var cutoff = Variable.LocalTime("cutoff", unit: Duration.FromMinutes(1));
        var constraint = cutoff.Between(LocalTime.Midnight, LocalTime.MaxValue) & (cutoff >= new LocalTime(22, 15)) & (cutoff - new LocalTime(22, 0) <= Duration.FromMinutes(45));

        var solution = Assert.IsType<Optimal>(OrToolsSolver.Create().Solve(Problem.Maximise(cutoff).SubjectTo(constraint))).Solution;

        Assert.Equal(new LocalTime(22, 45), solution.Value(cutoff));
    }

    [Fact]
    public void PlainValuesShiftedByQuantitiesArePoints() {
        var runtime = Variable.Duration("runtime", unit: Duration.FromSeconds(30), inWholeUnits: true);
        var days = Variable.LocalDate("reopening", origin: new LocalDate(2026, 9, 19)) - new LocalDate(2026, 9, 19);
        var sydney = DateTimeZoneProviders.Tzdb["Australia/Sydney"];
        var instant = Instant.FromUtc(2026, 9, 19, 0, 0);
        var solution = new Solution(
            System.Collections.Immutable.ImmutableSortedDictionary.Create<IVariable, double>(VariableOrder.Comparer).Add(Variable.Integer("runtime"), 3).Add(Variable.Integer("reopening"), 10),
            0);

        Assert.Equal(ShiftStart.PlusSeconds(90), solution.Value(ShiftStart + runtime));
        Assert.Equal(ShiftStart.PlusSeconds(90), solution.Value(runtime + ShiftStart));
        Assert.Equal(ShiftStart.PlusSeconds(-90), solution.Value(ShiftStart - runtime));
        Assert.Equal(instant + Duration.FromSeconds(90), solution.Value(instant + runtime));
        Assert.Equal((instant + Duration.FromSeconds(90)).InZone(sydney), solution.Value(instant.InZone(sydney) + runtime));
        Assert.Equal((instant + Duration.FromSeconds(90)).WithOffset(Offset.FromHours(10)), solution.Value(instant.WithOffset(Offset.FromHours(10)) + runtime));
        Assert.Equal(new LocalTime(8, 1, 30), solution.Value(new LocalTime(8, 0) + runtime));
        Assert.Equal(new LocalDate(2026, 10, 11), solution.Value(new LocalDate(2026, 10, 1) + days));
        Assert.Equal(new LocalDate(2026, 9, 21), solution.Value(new LocalDate(2026, 10, 1) - days));
    }

    [Fact]
    public void AStartWaitsForTheRuntimeAfterAFixedFinish() {
        var runtime = Variable.Duration("runtime");
        var start = Variable.LocalDateTime("start", origin: ShiftStart);
        var constraint = runtime.Between(Duration.FromSeconds(45), Duration.FromMinutes(5)) & start.Between(ShiftStart, ShiftStart.PlusHours(1)) & (start >= ShiftStart.PlusMinutes(10) + runtime);

        var solution = Assert.IsType<Optimal>(OrToolsSolver.Create(OrToolsSolverId.Highs).Solve(Problem.Minimise(start).SubjectTo(constraint))).Solution;

        AssertClose(ShiftStart.PlusMinutes(10).PlusSeconds(45), solution.Value(start));
    }

    [Fact]
    public void WhatDoesNotMakeSenseDoesNotCompile() {
        // startA + startB          -- no operator + for two points
        // 2 * startA                   -- points cannot be scaled
        // startA <= Changeover            -- a point is not comparable with a quantity
        // (startA - startB) <= ShiftStart -- nor a quantity with a point
        var start = Variable.LocalDateTime("start", origin: ShiftStart);

        Quantity<Duration> sinceStart = start - ShiftStart;
        Point<LocalDateTime, Duration> shifted = start + 2 * sinceStart - Changeover;

        Assert.Equal((120 - 2 * Variable.Continuous("start")).Normalise(), (start - shifted).Expression.Normalise());
    }
}
