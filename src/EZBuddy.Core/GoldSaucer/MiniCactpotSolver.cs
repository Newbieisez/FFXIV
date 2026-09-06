namespace EZBuddy.Core.GoldSaucer;

public sealed record MiniCactpotLineEvaluation(
    IReadOnlyList<int> CellIndexes,
    double ExpectedPayout,
    int? KnownSum,
    int UnknownCells);

public sealed record MiniCactpotRevealRecommendation(
    int CellIndex,
    double ExpectedBestLinePayoutAfterReveal);

public static class MiniCactpotSolver
{
    private static readonly int[][] Lines =
    [
        [0, 1, 2],
        [3, 4, 5],
        [6, 7, 8],
        [0, 3, 6],
        [1, 4, 7],
        [2, 5, 8],
        [0, 4, 8],
        [2, 4, 6]
    ];

    public static IReadOnlyDictionary<int, int> DefaultPayouts { get; } = new Dictionary<int, int>
    {
        [6] = 10_000,
        [7] = 36,
        [8] = 720,
        [9] = 360,
        [10] = 80,
        [11] = 252,
        [12] = 108,
        [13] = 72,
        [14] = 54,
        [15] = 180,
        [16] = 72,
        [17] = 180,
        [18] = 119,
        [19] = 36,
        [20] = 306,
        [21] = 1_080,
        [22] = 144,
        [23] = 1_800,
        [24] = 3_600
    };

    public static IReadOnlyList<MiniCactpotLineEvaluation> EvaluateLines(
        IReadOnlyList<int?> cells,
        IReadOnlyDictionary<int, int>? payouts = null)
    {
        ValidateBoard(cells);
        payouts ??= DefaultPayouts;

        var remainingDigits = GetRemainingDigits(cells);
        return Lines
            .Select(line => EvaluateLine(cells, line, remainingDigits, payouts))
            .OrderByDescending(result => result.ExpectedPayout)
            .ThenBy(result => result.UnknownCells)
            .ToArray();
    }

    public static MiniCactpotLineEvaluation RecommendLine(
        IReadOnlyList<int?> cells,
        IReadOnlyDictionary<int, int>? payouts = null)
        => EvaluateLines(cells, payouts).First();

    public static MiniCactpotRevealRecommendation? RecommendNextReveal(
        IReadOnlyList<int?> cells,
        IReadOnlyDictionary<int, int>? payouts = null)
    {
        ValidateBoard(cells);
        payouts ??= DefaultPayouts;

        var unknownIndexes = Enumerable.Range(0, 9).Where(index => cells[index] is null).ToArray();
        if (unknownIndexes.Length == 0)
        {
            return null;
        }

        var remainingDigits = GetRemainingDigits(cells);
        MiniCactpotRevealRecommendation? best = null;

        foreach (var index in unknownIndexes)
        {
            var totalBestLineValue = 0d;
            foreach (var digit in remainingDigits)
            {
                var simulated = cells.ToArray();
                simulated[index] = digit;
                totalBestLineValue += RecommendLine(simulated, payouts).ExpectedPayout;
            }

            var expectedValue = totalBestLineValue / remainingDigits.Count;
            var candidate = new MiniCactpotRevealRecommendation(index, expectedValue);
            if (best is null ||
                candidate.ExpectedBestLinePayoutAfterReveal > best.ExpectedBestLinePayoutAfterReveal ||
                (Math.Abs(candidate.ExpectedBestLinePayoutAfterReveal - best.ExpectedBestLinePayoutAfterReveal) < 0.000001 &&
                 TieBreakReveal(index, best.CellIndex)))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static MiniCactpotLineEvaluation EvaluateLine(
        IReadOnlyList<int?> cells,
        IReadOnlyList<int> line,
        IReadOnlyList<int> remainingDigits,
        IReadOnlyDictionary<int, int> payouts)
    {
        var knownValues = line.Where(index => cells[index].HasValue).Select(index => cells[index]!.Value).ToArray();
        var unknownCount = line.Count(index => cells[index] is null);
        var knownSum = knownValues.Sum();

        if (unknownCount == 0)
        {
            return new MiniCactpotLineEvaluation(
                line.ToArray(),
                payouts.TryGetValue(knownSum, out var payout) ? payout : 0,
                knownSum,
                0);
        }

        var combinations = Choose(remainingDigits, unknownCount).ToArray();
        if (combinations.Length == 0)
        {
            return new MiniCactpotLineEvaluation(line.ToArray(), 0, knownSum, unknownCount);
        }

        var expectedPayout = combinations.Average(combination =>
        {
            var sum = knownSum + combination.Sum();
            return payouts.TryGetValue(sum, out var payout) ? payout : 0;
        });

        return new MiniCactpotLineEvaluation(line.ToArray(), expectedPayout, knownSum, unknownCount);
    }

    private static IReadOnlyList<int> GetRemainingDigits(IReadOnlyList<int?> cells)
    {
        var known = cells.Where(value => value.HasValue).Select(value => value!.Value).ToHashSet();
        return Enumerable.Range(1, 9).Where(value => !known.Contains(value)).ToArray();
    }

    private static IEnumerable<int[]> Choose(IReadOnlyList<int> source, int count)
    {
        if (count == 0)
        {
            yield return Array.Empty<int>();
            yield break;
        }

        foreach (var combination in ChooseCore(source, count, 0, new List<int>(count)))
        {
            yield return combination;
        }
    }

    private static IEnumerable<int[]> ChooseCore(
        IReadOnlyList<int> source,
        int count,
        int start,
        List<int> current)
    {
        if (current.Count == count)
        {
            yield return current.ToArray();
            yield break;
        }

        for (var index = start; index <= source.Count - (count - current.Count); index++)
        {
            current.Add(source[index]);
            foreach (var combination in ChooseCore(source, count, index + 1, current))
            {
                yield return combination;
            }
            current.RemoveAt(current.Count - 1);
        }
    }

    private static bool TieBreakReveal(int candidateIndex, int currentIndex)
    {
        static int Preference(int index) => index switch
        {
            4 => 0,
            0 or 2 or 6 or 8 => 1,
            _ => 2
        };

        var candidatePreference = Preference(candidateIndex);
        var currentPreference = Preference(currentIndex);
        return candidatePreference < currentPreference ||
               (candidatePreference == currentPreference && candidateIndex < currentIndex);
    }

    private static void ValidateBoard(IReadOnlyList<int?> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Count != 9)
        {
            throw new ArgumentException("Mini Cactpot boards contain exactly nine cells.", nameof(cells));
        }

        var known = cells.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        if (known.Any(value => value is < 1 or > 9))
        {
            throw new ArgumentOutOfRangeException(nameof(cells), "Revealed Mini Cactpot values must be between 1 and 9.");
        }

        if (known.Distinct().Count() != known.Length)
        {
            throw new ArgumentException("Mini Cactpot digits are unique; duplicate revealed values are invalid.", nameof(cells));
        }
    }
}
