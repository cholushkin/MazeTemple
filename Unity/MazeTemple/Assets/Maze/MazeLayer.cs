using System;
using System.Collections.Generic;
using GameLib;
using GameLib.Random;
using UnityEngine;
using UnityEngine.Assertions;
using WaveProcessor;
using Range = GameLib.Random.Range;
using RangeInt = GameLib.Random.RangeInt;


[ScriptExecutionOrderDependsOn(typeof(Grid))]
public class MazeLayer : MonoBehaviour
{
    [Serializable]
    public class SuccessConditions
    {
        public RangeInt Steps;
        public float FillPercent;
    }

    enum State
    {
        Success,
        Fail,
        Processing
    }

    public class CellValue : Grid.BaseCellValue
    {
        public bool[] Directions = { false, false, false, false };
    }

    

    public Grid Grid;

    public long Seed;
    public Range PlatformElevation;
    public bool RestartOnSuccess;
    public SuccessConditions SuccessCond;

    private IPseudoRandomNumberGenerator _rnd;
    private SimpleWaveProcessor<Grid.BaseCellValue> _waveProcessor;
    private int _step;
    private State _state;

    private void Start()
    {
        _rnd = RandomHelper.CreateRandomNumberGenerator(Seed);
        Seed = _rnd.GetState().AsNumber();
        Generate();
    }

    private static bool WallFunction(Grid.BaseCellValue val)
    {
        return (val != null);
    }

    private void InstanceRnd(Vector2Int pointer, List<GameObject> prefabs, Range elevations, IPseudoRandomNumberGenerator rnd)
    {
        int[] angles = { 90, 180, 270 };
        Range scale = new Range(0.5f, 0.75f);

        Grid.Set(pointer.x, pointer.y, new CellValue
        {
            Color = rnd.ColorHSV(),
            Rotation = Quaternion.Euler(-90, 0, rnd.FromArray(angles)),
            Scale = rnd.FromRange(scale),
            Elevation = rnd.FromRange(elevations),
            Prefab = rnd.FromList(prefabs)
        });
    }

    private void Generate()
    {
        _state = State.Processing;
        _waveProcessor = new SimpleWaveProcessor<Grid.BaseCellValue>(Grid.Cells, WallFunction);

        // Set the initial position to the random position
        Vector2Int prevPointer = Vector2Int.zero;
        Vector2Int pointer = new Vector2Int(
            _rnd.Range(0, Grid.GridSize.x),
            _rnd.Range(0, Grid.GridSize.y));

        Debug.Log($">>>>> Generating maze layer {gameObject.name}. Starting point : {pointer}");
        Debug.Log($"Seed:{Seed};Grid:{Grid.GridSize.x}*{Grid.GridSize.y};Step:{_step}");

        // Set a block at the initial position
        InstanceRnd(pointer, Grid.blockPrefabs, new Range(0.1f,0.1f), _rnd);
        ++_step;

        while (_state == State.Processing)
        {
            if (SuccessCond.Steps.To != -1 && (_step >= SuccessCond.Steps.To))
                break;

            // Get a random direction to a list of available directions
            List<int> availableDirections = new List<int>(4);
            for (int i = 0; i < 4; ++i)
                if (IsValidMove(pointer + Direction2D.OrthogonalDirections[i]) && (Grid.Get(pointer.x, pointer.y) as CellValue).Directions[i] == false)
                    availableDirections.Add(i);

            // Propagate wave
            if (availableDirections.Count > 1 && HasSplit(pointer, availableDirections))
            {
                // Roll back
                var connections = Grid.Get(pointer.x, pointer.y).Connections;
                Vector2Int offset = Vector2Int.zero;
                for (int i = 0; i < connections.Length; ++i)
                    if (connections[i] != null)
                    {
                        offset = Direction2D.OrthogonalDirections[i];
                        break;
                    }

                // Delete self
                Grid.Set(pointer.x, pointer.y, null);

                // Assign pointer to prev
                pointer += offset;
                ++_step;
            }
            else
            {
                if (availableDirections.Count == 0)
                {
                    // Check finish condition
                    if (Grid.BlocksCounter == Grid.GridSize.x * Grid.GridSize.y || Grid.BlocksCounter == Grid.GridSize.x * Grid.GridSize.y - 1)
                    {
                        _state = State.Success;
                        continue;
                    }
                    // Roll back
                    var connections = Grid.Get(pointer.x, pointer.y).Connections;
                    Vector2Int offset = Vector2Int.zero;
                    for (int i = 0; i < connections.Length; ++i)
                        if (connections[i] != null)
                        {
                            offset = Direction2D.OrthogonalDirections[i];
                            break;
                        }

                    // Delete self
                    Grid.Set(pointer.x, pointer.y, null);

                    // Assign pointer to prev
                    pointer += offset;
                    ++_step;
                }
                else
                {
                    // Choose one of available direction and mark it as used
                    var dirIndex = _rnd.FromList(availableDirections);
                    var moveDirection = Direction2D.OrthogonalDirections[dirIndex];
                    (Grid.Get(pointer.x, pointer.y) as CellValue).Directions[dirIndex] = true;

                    prevPointer = pointer;
                    pointer += moveDirection;

                    // Set a block at the new position
                    InstanceRnd(pointer, Grid.blockPrefabs, PlatformElevation, _rnd);
                    Grid.Connect(pointer.x, pointer.y, prevPointer.x, prevPointer.y);
                    ++_step;
                }
            }
        }
    }

    private bool HasSplit(Vector2Int pointer, List<int> directions)
    {
        Assert.IsTrue(directions.Count > 1);

        List<Vector2Int> availableDirections = new List<Vector2Int>(4);
        foreach (var orthogonalDirection in Direction2D.OrthogonalDirections)
        {
            var dir = pointer + orthogonalDirection;
            if (IsValidMove(dir))
                availableDirections.Add(dir);
        }

        _waveProcessor.Clear();
        int waveIndex = 0;
        int contacts = availableDirections.Count - 1;
        foreach (var wave in _waveProcessor.ComputeWaves(availableDirections[0]))
        {
            _step += wave.Cells.Count;
            if (waveIndex++ == 0)
                continue;
            foreach (var cell in wave.Cells)
            {
                if (cell == availableDirections[1])
                    contacts--;
                if (availableDirections.Count >= 3 && cell == availableDirections[2])
                    contacts--;
                if (availableDirections.Count >= 4 && cell == availableDirections[3]) // only for the first cell in the path
                    contacts--;
            }
            if (contacts == 0)
                return false;
        }
        return true;
    }

    private bool IsValidMove(Vector2Int move)
    {
        // Check if the new position is within the grid bounds
        if (move.x < 0 || move.x >= Grid.GridSize.x || move.y < 0 || move.y >= Grid.GridSize.y)
            return false;
        return Grid.Get(move.x, move.y) == null;
    }
}
