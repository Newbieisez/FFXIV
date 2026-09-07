namespace EZBuddy.Core.GoldSaucer;

[Flags]
public enum TripleTriadRules
{
    None = 0,
    Same = 1 << 0,
    Plus = 1 << 1,
    Reverse = 1 << 2,
    FallenAce = 1 << 3,
    Ascension = 1 << 4,
    Descension = 1 << 5,
    Order = 1 << 6
}

public enum TripleTriadOwner
{
    Self,
    Opponent
}

public enum TripleTriadSide
{
    Up,
    Right,
    Down,
    Left
}

public sealed record TripleTriadCardInstance(
    string InstanceId,
    uint CardId,
    string Name,
    int Up,
    int Right,
    int Down,
    int Left,
    int DeckOrder,
    string? TypeKey = null)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(InstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (CardId == 0)
        {
            throw new InvalidDataException($"Triple Triad card '{Name}' requires a non-zero card ID.");
        }

        if (DeckOrder is < 0 or > 4)
        {
            throw new InvalidDataException($"Triple Triad card '{Name}' has invalid deck order {DeckOrder}.");
        }

        foreach (var value in new[] { Up, Right, Down, Left })
        {
            if (value is < 1 or > 10)
            {
                throw new InvalidDataException($"Triple Triad card '{Name}' side values must be between 1 and 10 (10 = A). ");
            }
        }
    }

    public int GetSide(TripleTriadSide side) => side switch
    {
        TripleTriadSide.Up => Up,
        TripleTriadSide.Right => Right,
        TripleTriadSide.Down => Down,
        TripleTriadSide.Left => Left,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };
}

public sealed record TripleTriadPlacedCard(
    TripleTriadCardInstance Card,
    TripleTriadOwner Owner);

public sealed record TripleTriadGameState(
    IReadOnlyList<TripleTriadPlacedCard?> Board,
    IReadOnlyList<TripleTriadCardInstance> SelfHand,
    IReadOnlyList<TripleTriadCardInstance> OpponentHand,
    TripleTriadOwner Turn,
    TripleTriadRules Rules)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Board);
        ArgumentNullException.ThrowIfNull(SelfHand);
        ArgumentNullException.ThrowIfNull(OpponentHand);
        if (Board.Count != 9)
        {
            throw new InvalidDataException("Triple Triad board must contain exactly nine cells.");
        }

        if (Rules.HasFlag(TripleTriadRules.Ascension) && Rules.HasFlag(TripleTriadRules.Descension))
        {
            throw new InvalidDataException("Ascension and Descension cannot both be active in one deterministic rule set.");
        }

        var instanceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in Board.Where(cell => cell is not null).Select(cell => cell!.Card)
                     .Concat(SelfHand)
                     .Concat(OpponentHand))
        {
            card.Validate();
            if (!instanceIds.Add(card.InstanceId))
            {
                throw new InvalidDataException($"Duplicate Triple Triad card instance ID '{card.InstanceId}'.");
            }
        }
    }

    public bool IsTerminal => Board.All(cell => cell is not null);
}

public sealed record TripleTriadMove(
    string CardInstanceId,
    int CellIndex);

public sealed record TripleTriadMoveResult(
    TripleTriadGameState State,
    int CapturedCards,
    bool SameTriggered,
    bool PlusTriggered,
    int ComboCaptures);

public sealed record TripleTriadSolverOptions(
    int MaximumSearchDepth = 6,
    int MaximumSearchNodes = 250_000)
{
    public void Validate()
    {
        if (MaximumSearchDepth is < 1 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumSearchDepth));
        }

        if (MaximumSearchNodes is < 100 or > 5_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumSearchNodes));
        }
    }
}

public sealed record TripleTriadRecommendation(
    TripleTriadMove Move,
    int ExpectedScore,
    int SearchDepth,
    int NodesEvaluated,
    int ImmediateCaptures);

