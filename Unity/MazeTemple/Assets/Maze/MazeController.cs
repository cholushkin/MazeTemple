using System.Collections.Generic;
using Assets.Plugins.Alg;
using GameLib;
using GameLib.Random;
using UnityEngine;

[ScriptExecutionOrderDependsOn(typeof(MazeLayer))]
public class MazeController : MonoBehaviour
{
    public long Seed;
    public bool PropagateSeed;
    public Range TowerHeight;

    [Range(0f, 1f)]
    public float PeoplePercent;

    public MazeLayer[] Layers;
    public int MaxTowersNumber;
    public int[] MaxColumnsPerLayer;

    public GameObject[] TowerChunkPrefabs;
    public GameObject[] PeoplePrefabs;

    private int[,] _raycasts;
    private Vector2Int[,] _offsets;
    private IPseudoRandomNumberGenerator _rnd;

    void Awake()
    {
        _rnd = RandomHelper.CreateRandomNumberGenerator(Seed);
        Seed = _rnd.GetState().AsNumber();
        if (PropagateSeed)
            for (int i = 0; i < Layers.Length; i++)
                Layers[i].Seed = Seed + i + 1;
    }

    void Start()
    {
        GetRaycasts();
        GenerateConnectionsBetweenLayers();
        SpawnTowers();
        SpawnColumns();
        SpawnPeople();
    }

    private void GenerateConnectionsBetweenLayers()
    {
        for (int i = 0; i < Layers.Length - 1; ++i)
        {
            var w = Layers[i].Grid.GridSize.x;
            var h = Layers[i].Grid.GridSize.y;
            var minDistance = float.MaxValue;
            Direction2D.RelativeDirection d1 = Direction2D.RelativeDirection.Center, d2 = Direction2D.RelativeDirection.Center;
            Grid.BaseCellValue c1 = null, c2 = null;

            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    var cell = Layers[i].Grid.Get(x, y);
                    if (cell == null)
                        continue;

                    for (int c = 0; c < 4; c++)
                    {
                        if (cell.Connections[c] == null) // if the cell has vacant connection
                        {
                            // Take the neigbour from above to this direction
                            var dir = Direction2D.OrthogonalDirections[c];
                            var n = Layers[i + 1].Grid.Get(x + dir.x, y + dir.y);

                            if (n != null)
                            {
                                // If neighbour has vacant connection
                                var nConnection = n.Connections[Grid.BaseCellValue.Dir2Index(Direction2D.FromVector(-dir))];
                                if (nConnection == null)
                                {
                                    var pos1 = Layers[i].Grid.GetWorldCoordinates(x, y, true);
                                    var pos2 = Layers[i + 1].Grid.GetWorldCoordinates(x + dir.x, y + dir.y, true);
                                    var distance = Vector3.Distance(pos1, pos2);
                                    if (distance < minDistance)
                                    {
                                        minDistance = distance;
                                        d1 = Direction2D.FromVector(dir);
                                        d2 = Direction2D.FromVector(-dir);
                                        c1 = cell;
                                        c2 = n;
                                    }
                                }
                            }
                        }
                    }
                }
            if (minDistance != float.MaxValue)
                Layers[i].Grid.VisualConnect(c1, c2, d1, d2);
            else
            {
                Debug.LogWarning($"Can't connect layer {i} to {i+1}");
            }
        }
    }

    private void SpawnColumns()
    {
        List<Vector2Int> CollectSpots(int layer)
        {
            var w = Layers[layer].Grid.GridSize.x;
            var h = Layers[layer].Grid.GridSize.y;

            List<Vector2Int> availableSpotForColumns = new List<Vector2Int>();
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    if (_raycasts[x, y] == layer)
                        availableSpotForColumns.Add(new Vector2Int(x, y));
                }
            _rnd.ShuffleInplace(availableSpotForColumns);
            return availableSpotForColumns;
        }


        for (int i = 0; i < Layers.Length; ++i)
        {
            var spots = CollectSpots(i);
            var spawnedColums = 0;
            foreach (var spot in spots)
            {
                SpawnColumn(Layers[i].Grid.GetWorldCoordinates(spot.x, spot.y, true) + Vector3.down * 0.1f);
                spawnedColums++;
                if (spawnedColums >= MaxColumnsPerLayer[i])
                    break;
            }
        }
    }

    private void SpawnPeople()
    {
        var spawners = GameObject.FindGameObjectsWithTag("Respawn");
        _rnd.ShuffleInplace(spawners);

        for (int i = 0; i < spawners.Length * PeoplePercent; ++i)
        {
            var person = Instantiate(_rnd.FromArray(PeoplePrefabs), spawners[i].transform.position,
                Quaternion.Euler(-90, 0, _rnd.Range(0f, 360f)));
            person.transform.SetParent(spawners[i].transform);
            person.transform.localScale = Vector3.one;
        }
    }

    private void SpawnColumn(Vector3 worldCoordinates)
    {
        var spawnPoint = worldCoordinates;

        var hitTheGround = false;
        while (!hitTheGround)
        {
            var prefab = _rnd.FromArray(TowerChunkPrefabs);
            var chunk = Instantiate(prefab, spawnPoint, Quaternion.Euler(-90, 0, _rnd.FromArray(angles)));
            chunk.transform.localScale *= _rnd.Range(0.25f, 0.45f);
            chunk.transform.SetParent(gameObject.transform);
            var aabb = chunk.BoundBox();
            spawnPoint.y -= aabb.size.y;
            hitTheGround = spawnPoint.y < 0f;
        }
    }

    private void SpawnTowers()
    {
        // Get all full columns coordinates
        List<Vector2Int> fullColumns = new List<Vector2Int>();
        var w = Layers[0].Grid.GridSize.x;
        var h = Layers[0].Grid.GridSize.y;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (_raycasts[x, y] == Layers.Length)
                    fullColumns.Add(new Vector2Int(x, y));

        _rnd.ShuffleInplace(fullColumns);

        int towersCounter = 0;
        foreach (var fullColumn in fullColumns)
        {
            SpawnTower(fullColumn);
            towersCounter++;
            if (towersCounter == MaxTowersNumber)
                break;
        }
    }

    static readonly int[] angles = { 90, 180, 270 };
    private void SpawnTower(Vector2Int fullColumn)
    {
        var spawnPoint = Layers[Layers.Length - 1].Grid.GetWorldCoordinates(fullColumn.x, fullColumn.y);
        var height = _rnd.FromRange(TowerHeight);
        spawnPoint.y = height;


        var hitTheGround = false;
        while (!hitTheGround)
        {
            var prefab = _rnd.FromArray(TowerChunkPrefabs);
            var chunk = Instantiate(prefab, spawnPoint, Quaternion.Euler(-90, 0, _rnd.FromArray(angles)));
            chunk.transform.localScale *= _rnd.Range(0.5f, 0.7f);
            var aabb = chunk.BoundBox();
            chunk.transform.SetParent(gameObject.transform);
            spawnPoint.y -= aabb.size.y;
            hitTheGround = spawnPoint.y < 0f;
        }
    }

    private void GetRaycasts()
    {
        _raycasts = new int[Layers[0].Grid.GridSize.x, Layers[0].Grid.GridSize.y];
        var w = Layers[0].Grid.GridSize.x;
        var h = Layers[0].Grid.GridSize.y;

        int rayCounter = 0;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                rayCounter = 0;
                foreach (var mazeLayer in Layers)
                    if (mazeLayer.Grid.Get(x, y) == null)
                        rayCounter++;
                    else
                        break;
                _raycasts[x, y] = rayCounter;
            }
    }
}
