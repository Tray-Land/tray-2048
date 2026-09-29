using Tray2048.Game;
using Xunit;

namespace Tray2048.Tests;

public class GameEngineTests
{
    private static GameEngine EngineWith(params int[] cells)
    {
        GameEngine engine = new(new Random(42));
        engine.Load(new GameState(cells, 0, 0, false));
        return engine;
    }

    private static int[] Row(GameEngine engine, int row) =>
        [.. Enumerable.Range(row * 4, 4).Select(engine.ValueAt)];

    [Fact]
    public void NewGame_StartsWithTwoTiles()
    {
        GameEngine engine = new(new Random(1));
        IReadOnlyList<TileSpawn> spawns = engine.NewGame();

        Assert.Equal(2, spawns.Count);
        Assert.Equal(2, Enumerable.Range(0, 16).Count(c => engine.ValueAt(c) != 0));
        Assert.All(spawns, s => Assert.Contains(s.Value, new[] { 2, 4 }));
    }

    [Fact]
    public void Move_Left_MergesPairOnce()
    {
        GameEngine engine = EngineWith(
            2, 2, 2, 2,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0);

        MoveResult result = engine.Move(MoveDirection.Left);

        Assert.True(result.Moved);
        Assert.Equal(new[] { 4, 4 }, Row(engine, 0)[..2]);
        Assert.Equal(8, result.ScoreGained);
        Assert.Equal(2, result.Merges.Count);
    }

    [Fact]
    public void Move_Right_MergesFromTheLeadingEdge()
    {
        GameEngine engine = EngineWith(
            4, 2, 2, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0);

        engine.Move(MoveDirection.Right);

        Assert.Equal(new[] { 4, 4 }, Row(engine, 0)[2..]);
    }

    [Fact]
    public void Move_DoesNotMergeANewlyMergedTile()
    {
        GameEngine engine = EngineWith(
            4, 2, 2, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0);

        engine.Move(MoveDirection.Left);

        Assert.Equal(new[] { 4, 4 }, Row(engine, 0)[..2]);
    }

    [Fact]
    public void Move_Up_SlidesColumns()
    {
        GameEngine engine = EngineWith(
            0, 0, 0, 0,
            2, 0, 0, 0,
            0, 0, 0, 0,
            2, 0, 0, 8);

        MoveResult result = engine.Move(MoveDirection.Up);

        Assert.Equal(4, engine.ValueAt(0));
        Assert.Equal(8, engine.ValueAt(3));
        Assert.Contains(result.Slides, s => s.From == 15 && s.To == 3);
    }

    [Fact]
    public void Move_ThatChangesNothing_DoesNotSpawnOrRecordUndo()
    {
        GameEngine engine = EngineWith(
            2, 4, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0);

        MoveResult result = engine.Move(MoveDirection.Left);

        Assert.False(result.Moved);
        Assert.Null(result.Spawn);
        Assert.False(engine.CanUndo);
    }

    [Fact]
    public void Move_SpawnsIntoAnEmptyCell()
    {
        GameEngine engine = EngineWith(
            0, 0, 0, 2,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0);

        MoveResult result = engine.Move(MoveDirection.Left);

        Assert.NotNull(result.Spawn);
        Assert.NotEqual(0, result.Spawn!.Value.Cell);
        Assert.Equal(result.Spawn.Value.Value, engine.ValueAt(result.Spawn.Value.Cell));
    }

    [Fact]
    public void Merge_ReportsBothSourcesAndTheNewTile()
    {
        GameEngine engine = EngineWith(
            2, 0, 0, 2,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0);
        int left = engine.IdAt(0);
        int right = engine.IdAt(3);

        TileMerge merge = Assert.Single(engine.Move(MoveDirection.Left).Merges);

        Assert.Equal(left, merge.FirstId);
        Assert.Equal(right, merge.SecondId);
        Assert.Equal(merge.NewId, engine.IdAt(0));
        Assert.Equal(4, merge.Value);
    }

    [Fact]
    public void ReachingGoal_IsReportedOnce()
    {
        GameEngine engine = EngineWith(
            1024, 1024, 0, 0,
            1024, 1024, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0);

        MoveResult first = engine.Move(MoveDirection.Left);
        Assert.True(first.ReachedGoal);
        Assert.True(engine.HasWon);

        MoveResult second = engine.Move(MoveDirection.Up);
        Assert.False(second.ReachedGoal);
    }

    [Fact]
    public void Undo_RestoresBoardAndScore_KeepsBest()
    {
        GameEngine engine = EngineWith(
            2, 2, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0);

        engine.Move(MoveDirection.Left);
        Assert.Equal(4, engine.BestScore);

        Assert.True(engine.Undo());
        Assert.Equal(new[] { 2, 2, 0, 0 }, Row(engine, 0));
        Assert.Equal(0, engine.Score);
        Assert.Equal(4, engine.BestScore);
        Assert.False(engine.CanUndo);
    }

    [Fact]
    public void NewGame_CanBeUndone()
    {
        GameEngine engine = EngineWith(
            2, 2, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0);
        engine.Move(MoveDirection.Left);
        int[] before = [.. Enumerable.Range(0, 16).Select(engine.ValueAt)];

        engine.NewGame();
        Assert.Equal(0, engine.Score);

        Assert.True(engine.Undo());
        Assert.Equal(4, engine.Score);
        Assert.Equal(before, Enumerable.Range(0, 16).Select(engine.ValueAt));
    }

    [Fact]
    public void IsGameOver_WhenFullAndNoNeighborsMatch()
    {
        GameEngine engine = EngineWith(
            2, 4, 2, 4,
            4, 2, 4, 2,
            2, 4, 2, 4,
            4, 2, 4, 2);

        Assert.True(engine.IsGameOver);
    }

    [Fact]
    public void IsNotGameOver_WhenFullButAMergeRemains()
    {
        GameEngine engine = EngineWith(
            2, 4, 2, 4,
            4, 2, 4, 2,
            2, 4, 2, 4,
            4, 2, 4, 4);

        Assert.False(engine.IsGameOver);
    }

    [Theory]
    [InlineData(new[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new[] { 2, 2 })]
    [InlineData(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
    public void Load_RejectsBadStates_AndStartsFresh(int[] cells)
    {
        GameEngine engine = new(new Random(3));
        engine.Load(new GameState(cells, 10, 50, false));

        Assert.Equal(0, engine.Score);
        Assert.Equal(50, engine.BestScore);
        Assert.Equal(2, Enumerable.Range(0, 16).Count(c => engine.ValueAt(c) != 0));
    }

    [Fact]
    public void State_RoundTrips()
    {
        GameEngine engine = EngineWith(
            2, 4, 8, 16,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 2048);
        GameState state = engine.ToState() with { Score = 120, BestScore = 300, HasWon = true };

        GameEngine restored = new();
        restored.Load(state);

        Assert.Equal(state.Cells, restored.ToState().Cells);
        Assert.Equal(120, restored.Score);
        Assert.Equal(300, restored.BestScore);
        Assert.True(restored.HasWon);
    }
}
