using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Plugins.Alg;
using GameLib;
using GameLib.Random;
using UnityEngine;
using UnityEngine.Assertions;
using Object = UnityEngine.Object;
using Range = GameLib.Random.Range;

public class Grid : MonoBehaviour
{
    public class BaseCellValue
    {
        public enum PrimitiveType
        {
            Platform = 0,
            Tower = 1,
        }

        public BaseCellValue()
        {
            Connections = new BaseCellValue[4];
            ConnectionInstances = new GameObject[4];
            Primitive = PrimitiveType.Platform;
            Scale = 1.0f;
            Color = Color.white;
            Prefab = null;
            Rotation = Quaternion.Euler(new Vector3(-90, 0, 0));
        }

        public PrimitiveType Primitive;
        public GameObject Prefab;
        public float Scale;
        public float Elevation;
        public Color Color;
        public Quaternion Rotation;

        public GameObject PrimitiveGameObject;
        public BaseCellValue[] Connections;
        public GameObject[] ConnectionInstances;

        public static int Dir2Index(Direction2D.RelativeDirection dir)
        {
            Assert.IsTrue(dir == Direction2D.RelativeDirection.Left || dir == Direction2D.RelativeDirection.Right || dir == Direction2D.RelativeDirection.Up || dir == Direction2D.RelativeDirection.Down);
            if (dir == Direction2D.RelativeDirection.Left)
                return 0;
            if (dir == Direction2D.RelativeDirection.Up)
                return 1;
            if (dir == Direction2D.RelativeDirection.Right)
                return 2;
            if (dir == Direction2D.RelativeDirection.Down)
                return 3;
            return -1;
        }
    }

    public SpriteRenderer SpriteRenderer;
    [Tooltip("Image grid size in cells")]
    public Vector2Int GridSize;
    public List<GameObject> blockPrefabs;
    public List<GameObject> connectionPrefabs;
    public float CameraOffset;

    public int BlocksCounter { get; private set; }
    public BaseCellValue[,] Cells => _cells;

    private BaseCellValue[,] _cells;


    private void Start()
    {
        AdjustSpriteSize();
        CreateModel();
    }

    private void CreateModel()
    {
        _cells = new BaseCellValue[GridSize.x, GridSize.y];
    }

    public GameObject Set(int x, int y, BaseCellValue cellValue)
    {
        // Check range of cell coordinates
        if (x < 0 || y < 0 || x >= GridSize.x || y >= GridSize.y)
        {
            Debug.LogError($"{x}|{y} is out of GridSize range {GridSize}");
            return null;
        }

        // Delete previous value
        var prevVal = Get(x, y);
        if (prevVal != null)
        {
            Destroy(prevVal.PrimitiveGameObject);
            BlocksCounter--;
            BreakConnection(x, y, x - 1, y);
            BreakConnection(x, y, x, y + 1);
            BreakConnection(x, y, x + 1, y);
            BreakConnection(x, y, x, y - 1);
        }
        _cells[x, y] = cellValue;

        if (cellValue != null)
        {
            Assert.IsNotNull(cellValue.Prefab);
            var prefab = cellValue.Prefab;
            var block = Instantiate(prefab, new Vector3((x + 0.5f) * transform.localScale.x, cellValue.Elevation, (y + 0.5f) * transform.localScale.y) + transform.position, cellValue.Rotation);
            block.GetComponent<Platform>().SetColor(cellValue.Color);
            block.transform.localScale = Vector3.one * cellValue.Scale;
            cellValue.PrimitiveGameObject = block;
            block.name = $"{x}|{y}";
            block.transform.SetParent(gameObject.transform);
            BlocksCounter++;
            return block;
        }
        return null;
    }

    public BaseCellValue Get(int x, int y)
    {
        if (x < 0)
            return null;
        if (y < 0)
            return null;
        if (x >= GridSize.x)
            return null;
        if (y >= GridSize.y)
            return null;
        return _cells[x, y];
    }

    public Vector3 GetWorldCoordinates(int x, int y, bool considerElevation = false)
    {
        return new Vector3(
            (x + 0.5f) * transform.localScale.x,
            considerElevation ? Get(x, y).Elevation : 0f,
            (y + 0.5f) * transform.localScale.y)
               + transform.position;
    }

