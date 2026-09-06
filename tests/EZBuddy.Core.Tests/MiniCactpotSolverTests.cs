using EZBuddy.Core.GoldSaucer;

namespace EZBuddy.Core.Tests;

public sealed class MiniCactpotSolverTests
{
    [Fact]
    public void CompleteWinningLineReturnsExactPayout()
    {
        int?[] board =
        [
            1, 2, 3,
            4, 5, 6,
            7, 8, 9
        ];

        var best = MiniCactpotSolver.RecommendLine(board);

        Assert.Equal(10_000, best.ExpectedPayout);
        Assert.Equal([0, 1, 2], best.CellIndexes);
    }

    [Fact]
    public void DuplicateRevealedDigitsAreRejected()
    {
        int?[] board =
        [
            1, 1, null,
            null, null, null,
            null, null, null
        ];

        Assert.Throws<ArgumentException>(() => MiniCactpotSolver.EvaluateLines(board));
    }

    [Fact]
    public void EmptyBoardPrefersCenterOnEquivalentOneStepValue()
    {
        int?[] board = new int?[9];

        var recommendation = MiniCactpotSolver.RecommendNextReveal(board);

        Assert.NotNull(recommendation);
        Assert.Equal(4, recommendation!.CellIndex);
    }

    [Fact]
    public void SolverAlwaysReturnsOneOfEightLines()
    {
        int?[] board =
        [
            1, null, null,
            null, 5, null,
            null, null, 9
        ];

        var lines = MiniCactpotSolver.EvaluateLines(board);

        Assert.Equal(8, lines.Count);
        Assert.All(lines, line => Assert.Equal(3, line.CellIndexes.Count));
    }
}
