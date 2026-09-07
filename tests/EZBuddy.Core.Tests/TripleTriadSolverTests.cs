using EZBuddy.Core.GoldSaucer;

namespace EZBuddy.Core.Tests;

public sealed class TripleTriadSolverTests
{
    [Fact]
    public void NormalRule_CapturesLowerAdjacentSide()
    {
        var board = EmptyBoard();
        board[5] = Placed(Card("enemy", 200, up: 1, right: 1, down: 1, left: 3), TripleTriadOwner.Opponent);
        var self = Card("self", 100, up: 1, right: 5, down: 1, left: 1);
        var state = State(board, [self], [], TripleTriadOwner.Self, TripleTriadRules.None);

        var result = TripleTriadSolver.ApplyMove(state, new TripleTriadMove("self", 4));

        Assert.Equal(TripleTriadOwner.Self, result.State.Board[5]!.Owner);
        Assert.Equal(1, result.CapturedCards);
    }

    [Fact]
    public void ReverseRule_CapturesWhenAttackerSideIsLower()
    {
        var board = EmptyBoard();
        board[5] = Placed(Card("enemy", 200, 1, 1, 1, 5), TripleTriadOwner.Opponent);
        var self = Card("self", 100, 1, 2, 1, 1);
        var state = State(board, [self], [], TripleTriadOwner.Self, TripleTriadRules.Reverse);

        var result = TripleTriadSolver.ApplyMove(state, new TripleTriadMove("self", 4));

        Assert.Equal(TripleTriadOwner.Self, result.State.Board[5]!.Owner);
    }

    [Fact]
    public void FallenAce_Normal_AllowsOneToCaptureAce()
    {
        var board = EmptyBoard();
        board[5] = Placed(Card("ace", 200, 1, 1, 1, 10), TripleTriadOwner.Opponent);
        var self = Card("one", 100, 1, 1, 1, 1);
        var state = State(board, [self], [], TripleTriadOwner.Self, TripleTriadRules.FallenAce);

        var result = TripleTriadSolver.ApplyMove(state, new TripleTriadMove("one", 4));

        Assert.Equal(TripleTriadOwner.Self, result.State.Board[5]!.Owner);
    }

    [Fact]
    public void FallenAce_WithReverse_AllowsAceToCaptureOne()
    {
        var board = EmptyBoard();
        board[5] = Placed(Card("one", 200, 1, 1, 1, 1), TripleTriadOwner.Opponent);
        var self = Card("ace", 100, 1, 10, 1, 1);
        var state = State(
            board,
            [self],
            [],
            TripleTriadOwner.Self,
            TripleTriadRules.FallenAce | TripleTriadRules.Reverse);

        var result = TripleTriadSolver.ApplyMove(state, new TripleTriadMove("ace", 4));

        Assert.Equal(TripleTriadOwner.Self, result.State.Board[5]!.Owner);
    }

    [Fact]
    public void SameRule_CapturesMatchesAndStartsCombo()
    {
        var board = EmptyBoard();
        board[1] = Placed(Card("up", 201, up: 2, right: 9, down: 5, left: 2), TripleTriadOwner.Opponent);
        board[5] = Placed(Card("right", 202, up: 2, right: 2, down: 2, left: 5), TripleTriadOwner.Opponent);
        board[2] = Placed(Card("combo-target", 203, up: 2, right: 2, down: 2, left: 1), TripleTriadOwner.Opponent);
        var self = Card("center", 100, up: 5, right: 5, down: 1, left: 1);
        var state = State(board, [self], [], TripleTriadOwner.Self, TripleTriadRules.Same);

        var result = TripleTriadSolver.ApplyMove(state, new TripleTriadMove("center", 4));

        Assert.True(result.SameTriggered);
        Assert.Equal(TripleTriadOwner.Self, result.State.Board[1]!.Owner);
        Assert.Equal(TripleTriadOwner.Self, result.State.Board[5]!.Owner);
        Assert.Equal(TripleTriadOwner.Self, result.State.Board[2]!.Owner);
        Assert.True(result.ComboCaptures >= 1);
    }

    [Fact]
    public void PlusRule_CapturesEqualSumsAndStartsCombo()
    {
        var board = EmptyBoard();
        board[1] = Placed(Card("up", 201, up: 2, right: 9, down: 4, left: 2), TripleTriadOwner.Opponent);
        board[5] = Placed(Card("right", 202, up: 2, right: 2, down: 2, left: 3), TripleTriadOwner.Opponent);
        board[2] = Placed(Card("combo-target", 203, up: 2, right: 2, down: 2, left: 1), TripleTriadOwner.Opponent);
        var self = Card("center", 100, up: 2, right: 3, down: 1, left: 1);
        var state = State(board, [self], [], TripleTriadOwner.Self, TripleTriadRules.Plus);

        var result = TripleTriadSolver.ApplyMove(state, new TripleTriadMove("center", 4));

        Assert.True(result.PlusTriggered);
        Assert.Equal(TripleTriadOwner.Self, result.State.Board[1]!.Owner);
        Assert.Equal(TripleTriadOwner.Self, result.State.Board[5]!.Owner);
        Assert.Equal(TripleTriadOwner.Self, result.State.Board[2]!.Owner);
        Assert.True(result.ComboCaptures >= 1);
    }