    public bool Connect(int ax, int ay, int bx, int by, bool lerpColors = true)
    {
        var blockA = Get(ax, ay);
        var blockB = Get(bx, by);

        if (blockB == null || blockA == null) // Can't connect when first or second blocks are empty
            return false;

        Vector2Int offsetA2B = new Vector2Int(bx - ax, by - ay);
        Vector2Int offsetB2A = new Vector2Int(ax - bx, ay - by);
        var validConnection = (offsetA2B.x == 0 && offsetA2B.y == 1) || (offsetA2B.x == 0 && offsetA2B.y == -1)
            || (offsetA2B.x == -1 && offsetA2B.y == 0) || (offsetA2B.x == 1 && offsetA2B.y == 0); // No diagonal connections and non-neighbor connection allowed, as long as self-connection 
        if (!validConnection)
            return false;

        var boundsA = blockA.PrimitiveGameObject.BoundBox();
        var boundsB = blockB.PrimitiveGameObject.BoundBox();

        var pointA = Get(ax, ay).PrimitiveGameObject.transform.position +
                     new Vector3(boundsA.size.x * 0.5f * offsetA2B.x, 0, boundsA.size.z * 0.5f * offsetA2B.y); // Starting point of connection

        var pointB = Get(bx, by).PrimitiveGameObject.transform.position +
                     new Vector3(boundsB.size.x * 0.5f * offsetB2A.x, 0, boundsB.size.z * 0.5f * offsetB2A.y); // Starting point of connection

        // Assign connections 
        var dirA2B = Direction2D.FromVector(offsetA2B);
        var dirB2A = Direction2D.Opposite(dirA2B);
        var rotation = Direction2D.IsHorizontal(dirB2A) ? Quaternion.Euler(-90, 0, 90) : Quaternion.Euler(-90, 0, 0);
        BreakConnection(ax, ay, bx, by); // Break previous connections
        blockA.Connections[BaseCellValue.Dir2Index(dirA2B)] = blockB;
        blockB.Connections[BaseCellValue.Dir2Index(dirB2A)] = blockA;

        // Create visual instance
        var connectionRoot = new GameObject($"Connection{ax}{ay} - {bx}{by}");
        connectionRoot.transform.position = (pointA + pointB) * 0.5f;
        connectionRoot.transform.SetParent(gameObject.transform);

        var distance = MathF.Abs(Direction2D.IsHorizontal(dirB2A) ? pointA.x - pointB.x : pointA.z - pointB.z); // planar distance
        var boundsConnection = connectionPrefabs[0].BoundBox();
        var step = MathF.Min(boundsConnection.size.x, boundsConnection.size.z); // planar step
        var stepsCount = (int)MathF.Abs(distance / step);

        for (int i = 0; i <= stepsCount; ++i)
        {
            var pos = Vector3.Lerp(pointA, pointB, i / (float)stepsCount);
            var connection = Instantiate(connectionPrefabs[0], pos + Vector3.up * 0.05f, rotation);
            connection.name = $"ConnectionSegment{i}";
            connection.transform.SetParent(connectionRoot.transform);
        }

        blockA.ConnectionInstances[BaseCellValue.Dir2Index(dirA2B)] = connectionRoot;
        blockB.ConnectionInstances[BaseCellValue.Dir2Index(dirB2A)] = connectionRoot;

        // Lerp color of connection visual instance based on 2 blocks colors
        if (lerpColors)
        {
            //var mixedColor = Color.Lerp(blockA.Color, blockB.Color, 0.5f);
            //connection.GetComponent<SpriteRenderer>().color = mixedColor;
        }

        return true;
    }