/// <summary>
/// Deterministic Triple Triad simulation and alpha-beta move search. Rules whose randomness is
/// resolved before a move (Random, Swap, Chaos, Draft, Roulette) must be represented by the actual
/// host-provided hand/forced-card state rather than guessed by this solver.
/// </summary>
public static class TripleTriadSolver
{
    private static readonly Direction[] Directions =
    [
        new(-3, TripleTriadSide.Up, TripleTriadSide.Down),
        new(1, TripleTriadSide.Right, TripleTriadSide.Left),
        new(3, TripleTriadSide.Down, TripleTriadSide.Up),
        new(-1, TripleTriadSide.Left, TripleTriadSide.Right)
    ];

    public static IReadOnlyList<TripleTriadMove> GetLegalMoves(
        TripleTriadGameState state,
        string? forcedCardInstanceId = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();
        if (state.IsTerminal)
        {
            return [];
        }

        var hand = state.Turn == TripleTriadOwner.Self ? state.SelfHand : state.OpponentHand;
        IEnumerable<TripleTriadCardInstance> playable = hand;

        if (!string.IsNullOrWhiteSpace(forcedCardInstanceId))
        {
            playable = hand.Where(card => string.Equals(card.InstanceId, forcedCardInstanceId, StringComparison.Ordinal));
        }
        else if (state.Rules.HasFlag(TripleTriadRules.Order))
        {
            var ordered = hand.OrderBy(card => card.DeckOrder).FirstOrDefault();
            playable = ordered is null ? [] : [ordered];
        }

        var cards = playable.ToArray();
        if (cards.Length == 0)
        {
            return [];
        }

        var openCells = Enumerable.Range(0, 9).Where(index => state.Board[index] is null).ToArray();
        return cards
            .SelectMany(card => openCells.Select(cell => new TripleTriadMove(card.InstanceId, cell)))
            .ToArray();
    }

    public static TripleTriadMoveResult ApplyMove(TripleTriadGameState state, TripleTriadMove move)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(move);
        state.Validate();

