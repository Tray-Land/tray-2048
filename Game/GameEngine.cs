namespace Tray2048.Game;

public enum MoveDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>A tile sliding from one cell to another. Cells are row-major, 0..15.</summary>
public readonly record struct TileSlide(int TileId, int From, int To);

/// <summary>Two tiles that met in <see cref="Cell"/> and became one new tile.</summary>
public readonly record struct TileMerge(int FirstId, int SecondId, int NewId, int Cell, int Value);

/// <summary>A new tile dropped into an empty cell.</summary>
public readonly record struct TileSpawn(int TileId, int Cell, int Value);

public sealed class MoveResult
{
    public static readonly MoveResult None = new([], [], null, 0, false);

    public MoveResult(IReadOnlyList<TileSlide> slides, IReadOnlyList<TileMerge> merges, TileSpawn? spawn, int scoreGained, bool reachedGoal)
    {
        Slides = slides;
        Merges = merges;
        Spawn = spawn;
        ScoreGained = scoreGained;
        ReachedGoal = reachedGoal;
    }

    public bool Moved => Slides.Count > 0 || Merges.Count > 0;

    public IReadOnlyList<TileSlide> Slides { get; }

    public IReadOnlyList<TileMerge> Merges { get; }

    public TileSpawn? Spawn { get; }

    public int ScoreGained { get; }

    /// <summary>True only on the move that first makes the <see cref="GameEngine.Goal"/> tile.</summary>
    public bool ReachedGoal { get; }
}

/// <summary>
/// Classic 2048 rules on a 4×4 board. Every tile has an id so the UI can animate the same tile
/// from cell to cell; a merge retires both source ids and creates a new one.
/// </summary>
public sealed class GameEngine
{
    public const int Size = 4;
    public const int CellCount = Size * Size;
    public const int Goal = 2048;

    private readonly int[] _values = new int[CellCount];
    private readonly int[] _ids = new int[CellCount];
    private readonly Random _random;
    private int _nextId = 1;
    private GameState? _undo;

    public GameEngine(Random? random = null)
    {
        _random = random ?? Random.Shared;
    }

    public int Score { get; private set; }

    public int BestScore { get; private set; }

    /// <summary>The goal tile has been made in this game (the player may keep going).</summary>
    public bool HasWon { get; private set; }

    public bool CanUndo => _undo is not null;

    public bool IsGameOver => !CanMove();

    public int ValueAt(int cell) => _values[cell];

    public int IdAt(int cell) => _ids[cell];

    /// <summary>
    /// Starts over with two tiles. An unfinished game can be brought back with <see cref="Undo"/>,
    /// so a stray click never costs a run.
    /// </summary>
    public IReadOnlyList<TileSpawn> NewGame()
    {
        _undo = Score > 0 && !IsGameOver ? ToState() : null;
        Array.Clear(_values);
        Array.Clear(_ids);
        Score = 0;
        HasWon = false;

        List<TileSpawn> spawns = [];
        for (int i = 0; i < 2; i++)
        {
            if (SpawnTile() is { } spawn)
            {
                spawns.Add(spawn);
            }
        }

        return spawns;
    }