    public void BreakConnection(int ax, int ay, int bx, int by)
    {
        var blockA = Get(ax, ay);
        var blockB = Get(bx, by);
        if (blockA == null || blockB == null)
            return;

        Vector2Int offsetA2B = new Vector2Int(bx - ax, by - ay);
        Assert.IsTrue((offsetA2B.x == 0 && offsetA2B.y == 1) || (offsetA2B.x == 0 && offsetA2B.y == -1)
            || (offsetA2B.x == -1 && offsetA2B.y == 0) || (offsetA2B.x == 1 && offsetA2B.y == 0), "No diagonal connections and non-neighbor connection allowed");

        var dirA2B = Direction2D.FromVector(offsetA2B);
        var dirB2A = Direction2D.Opposite(dirA2B);
        blockA.Connections[BaseCellValue.Dir2Index(dirA2B)] = null;
        if (blockA.ConnectionInstances[BaseCellValue.Dir2Index(dirA2B)] != null)
            Destroy(blockA.ConnectionInstances[BaseCellValue.Dir2Index(dirA2B)]);
        blockA.ConnectionInstances[BaseCellValue.Dir2Index(dirA2B)] = null;
        blockB.Connections[BaseCellValue.Dir2Index(dirB2A)] = null;
        blockB.ConnectionInstances[BaseCellValue.Dir2Index(dirB2A)] = null;
    }

    void AdjustCameraSizeFitSpriteInScreen(SpriteRenderer sprite, Camera cam)
    {
        var spriteWorldHeight = sprite.size.y;
        var spriteWorldWidth = sprite.size.x;

        var screenAspectRatio = Screen.height / (float)Screen.width;
        var cameraHeight = spriteWorldHeight;
        var cameraWidth = spriteWorldHeight / screenAspectRatio;
        cam.orthographicSize = cameraHeight * 0.5f;
        cam.transform.position += Vector3.right * spriteWorldWidth * 0.5f + Vector3.left * CameraOffset;
        cam.transform.position += Vector3.up * cameraHeight * 0.5f;
    }

    private void AdjustSpriteSize()
    {
        SpriteRenderer.size = GridSize;
    }
}

static class ImageGridHelper
{
    public static void Fill(this Grid grid, Grid.BaseCellValue fillValue)
    {
        for (int x = 0; x < grid.GridSize.x; ++x)
            for (int y = 0; y < grid.GridSize.y; ++y)
                grid.Set(x, y, fillValue);
    }

    public static void FillRnd(this Grid grid, List<GameObject> prefabs, IPseudoRandomNumberGenerator rnd)
    {
        int[] angles = { 90, 180, 270 };
        Range elevation = new Range(0.25f, 1f);
        Range scale = new Range(0.5f, 0.75f);

        for (int x = 0; x < grid.GridSize.x; ++x)
            for (int y = 0; y < grid.GridSize.y; ++y)
                grid.Set(x, y, new Grid.BaseCellValue
                {
                    Color = rnd.ColorHSV(),
                    Rotation = Quaternion.Euler(-90, 0, rnd.FromArray(angles)),
                    Scale = rnd.FromRange(scale),
                    Elevation = rnd.FromRange(elevation),
                    Prefab = rnd.FromList(prefabs)
                });
    }


    public static void FillWithTowerBasements(this Grid grid, float density, IPseudoRandomNumberGenerator rnd)
    {
        for (int x = 0; x < grid.GridSize.x; ++x)
            for (int y = 0; y < grid.GridSize.y; ++y)
                if (rnd.ValueFloat() < density)
                    grid.Set(x, y, new Grid.BaseCellValue { Color = Color.black, Primitive = Grid.BaseCellValue.PrimitiveType.Tower });
    }

    public static bool HasAnyConnection(this Grid grid, int x, int y)
    {
        var cellVal = grid.Get(x, y);
        if (cellVal == null)
            return false;
        foreach (var connection in cellVal.Connections)
            if (connection != null)
                return true;
        return false;
    }

    public static bool IsInsideGrid(this Grid imgGrid, int x, int y)
    {
        // Check if the new position is within the grid bounds
        return (x >= 0 && x < imgGrid.GridSize.x && y >= 0 && y < imgGrid.GridSize.y);
    }

    public static bool IsEmptyCell(this Grid imgGrid, int x, int y)
    {
        if (!imgGrid.IsInsideGrid(x, y))
            return false;
        return imgGrid.Get(x, y) == null;
    }


    public static List<(Grid.BaseCellValue, Vector2Int)> GetAll(this Grid imgGrid, Func<Grid.BaseCellValue, bool> Condition)
    {
        List<(Grid.BaseCellValue, Vector2Int)> result = new List<(Grid.BaseCellValue, Vector2Int)>();
        for (int x = 0; x < imgGrid.GridSize.x; ++x)
            for (int y = 0; y < imgGrid.GridSize.y; ++y)
            {
                var val = imgGrid.Get(x, y);
                if (Condition(val))
                    result.Add((val, new Vector2Int(x, y)));
            }

        return result;
    }