        if (move.CellIndex is < 0 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(move.CellIndex));
        }

        if (state.Board[move.CellIndex] is not null)
        {
            throw new InvalidOperationException($"Triple Triad cell {move.CellIndex} is already occupied.");
        }

        var currentHand = state.Turn == TripleTriadOwner.Self ? state.SelfHand : state.OpponentHand;
        var card = currentHand.FirstOrDefault(candidate => string.Equals(candidate.InstanceId, move.CardInstanceId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Card '{move.CardInstanceId}' is not available in the current player's hand.");

        if (state.Rules.HasFlag(TripleTriadRules.Order) &&
            currentHand.Any(candidate => candidate.DeckOrder < card.DeckOrder))
        {
            throw new InvalidOperationException("Order rule requires the earliest remaining deck card to be played.");
        }

        var board = state.Board.ToArray();
        board[move.CellIndex] = new TripleTriadPlacedCard(card, state.Turn);
        var typeCounts = BuildTypeCounts(board);
        var neighbors = GetNeighbors(move.CellIndex, board).ToArray();

        // Same/Plus qualification is determined from the board as it existed immediately after
        // placement, before ownership mutations caused by any capture mechanic.
        var sameMatches = state.Rules.HasFlag(TripleTriadRules.Same)
            ? neighbors.Where(neighbor =>
                    EffectiveSide(card, neighbor.AttackerSide, typeCounts, state.Rules) ==
                    EffectiveSide(neighbor.Placed.Card, neighbor.DefenderSide, typeCounts, state.Rules))
                .ToArray()
            : [];
        var sameTriggered = sameMatches.Length >= 2 && sameMatches.Any(match => match.Placed.Owner != state.Turn);

        var plusGroups = state.Rules.HasFlag(TripleTriadRules.Plus)
            ? neighbors.GroupBy(neighbor =>
                    EffectiveSide(card, neighbor.AttackerSide, typeCounts, state.Rules) +
                    EffectiveSide(neighbor.Placed.Card, neighbor.DefenderSide, typeCounts, state.Rules))
                .Where(group => group.Count() >= 2 && group.Any(match => match.Placed.Owner != state.Turn))
                .SelectMany(group => group)
                .DistinctBy(neighbor => neighbor.CellIndex)
                .ToArray()
            : [];
        var plusTriggered = plusGroups.Length >= 2;

        var specialCaptureIndexes = new HashSet<int>();
        if (sameTriggered)
        {
            foreach (var match in sameMatches.Where(match => match.Placed.Owner != state.Turn))
            {
                specialCaptureIndexes.Add(match.CellIndex);
            }
        }

        if (plusTriggered)
        {
            foreach (var match in plusGroups.Where(match => match.Placed.Owner != state.Turn))
            {
                specialCaptureIndexes.Add(match.CellIndex);
            }
        }

        var captured = 0;
        var comboQueue = new Queue<int>();
        foreach (var index in specialCaptureIndexes)
        {
            board[index] = board[index]! with { Owner = state.Turn };
            captured++;
            comboQueue.Enqueue(index);
        }

        // Basic captures from the placed card still apply alongside Same/Plus.
        foreach (var neighbor in GetNeighbors(move.CellIndex, board))
        {
            if (neighbor.Placed.Owner == state.Turn)
            {
                continue;
            }

            var attacker = EffectiveSide(card, neighbor.AttackerSide, typeCounts, state.Rules);
            var defender = EffectiveSide(neighbor.Placed.Card, neighbor.DefenderSide, typeCounts, state.Rules);
            if (WinsComparison(attacker, defender, state.Rules))
            {
                board[neighbor.CellIndex] = neighbor.Placed with { Owner = state.Turn };
                captured++;
            }
        }

        var comboCaptures = 0;
        var comboVisited = new HashSet<int>();
        while (comboQueue.Count > 0)
        {
            var sourceIndex = comboQueue.Dequeue();
            if (!comboVisited.Add(sourceIndex))
            {
                continue;
            }

            var source = board[sourceIndex]!;
            foreach (var neighbor in GetNeighbors(sourceIndex, board))
            {
                if (neighbor.Placed.Owner == state.Turn)
                {
                    continue;
                }

                var attacker = EffectiveSide(source.Card, neighbor.AttackerSide, typeCounts, state.Rules);
                var defender = EffectiveSide(neighbor.Placed.Card, neighbor.DefenderSide, typeCounts, state.Rules);
                if (!WinsComparison(attacker, defender, state.Rules))
                {
                    continue;
                }

                board[neighbor.CellIndex] = neighbor.Placed with { Owner = state.Turn };
                captured++;
                comboCaptures++;
                comboQueue.Enqueue(neighbor.CellIndex);
            }
        }

        var nextSelfHand = state.SelfHand;
        var nextOpponentHand = state.OpponentHand;
        if (state.Turn == TripleTriadOwner.Self)
        {
            nextSelfHand = state.SelfHand.Where(candidate => !string.Equals(candidate.InstanceId, card.InstanceId, StringComparison.Ordinal)).ToArray();
        }
        else
        {
            nextOpponentHand = state.OpponentHand.Where(candidate => !string.Equals(candidate.InstanceId, card.InstanceId, StringComparison.Ordinal)).ToArray();
        }

        var nextState = new TripleTriadGameState(
            board,
            nextSelfHand,
            nextOpponentHand,
            Opposite(state.Turn),
            state.Rules);

        return new TripleTriadMoveResult(nextState, captured, sameTriggered, plusTriggered, comboCaptures);
    }

    public static TripleTriadRecommendation RecommendMove(
        TripleTriadGameState state,
        TripleTriadSolverOptions? options = null,
        string? forcedCardInstanceId = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();
        options ??= new TripleTriadSolverOptions();
        options.Validate();

        var legalMoves = GetLegalMoves(state, forcedCardInstanceId);
        if (legalMoves.Count == 0)
        {
            throw new InvalidOperationException("No legal Triple Triad move is available for the current state.");
        }

        var remainingPlays = state.Board.Count(cell => cell is null);
        var depth = Math.Min(options.MaximumSearchDepth, remainingPlays);
        var context = new SearchContext(options.MaximumSearchNodes);
        TripleTriadMove? bestMove = null;
        var bestScore = state.Turn == TripleTriadOwner.Self ? int.MinValue : int.MaxValue;
        var bestCaptures = -1;

        foreach (var move in legalMoves)
        {
            var result = ApplyMove(state, move);
            var score = Search(result.State, depth - 1, int.MinValue + 1, int.MaxValue - 1, context);
            var better = state.Turn == TripleTriadOwner.Self
                ? score > bestScore || score == bestScore && TieBreak(move, result.CapturedCards, bestMove, bestCaptures)
                : score < bestScore || score == bestScore && TieBreak(move, result.CapturedCards, bestMove, bestCaptures);

            if (better)
            {
                bestMove = move;
                bestScore = score;
                bestCaptures = result.CapturedCards;
            }

            if (context.Nodes >= options.MaximumSearchNodes)
            {
                break;
            }
        }

        return new TripleTriadRecommendation(
            bestMove ?? legalMoves[0],
            bestScore,
            depth,
            context.Nodes,
            Math.Max(0, bestCaptures));
    }

    public static (int SelfScore, int OpponentScore) Score(TripleTriadGameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var self = state.Board.Count(cell => cell?.Owner == TripleTriadOwner.Self) + state.SelfHand.Count;
        var opponent = state.Board.Count(cell => cell?.Owner == TripleTriadOwner.Opponent) + state.OpponentHand.Count;
        return (self, opponent);
    }

    private static int Search(
        TripleTriadGameState state,
        int depth,
        int alpha,
        int beta,
        SearchContext context)
    {
        context.Nodes++;
        if (depth <= 0 || state.IsTerminal || context.Nodes >= context.MaximumNodes)
        {
            return Evaluate(state);
        }

        var moves = GetLegalMoves(state);
        if (moves.Count == 0)
        {
            return Evaluate(state);
        }

        if (state.Turn == TripleTriadOwner.Self)
        {
            var value = int.MinValue + 1;
            foreach (var move in OrderMoves(state, moves, descending: true))
            {
                value = Math.Max(value, Search(ApplyMove(state, move).State, depth - 1, alpha, beta, context));
                alpha = Math.Max(alpha, value);
                if (alpha >= beta || context.Nodes >= context.MaximumNodes)
                {
                    break;
                }
            }
            return value;
        }
        else
        {
            var value = int.MaxValue;
            foreach (var move in OrderMoves(state, moves, descending: false))
            {
                value = Math.Min(value, Search(ApplyMove(state, move).State, depth - 1, alpha, beta, context));
                beta = Math.Min(beta, value);
                if (alpha >= beta || context.Nodes >= context.MaximumNodes)
                {
                    break;
                }
            }
            return value;
        }
    }

    private static IEnumerable<TripleTriadMove> OrderMoves(
        TripleTriadGameState state,
        IReadOnlyList<TripleTriadMove> moves,
        bool descending)
    {
        var scored = moves.Select(move =>
        {
            var result = ApplyMove(state, move);
            return (Move: move, Captures: result.CapturedCards, Positional: PositionValue(move.CellIndex));
        });

        return descending
            ? scored.OrderByDescending(item => item.Captures).ThenByDescending(item => item.Positional).Select(item => item.Move)
            : scored.OrderByDescending(item => item.Captures).ThenByDescending(item => item.Positional).Select(item => item.Move);
    }

    private static int Evaluate(TripleTriadGameState state)
    {
        var (self, opponent) = Score(state);
        var score = (self - opponent) * 1_000;

        for (var index = 0; index < state.Board.Count; index++)
        {
            var placed = state.Board[index];
            if (placed is null)
            {
                continue;
            }

            var sign = placed.Owner == TripleTriadOwner.Self ? 1 : -1;
            score += sign * PositionValue(index) * 5;
        }

        return score;
    }

    private static bool TieBreak(
        TripleTriadMove move,
        int captures,
        TripleTriadMove? bestMove,
        int bestCaptures)
    {
        if (bestMove is null)
        {
            return true;
        }

        if (captures != bestCaptures)
        {
            return captures > bestCaptures;
        }

        var position = PositionValue(move.CellIndex);
        var bestPosition = PositionValue(bestMove.CellIndex);
        if (position != bestPosition)
        {
            return position > bestPosition;
        }

        return string.CompareOrdinal(move.CardInstanceId, bestMove.CardInstanceId) < 0;
    }

    private static int PositionValue(int cellIndex) => cellIndex switch
    {
        0 or 2 or 6 or 8 => 3,
        4 => 2,
        _ => 1
    };

    private static bool WinsComparison(int attacker, int defender, TripleTriadRules rules)
    {
        var reverse = rules.HasFlag(TripleTriadRules.Reverse);
        if (rules.HasFlag(TripleTriadRules.FallenAce))
        {
            if (!reverse)
            {
                if (attacker == 1 && defender == 10) return true;
                if (attacker == 10 && defender == 1) return false;
            }
            else
            {
                if (attacker == 10 && defender == 1) return true;
                if (attacker == 1 && defender == 10) return false;
            }
        }

        return reverse ? attacker < defender : attacker > defender;
    }

    private static int EffectiveSide(
        TripleTriadCardInstance card,
        TripleTriadSide side,
        IReadOnlyDictionary<string, int> typeCounts,
        TripleTriadRules rules)
    {
        var value = card.GetSide(side);
        if (string.IsNullOrWhiteSpace(card.TypeKey) || !typeCounts.TryGetValue(card.TypeKey, out var count))
        {
            return value;
        }

        if (rules.HasFlag(TripleTriadRules.Ascension))
        {
            return Math.Min(10, value + count);
        }

        if (rules.HasFlag(TripleTriadRules.Descension))
        {
            return Math.Max(1, value - count);
        }

        return value;
    }

    private static IReadOnlyDictionary<string, int> BuildTypeCounts(IReadOnlyList<TripleTriadPlacedCard?> board)
        => board
            .Where(cell => cell is not null && !string.IsNullOrWhiteSpace(cell.Card.TypeKey))
            .GroupBy(cell => cell!.Card.TypeKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<Neighbor> GetNeighbors(
        int cellIndex,
        IReadOnlyList<TripleTriadPlacedCard?> board)
    {
        var row = cellIndex / 3;
        var column = cellIndex % 3;

        foreach (var direction in Directions)
        {
            if (direction.Delta == -3 && row == 0 ||
                direction.Delta == 3 && row == 2 ||
                direction.Delta == -1 && column == 0 ||
                direction.Delta == 1 && column == 2)
            {
                continue;
            }

            var adjacentIndex = cellIndex + direction.Delta;
            var placed = board[adjacentIndex];
            if (placed is null)
            {
                continue;
            }

            yield return new Neighbor(
                adjacentIndex,
                placed,
                direction.AttackerSide,
                direction.DefenderSide);
        }
    }

    private static TripleTriadOwner Opposite(TripleTriadOwner owner)
        => owner == TripleTriadOwner.Self ? TripleTriadOwner.Opponent : TripleTriadOwner.Self;

    private readonly record struct Direction(
        int Delta,
        TripleTriadSide AttackerSide,
        TripleTriadSide DefenderSide);

    private readonly record struct Neighbor(
        int CellIndex,
        TripleTriadPlacedCard Placed,
        TripleTriadSide AttackerSide,
        TripleTriadSide DefenderSide);

    private sealed class SearchContext
    {
        public SearchContext(int maximumNodes)
        {
            MaximumNodes = maximumNodes;
        }

        public int MaximumNodes { get; }
        public int Nodes { get; set; }
    }
}