    [Fact]
    public void Ascension_IncreasesTypedCardSidesAndClampsAtAce()
    {
        var board = EmptyBoard();
        board[8] = Placed(Card("typed-existing", 300, 1, 1, 1, 1, typeKey: "Primal"), TripleTriadOwner.Self);
        board[5] = Placed(Card("enemy", 200, 1, 1, 1, 4), TripleTriadOwner.Opponent);
        var self = Card("typed-new", 100, 10, 3, 10, 10, typeKey: "Primal");
        var state = State(board, [self], [], TripleTriadOwner.Self, TripleTriadRules.Ascension);

        var result = TripleTriadSolver.ApplyMove(state, new TripleTriadMove("typed-new", 4));

        Assert.Equal(TripleTriadOwner.Self, result.State.Board[5]!.Owner);
    }

    [Fact]
    public void Descension_DecreasesTypedCardSidesButNeverBelowOne()
    {
        var board = EmptyBoard();
        board[8] = Placed(Card("typed-existing", 300, 1, 1, 1, 1, typeKey: "Primal"), TripleTriadOwner.Self);
        board[5] = Placed(Card("enemy", 200, 1, 1, 1, 5), TripleTriadOwner.Opponent);
        var self = Card("typed-new", 100, 1, 6, 1, 1, typeKey: "Primal");
        var state = State(board, [self], [], TripleTriadOwner.Self, TripleTriadRules.Descension);

        var result = TripleTriadSolver.ApplyMove(state, new TripleTriadMove("typed-new", 4));

        Assert.Equal(TripleTriadOwner.Opponent, result.State.Board[5]!.Owner);
        Assert.Equal(0, result.CapturedCards);
    }

    [Fact]
    public void OrderRule_AllowsOnlyEarliestRemainingDeckCard()
    {
        var first = Card("first", 100, 5, 5, 5, 5, deckOrder: 0);
        var second = Card("second", 101, 6, 6, 6, 6, deckOrder: 1);
        var state = State(EmptyBoard(), [second, first], [], TripleTriadOwner.Self, TripleTriadRules.Order);

        var legal = TripleTriadSolver.GetLegalMoves(state);

        Assert.Equal(9, legal.Count);
        Assert.All(legal, move => Assert.Equal("first", move.CardInstanceId));
        Assert.Throws<InvalidOperationException>(() =>
            TripleTriadSolver.ApplyMove(state, new TripleTriadMove("second", 4)));
    }

    [Fact]
    public void Score_IncludesUnplayedHandCard()
    {
        var board = EmptyBoard();
        board[0] = Placed(Card("s1", 1, 1, 1, 1, 1), TripleTriadOwner.Self);
        board[1] = Placed(Card("s2", 2, 1, 1, 1, 1), TripleTriadOwner.Self);
        board[2] = Placed(Card("s3", 3, 1, 1, 1, 1), TripleTriadOwner.Self);
        board[3] = Placed(Card("s4", 4, 1, 1, 1, 1), TripleTriadOwner.Self);
        board[5] = Placed(Card("o1", 5, 1, 1, 1, 1), TripleTriadOwner.Opponent);
        board[6] = Placed(Card("o2", 6, 1, 1, 1, 1), TripleTriadOwner.Opponent);
        board[7] = Placed(Card("o3", 7, 1, 1, 1, 1), TripleTriadOwner.Opponent);
        board[8] = Placed(Card("o4", 8, 1, 1, 1, 1), TripleTriadOwner.Opponent);
        var handCard = Card("self-hand", 9, 1, 1, 1, 1);
        var state = State(board, [handCard], [], TripleTriadOwner.Self, TripleTriadRules.None);

        var score = TripleTriadSolver.Score(state);

        Assert.Equal(5, score.SelfScore);
        Assert.Equal(4, score.OpponentScore);
    }

    [Fact]
    public void Solver_PrefersImmediateWinningCaptureOverWeakCard()
    {
        var board = EmptyBoard();
        board[1] = Placed(Card("enemy", 200, 4, 4, 4, 4), TripleTriadOwner.Opponent);
        var strong = Card("strong", 100, 10, 10, 10, 10, deckOrder: 0);
        var weak = Card("weak", 101, 1, 1, 1, 1, deckOrder: 1);
        var state = State(board, [strong, weak], [], TripleTriadOwner.Self, TripleTriadRules.None);

        var recommendation = TripleTriadSolver.RecommendMove(
            state,
            new TripleTriadSolverOptions(MaximumSearchDepth: 1, MaximumSearchNodes: 10_000));

        Assert.Equal("strong", recommendation.Move.CardInstanceId);
        Assert.True(recommendation.ImmediateCaptures >= 1);
    }

    [Fact]
    public void State_RejectsAscensionAndDescensionTogether()
    {
        var state = State(
            EmptyBoard(),
            [Card("self", 100, 1, 1, 1, 1)],
            [],
            TripleTriadOwner.Self,
            TripleTriadRules.Ascension | TripleTriadRules.Descension);

        Assert.Throws<InvalidDataException>(state.Validate);
    }

    private static TripleTriadGameState State(
        IReadOnlyList<TripleTriadPlacedCard?> board,
        IReadOnlyList<TripleTriadCardInstance> selfHand,
        IReadOnlyList<TripleTriadCardInstance> opponentHand,
        TripleTriadOwner turn,
        TripleTriadRules rules)
        => new(board, selfHand, opponentHand, turn, rules);

    private static TripleTriadPlacedCard?[] EmptyBoard()
        => new TripleTriadPlacedCard?[9];

    private static TripleTriadPlacedCard Placed(TripleTriadCardInstance card, TripleTriadOwner owner)
        => new(card, owner);

    private static TripleTriadCardInstance Card(
        string instanceId,
        uint cardId,
        int up,
        int right,
        int down,
        int left,
        int deckOrder = 0,
        string? typeKey = null)
        => new(instanceId, cardId, instanceId, up, right, down, left, deckOrder, typeKey);
}