    public static List<Vector2Int> GetEmptyCells(this Grid imgGrid)
    {
        return Enumerable.Range(0, imgGrid.Cells.GetLength(0))
            .SelectMany(x => Enumerable.Range(0, imgGrid.Cells.GetLength(1))
                .Where(y => imgGrid.Cells[x, y] != null)
                .Select(y => new Vector2Int(x, y))).ToList();
    }

    // Get all orthogonal neighbors if they are:
    // - Inside the grid
    // - Meet PickupCondition if it's passed
    public static List<(Grid.BaseCellValue, Vector2Int)> GetNeigbours(this Grid imgGrid, int x, int y, bool isReturnEmptyNeighbour = false, Func<Grid.BaseCellValue, bool> PickupCondition = null)
    {
        List<(Grid.BaseCellValue, Vector2Int)> neigbours = new List<(Grid.BaseCellValue, Vector2Int)>(4);
        foreach (var offset in Direction2D.OrthogonalDirections)
        {
            var checkPos = new Vector2Int(x + offset.x, y + offset.y);
            if (!imgGrid.IsInsideGrid(checkPos.x, checkPos.y))
                continue;
            var cell = imgGrid.Get(checkPos.x, checkPos.y);
            if (cell == null)
            {
                if (isReturnEmptyNeighbour)
                    neigbours.Add((cell, checkPos));
                continue;
            }

            if (PickupCondition != null)
            {
                if (PickupCondition(cell))
                    neigbours.Add((cell, checkPos));
            }
            else
            {
                neigbours.Add((cell, checkPos));
            }
        }
        return neigbours;
    }

    public static void VisualConnect(this Grid grid, Grid.BaseCellValue c1, Grid.BaseCellValue c2, Direction2D.RelativeDirection dir1, Direction2D.RelativeDirection dir2)
    {
        Assert.IsNotNull(c1);
        Assert.IsNotNull(c2);
        var blockA = c1;
        var blockB = c2;

        var boundsA = blockA.PrimitiveGameObject.BoundBox();
        var boundsB = blockB.PrimitiveGameObject.BoundBox();

        var offsetA2B = Direction2D.OrthogonalDirections[Grid.BaseCellValue.Dir2Index(dir1)];
        var offsetB2A = Direction2D.OrthogonalDirections[Grid.BaseCellValue.Dir2Index(dir2)];
        var pointA = c1.PrimitiveGameObject.transform.position +
                     new Vector3(boundsA.size.x * 0.5f * offsetA2B.x, 0, boundsA.size.z * 0.5f * offsetA2B.y); // Starting point of connection

        var pointB = c2.PrimitiveGameObject.transform.position +
                     new Vector3(boundsB.size.x * 0.5f * offsetB2A.x, 0, boundsB.size.z * 0.5f * offsetB2A.y); // Starting point of connection

        // Assign connections 
        var dirA2B = dir1;
        var dirB2A = dir2;
        var rotation = Direction2D.IsHorizontal(dirB2A) ? Quaternion.Euler(-90, 0, 90) : Quaternion.Euler(-90, 0, 0);
        
        // Create visual instance
        var connectionRoot = new GameObject($"ConnectionToNextLayer");
        connectionRoot.transform.position = (pointA + pointB) * 0.5f;
        connectionRoot.transform.SetParent(grid.transform);

        var distance = MathF.Abs(Direction2D.IsHorizontal(dirB2A) ? pointA.x - pointB.x : pointA.z - pointB.z); // planar distance
        var boundsConnection = grid.connectionPrefabs[0].BoundBox();
        var step = MathF.Min(boundsConnection.size.x, boundsConnection.size.z); // planar step
        var stepsCount = (int)MathF.Abs(distance / step);

        for (int i = 0; i <= stepsCount; ++i)
        {
            var pos = Vector3.Lerp(pointA, pointB, i / (float)stepsCount);
            var connection = Object.Instantiate(grid.connectionPrefabs[0], pos + Vector3.up * 0.05f, rotation);
            connection.name = $"ConnectionSegment{i}";
            connection.transform.SetParent(connectionRoot.transform);
        }
    }
}