    public MoveResult Move(MoveDirection direction)
    {
        int[] newValues = new int[CellCount];
        int[] newIds = new int[CellCount];
        List<TileSlide> slides = [];
        List<TileMerge> merges = [];
        int gained = 0;
        int nextId = _nextId;
        Span<int> cells = stackalloc int[Size];

        for (int line = 0; line < Size; line++)
        {
            // Cells of this line, starting at the edge the tiles move toward.
            for (int i = 0; i < Size; i++)
            {
                cells[i] = CellInLine(direction, line, i);
            }

            int target = 0;
            bool lastCanMerge = false;
            for (int i = 0; i < Size; i++)
            {
                int cell = cells[i];
                int value = _values[cell];
                if (value == 0)
                {
                    continue;
                }

                int id = _ids[cell];
                if (lastCanMerge && newValues[cells[target - 1]] == value)
                {
                    // Each tile merges at most once per move: 2 2 2 2 → 4 4, not 8.
                    int dest = cells[target - 1];
                    int merged = value * 2;
                    slides.Add(new TileSlide(id, cell, dest));
                    merges.Add(new TileMerge(newIds[dest], id, nextId, dest, merged));
                    newValues[dest] = merged;
                    newIds[dest] = nextId++;
                    gained += merged;
                    lastCanMerge = false;
                }
                else
                {
                    int dest = cells[target];
                    if (dest != cell)
                    {
                        slides.Add(new TileSlide(id, cell, dest));
                    }

                    newValues[dest] = value;
                    newIds[dest] = id;
                    lastCanMerge = true;
                    target++;
                }
            }
        }

        if (slides.Count == 0 && merges.Count == 0)
        {
            return MoveResult.None;
        }

        _undo = ToState();
        newValues.CopyTo(_values, 0);
        newIds.CopyTo(_ids, 0);
        _nextId = nextId;
        Score += gained;
        BestScore = Math.Max(BestScore, Score);

        bool reachedGoal = !HasWon && merges.Any(m => m.Value >= Goal);
        HasWon |= reachedGoal;

        return new MoveResult(slides, merges, SpawnTile(), gained, reachedGoal);
    }

    /// <summary>Steps back one move (or one "New game"). Ids are reissued, so redraw the board.</summary>
    public bool Undo()
    {
        if (_undo is not { } previous)
        {
            return false;
        }

        _undo = null;
        int best = BestScore;
        Load(previous);
        BestScore = best;
        return true;
    }

    public void ResetBestScore() => BestScore = Score;

    public GameState ToState() => new([.. _values], Score, BestScore, HasWon);

    /// <summary>Restores a saved game; anything malformed starts a fresh one instead.</summary>
    public void Load(GameState? state)
    {
        if (state is null || !IsValid(state))
        {
            BestScore = Math.Max(BestScore, state?.BestScore ?? 0);
            NewGame();
            _undo = null;
            return;
        }

        for (int cell = 0; cell < CellCount; cell++)
        {
            _values[cell] = state.Cells[cell];
            _ids[cell] = state.Cells[cell] == 0 ? 0 : _nextId++;
        }

        Score = state.Score;
        BestScore = Math.Max(state.BestScore, state.Score);
        HasWon = state.HasWon;
    }

    private static bool IsValid(GameState state) =>
        state.Cells is { Length: CellCount } cells
        && state.Score >= 0
        && cells.All(v => v == 0 || (v >= 2 && (v & (v - 1)) == 0))
        && cells.Any(v => v != 0);

    private static int CellInLine(MoveDirection direction, int line, int index) => direction switch
    {
        MoveDirection.Left => (line * Size) + index,
        MoveDirection.Right => (line * Size) + (Size - 1 - index),
        MoveDirection.Up => (index * Size) + line,
        _ => ((Size - 1 - index) * Size) + line,
    };

    private TileSpawn? SpawnTile()
    {
        Span<int> empty = stackalloc int[CellCount];
        int count = 0;
        for (int cell = 0; cell < CellCount; cell++)
        {
            if (_values[cell] == 0)
            {
                empty[count++] = cell;
            }
        }

        if (count == 0)
        {
            return null;
        }

        int chosen = empty[_random.Next(count)];
        int value = _random.NextDouble() < 0.9 ? 2 : 4;
        _values[chosen] = value;
        _ids[chosen] = _nextId++;
        return new TileSpawn(_ids[chosen], chosen, value);
    }

    private bool CanMove()
    {
        for (int cell = 0; cell < CellCount; cell++)
        {
            int value = _values[cell];
            if (value == 0)
            {
                return true;
            }

            bool rightMatches = cell % Size < Size - 1 && _values[cell + 1] == value;
            bool downMatches = cell + Size < CellCount && _values[cell + Size] == value;
            if (rightMatches || downMatches)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>A saved game: the 16 cell values (0 = empty), row-major.</summary>
public sealed record GameState(int[] Cells, int Score, int BestScore, bool HasWon);
