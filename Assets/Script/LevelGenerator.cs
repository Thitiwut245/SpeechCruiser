using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Constraint-based procedural level generator.
///
/// Designer contract: drop prefabs into the slots below and press play. Nothing
/// about a prefab has to be prepared first — the generator measures each one's
/// rendered bounds and then fits, anchors, tags and collides it itself. Pivot at
/// the base, pivot at the centre, authored at scale 1 or scale 50, mesh offset
/// from its transform, no collider, wrong tag: all handled.
///
/// The two guarantees it makes:
///   * Every level is completable. A* validates the grid before anything spawns.
///   * Every gap the path runs through is at least MinChannelWidth of clear
///     water, whatever size the hazard art happens to be.
/// </summary>
public class LevelGenerator : MonoBehaviour
{
    private const string HazardTag = "Hazard";

    /// <summary>Where a fitted prefab's measured bounds should sit vertically.</summary>
    private enum Anchor
    {
        Center,
        Top,
    }

    [Header("Prefabs — drop them in, the generator measures and fits them")]
    [Tooltip("Water surface. Stretched to cover the whole level. Leave empty to keep whatever water is already in the scene.")]
    [SerializeField] private GameObject WaterPrefab;

    [Tooltip("Sea floor. Spawned once and stretched under the level; its top surface is placed at SeaFloorDepth.")]
    [SerializeField] private GameObject SeaFloorPrefab;

    [Tooltip("Dock the ship departs from. Placed behind the spawn point so it never overlaps the hull.")]
    [SerializeField] private GameObject StartPortPrefab;

    [Tooltip("Destination. Stretched into a gate across the full map width and forced to a trigger.")]
    [SerializeField] private GameObject EndPortPrefab;

    [Tooltip("Obstacle art. Any size, any pivot, collider and Hazard tag optional — all added if missing.")]
    [SerializeField] private GameObject[] HazardPrefabs;

    [Header("Grid")]
    [Tooltip("World units per grid cell. Also the spacing of the navigable lattice.")]
    [SerializeField] private float CellSize = 25f;

    [Tooltip("Cells across (X). An odd number gives a symmetric map with a true centre lane.")]
    [SerializeField] private int GridWidth = 15;

    [Tooltip("Cells long (Z) at difficulty 1.")]
    [SerializeField] private int BaseGridLength = 30;

    [Tooltip("Extra cells of length per difficulty level above 1.")]
    [SerializeField] private int CellsPerDifficulty = 5;

    [Header("Navigation Guarantee")]
    [Tooltip("Clear water guaranteed through any gap, in world units. The hull is ~6 units wide; " +
             "hazard size and scatter are both capped to protect this no matter how big the art is.")]
    [SerializeField] private float MinChannelWidth = 14f;

    [Tooltip("How much of a cell a hazard tries to fill, before the channel cap is applied.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float HazardCellFill = 0.7f;

    [Tooltip("Random size variation between hazards.")]
    [SerializeField] private Vector2 HazardScaleJitter = new Vector2(0.8f, 1.2f);

    [Tooltip("Random offset from the cell centre, as a fraction of CellSize, so hazards do not sit on a visible lattice.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float HazardPositionJitter = 0.3f;

    [Tooltip("Fraction of a hazard's height below the waterline. 0.5 = half submerged.")]
    [Range(0f, 1f)]
    [SerializeField] private float HazardSubmersion = 0.55f;

    [Tooltip("Shortest a hazard may end up. Long, flat art fitted to the cell width would otherwise scale down " +
             "to something paper-thin the player cannot see. Height costs nothing in channel width, so it is " +
             "stretched independently.")]
    [SerializeField] private float MinHazardHeight = 6f;

    [Header("Environment")]
    [Tooltip("Depth of the sea floor's top surface below the water. This is NOT cosmetic: the stylized water is " +
             "opaque and takes its colour from the depth of whatever sits behind it, fading ShallowColor to " +
             "DeepColor over _DepthFadeDistance (10 on M_StylizedOceanWater). Deeper than that and the ocean " +
             "renders at DeepColor, which is pure black. Shallower than _IntersectionFoamDepth (2.43) and the " +
             "whole surface turns to foam. Keep it between those two.")]
    [SerializeField] private float SeaFloorDepth = 6f;

    [Tooltip("Extra world units of water and sea floor beyond the grid edge, so the horizon never shows a seam.")]
    [SerializeField] private float EnvironmentMargin = 150f;

    [Tooltip("Height of the finish-line trigger volume. Just needs to comfortably contain the hull.")]
    [SerializeField] private float EndGateHeight = 30f;

    [Tooltip("Height the ship floats at. The generator owns this because it owns where the course starts.")]
    [SerializeField] private float ShipSpawnHeight = 1f;

    [Header("Difficulty")]
    [Range(0.1f, 0.4f)]
    [Tooltip("Base density of obstacles. Recommended 0.2 to 0.35.")]
    [SerializeField] private float ObstacleDensity = 0.25f;

    [Tooltip("Attempts at a valid layout before falling back to open sea.")]
    [SerializeField] private int MaxRetries = 10;

    [Tooltip("Fixed seed for reproducible levels. Leave at -1 for a different level every mission.")]
    [SerializeField] private int Seed = -1;

    private Transform _levelRoot;
    private int _currentDifficulty = 1;
    private bool _hazardTagMissing;
    private bool[,] _obstacleMap;
    private Vector2 _gridOrigin;

    public static LevelGenerator Instance { get; private set; }

    /// <summary>Raised once a level is built and its map data is queryable.</summary>
    public event System.Action OnLevelGenerated;

    public int GridColumns { get; private set; }
    public int GridRows { get; private set; }
    public float CellWorldSize => CellSize;
    public Vector2Int StartCell { get; private set; }
    public Vector2Int EndCell { get; private set; }

    /// <summary>Where the ship should be placed. Valid after GenerateLevel.</summary>
    public Vector3 StartPosition { get; private set; }

    /// <summary>Which way the ship should face — down the length of the course.</summary>
    public Quaternion StartRotation => Quaternion.identity;

    /// <summary>Centre of the finish gate. Valid after GenerateLevel.</summary>
    public Vector3 EndPosition { get; private set; }

    public bool IsLevelReady => _levelRoot != null;

    // Node structure for A* Pathfinding
    private class Node
    {
        public int X;
        public int Y;
        public bool IsObstacle;
        public int GCost;
        public int HCost;
        public Node Parent;
        public int FCost => GCost + HCost;

        public Node(int x, int y, bool isObstacle)
        {
            X = x;
            Y = y;
            IsObstacle = isObstacle;
        }
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnValidate()
    {
        GridWidth = Mathf.Max(3, GridWidth);
        BaseGridLength = Mathf.Max(5, BaseGridLength);
        CellsPerDifficulty = Mathf.Max(0, CellsPerDifficulty);
        CellSize = Mathf.Max(1f, CellSize);
        MinChannelWidth = Mathf.Clamp(MinChannelWidth, 1f, CellSize * 0.9f);
        HazardScaleJitter.x = Mathf.Max(0.05f, HazardScaleJitter.x);
        HazardScaleJitter.y = Mathf.Max(HazardScaleJitter.x, HazardScaleJitter.y);
        MaxRetries = Mathf.Max(1, MaxRetries);
    }

    [ContextMenu("Regenerate Now")]
    public void GenerateLevel()
    {
        if (!HasRequiredPrefabs()) return;

        if (Seed >= 0) Random.InitState(Seed);

        if (GameManager.Instance != null)
        {
            _currentDifficulty = 1 + (GameManager.Instance.Credits / 1000);
        }

        ClearLevel();

        int gridLength = BaseGridLength + ((_currentDifficulty - 1) * CellsPerDifficulty);
        Node[,] finalGrid = null;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            Node[,] grid = CreateAndScatterGrid(GridWidth, gridLength);

            Node startNode = grid[GridWidth / 2, 0];
            Node endNode = grid[GridWidth / 2, gridLength - 1];

            List<Node> path = FindPathAStar(grid, startNode, endNode);
            if (path == null) continue;

            // RetracePath excludes the start node, so a dead-straight run up the
            // middle is (gridLength - 1) nodes. The 1.3x weaving requirement is
            // measured against that, not against gridLength.
            if (path.Count < (gridLength - 1) * 1.3f) continue;

            finalGrid = grid;
            Debug.Log($"🌊 [LEVEL GEN]: difficulty {_currentDifficulty}, grid {GridWidth}x{gridLength}, " +
                      $"valid on attempt {attempt}, path {path.Count} cells.");
            break;
        }

        if (finalGrid == null)
        {
            // Open water beats a broken level: same ocean, same ports, no hazards.
            Debug.LogWarning($"⚠️ [LEVEL GEN]: no valid layout in {MaxRetries} attempts — falling back to open sea. " +
                             "Lower ObstacleDensity or widen the grid.");
            finalGrid = CreateEmptyGrid(GridWidth, gridLength);
        }

        Build(finalGrid, gridLength);
    }

    public void ClearLevel()
    {
        if (_levelRoot != null) Destroy(_levelRoot.gameObject);
        _levelRoot = null;
    }

    private bool HasRequiredPrefabs()
    {
        if (HazardPrefabs == null || HazardPrefabs.Length == 0)
        {
            Debug.LogError("❌ [LEVEL GEN]: no HazardPrefabs assigned — there would be nothing to sail around.");
            return false;
        }

        if (EndPortPrefab == null)
        {
            Debug.LogError("❌ [LEVEL GEN]: no EndPortPrefab assigned — the mission could never be completed.");
            return false;
        }

        if (SeaFloorPrefab == null)
        {
            // The stylized water is opaque, so this is not about seeing the
            // bottom — it is what the water measures its depth against. With
            // nothing behind it every pixel reads as maximum depth and the
            // ocean renders at DeepColor, which is black.
            Debug.LogWarning("⚠️ [LEVEL GEN]: no SeaFloorPrefab — the water has nothing to depth-fade against " +
                             "and will render at DeepColor (black) everywhere except around hazards.");
        }
        if (WaterPrefab == null) Debug.LogWarning("⚠️ [LEVEL GEN]: no WaterPrefab — relying on whatever water is already in the scene, which may not cover the level.");

        return true;
    }

    // ==============================
    // GRID + VALIDATION
    // ==============================

    private Node[,] CreateAndScatterGrid(int width, int length)
    {
        Node[,] grid = CreateEmptyGrid(width, length);

        int startX = width / 2;
        int endX = width / 2;
        int endY = length - 1;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < length; y++)
            {
                // Keep a 3x3 apron clear around the departure and arrival cells.
                if (Mathf.Abs(x - startX) <= 1 && y <= 1) continue;
                if (Mathf.Abs(x - endX) <= 1 && Mathf.Abs(y - endY) <= 1) continue;

                // Choke points tighten towards the finish.
                float depthFactor = (float)y / length;
                float density = Mathf.Lerp(ObstacleDensity * 0.5f, ObstacleDensity * 1.5f, depthFactor);
                density = Mathf.Clamp(density, 0.1f, 0.4f);

                if (Random.value <= density) grid[x, y].IsObstacle = true;
            }
        }

        return grid;
    }

    private Node[,] CreateEmptyGrid(int width, int length)
    {
        Node[,] grid = new Node[width, length];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < length; y++)
            {
                grid[x, y] = new Node(x, y, false);
            }
        }

        return grid;
    }

    private List<Node> FindPathAStar(Node[,] grid, Node startNode, Node endNode)
    {
        int width = grid.GetLength(0);
        int length = grid.GetLength(1);

        List<Node> openSet = new List<Node>();
        HashSet<Node> closedSet = new HashSet<Node>();

        openSet.Add(startNode);

        while (openSet.Count > 0)
        {
            Node currentNode = openSet[0];
            for (int i = 1; i < openSet.Count; i++)
            {
                if (openSet[i].FCost < currentNode.FCost ||
                    (openSet[i].FCost == currentNode.FCost && openSet[i].HCost < currentNode.HCost))
                {
                    currentNode = openSet[i];
                }
            }

            openSet.Remove(currentNode);
            closedSet.Add(currentNode);

            if (currentNode == endNode) return RetracePath(startNode, endNode);

            foreach (Node neighbor in GetNeighbors(grid, currentNode, width, length))
            {
                if (neighbor.IsObstacle || closedSet.Contains(neighbor)) continue;

                int moveCost = currentNode.GCost + GetDistance(currentNode, neighbor);

                if (moveCost < neighbor.GCost || !openSet.Contains(neighbor))
                {
                    neighbor.GCost = moveCost;
                    neighbor.HCost = GetDistance(neighbor, endNode);
                    neighbor.Parent = currentNode;

                    if (!openSet.Contains(neighbor)) openSet.Add(neighbor);
                }
            }
        }

        return null;
    }

    private List<Node> RetracePath(Node startNode, Node endNode)
    {
        List<Node> path = new List<Node>();
        Node currentNode = endNode;

        while (currentNode != startNode)
        {
            path.Add(currentNode);
            currentNode = currentNode.Parent;
        }

        path.Reverse();
        return path;
    }

    private List<Node> GetNeighbors(Node[,] grid, Node node, int width, int length)
    {
        List<Node> neighbors = new List<Node>();

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue;

                // Orthogonal only — a diagonal slip between two rocks is not a
                // gap a ship can actually use.
                if (Mathf.Abs(x) == 1 && Mathf.Abs(y) == 1) continue;

                int checkX = node.X + x;
                int checkY = node.Y + y;

                if (checkX >= 0 && checkX < width && checkY >= 0 && checkY < length)
                {
                    neighbors.Add(grid[checkX, checkY]);
                }
            }
        }

        return neighbors;
    }

    private int GetDistance(Node nodeA, Node nodeB)
    {
        int dstX = Mathf.Abs(nodeA.X - nodeB.X);
        int dstY = Mathf.Abs(nodeA.Y - nodeB.Y);

        if (dstX > dstY) return 14 * dstY + 10 * (dstX - dstY);
        return 14 * dstX + 10 * (dstY - dstX);
    }

    // ==============================
    // BUILD THE WORLD
    // ==============================

    private void Build(Node[,] grid, int length)
    {
        int width = grid.GetLength(0);

        _levelRoot = new GameObject("GeneratedLevel").transform;
        _hazardTagMissing = false;

        // The departure cell is pinned to world X = 0 so the ship spawn and the
        // grid always agree. With an odd GridWidth the map is symmetric about it.
        float originX = -(width / 2) * CellSize;
        float minX = originX;
        float maxX = originX + ((width - 1) * CellSize);
        float centerX = (minX + maxX) * 0.5f;
        float lengthZ = (length - 1) * CellSize;

        StartPosition = new Vector3(0f, ShipSpawnHeight, 0f);
        EndPosition = new Vector3(centerX, 0f, lengthZ);

        Vector3 envCenter = new Vector3(centerX, 0f, lengthZ * 0.5f);
        float envX = (maxX - minX) + CellSize + (EnvironmentMargin * 2f);
        float envZ = lengthZ + CellSize + (EnvironmentMargin * 2f);

        BuildSeaFloor(envCenter, envX, envZ);
        BuildWater(envCenter, envX, envZ);
        BuildHazards(grid, width, length, originX);
        BuildStartPort();
        BuildEndPort(minX, maxX);

        CaptureMapData(grid, width, length, originX);
        OnLevelGenerated?.Invoke();

        if (_hazardTagMissing)
        {
            Debug.LogError($"❌ [LEVEL GEN]: the \"{HazardTag}\" tag does not exist in this project, so ShipHull " +
                           "will never register a collision. Add it under Project Settings > Tags and Layers.");
        }
    }

    private void BuildSeaFloor(Vector3 envCenter, float envX, float envZ)
    {
        if (SeaFloorPrefab == null) return;

        GameObject floor = SpawnStretched(SeaFloorPrefab, "SeaFloor", envCenter, envX, envZ,
                                          Anchor.Top, -SeaFloorDepth);

        // Decorative. A collider down here would only fight the transform-driven ship.
        foreach (Collider col in floor.GetComponentsInChildren<Collider>())
        {
            Destroy(col);
        }
    }

    private void BuildWater(Vector3 envCenter, float envX, float envZ)
    {
        if (WaterPrefab == null) return;

        GameObject water = SpawnStretched(WaterPrefab, "Water", envCenter, envX, envZ, Anchor.Center, 0f);

        foreach (Collider col in water.GetComponentsInChildren<Collider>())
        {
            Destroy(col);
        }
    }

    private void BuildHazards(Node[,] grid, int width, int length, float originX)
    {
        // Everything below protects MinChannelWidth. A hazard may never reach
        // further than this from its cell centre, so a free cell flanked by two
        // occupied ones still leaves exactly MinChannelWidth of clear water.
        float maxReach = (CellSize - MinChannelWidth) * 0.5f;
        float nominal = Mathf.Min(CellSize * HazardCellFill, maxReach * 2f);
        int spawned = 0;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < length; y++)
            {
                if (!grid[x, y].IsObstacle) continue;

                GameObject prefab = HazardPrefabs[Random.Range(0, HazardPrefabs.Length)];
                if (prefab == null) continue;

                float footprint = Mathf.Min(nominal * Random.Range(HazardScaleJitter.x, HazardScaleJitter.y),
                                            maxReach * 2f);

                // Scatter only as far as the remaining slack allows.
                float slack = Mathf.Max(0f, maxReach - (footprint * 0.5f));
                float jitter = Mathf.Min(CellSize * HazardPositionJitter, slack);

                Vector3 cell = new Vector3(
                    originX + (x * CellSize) + Random.Range(-jitter, jitter),
                    0f,
                    (y * CellSize) + Random.Range(-jitter, jitter));

                BuildHazard(prefab, cell, footprint);
                spawned++;
            }
        }

        Debug.Log($"🪨 [LEVEL GEN]: {spawned} hazards, each within {maxReach:F1}u of its cell centre " +
                  $"— guaranteed channel {MinChannelWidth:F1}u for a ~6u hull.");
    }

    private void BuildHazard(GameObject prefab, Vector3 cell, float footprint)
    {
        GameObject hazard = Instantiate(prefab, Vector3.zero, Quaternion.identity, _levelRoot);

        // Yaw first, then measure. Measuring the rotated bounds is what makes the
        // channel guarantee exact: it is the rotated footprint that intrudes on
        // the neighbouring lane, so that is what gets fitted. Fitting before the
        // yaw would need the circumscribed diameter instead, shrinking every
        // rock by up to 1.4x for a worst case it may not even be in.
        hazard.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        Bounds bounds = MeasureBounds(hazard);
        float widest = Mathf.Max(bounds.size.x, bounds.size.z);
        if (widest > 0.0001f)
        {
            hazard.transform.localScale *= footprint / widest;
        }

        bounds = MeasureBounds(hazard);

        // Height is free — it costs nothing in channel width — so stretch it
        // rather than let elongated art fit down to something invisible. Y scale
        // and a Y rotation commute, so this cannot shear the mesh.
        if (bounds.size.y > 0.0001f && bounds.size.y < MinHazardHeight)
        {
            Vector3 stretched = hazard.transform.localScale;
            stretched.y *= MinHazardHeight / bounds.size.y;
            hazard.transform.localScale = stretched;
            bounds = MeasureBounds(hazard);
        }

        // Anchor by bounds so the pivot the artist happened to use is irrelevant.
        float targetY = bounds.size.y * (0.5f - HazardSubmersion);
        PlaceByBounds(hazard, new Vector3(cell.x, targetY, cell.z), bounds);

        MakeHazardDeadly(hazard);
    }

    /// <summary>
    /// ShipHull only reacts to a collider tagged "Hazard", and it is the
    /// collider's own GameObject that gets tag-checked. Guarantee both here so a
    /// designer can drop in bare art with no setup.
    /// </summary>
    private void MakeHazardDeadly(GameObject hazard)
    {
        Collider[] colliders = hazard.GetComponentsInChildren<Collider>();

        if (colliders.Length == 0)
        {
            Bounds bounds = MeasureBounds(hazard);
            BoxCollider box = hazard.AddComponent<BoxCollider>();
            Vector3 scale = hazard.transform.lossyScale;

            box.center = hazard.transform.InverseTransformPoint(bounds.center);
            box.size = new Vector3(
                bounds.size.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                bounds.size.y / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
                bounds.size.z / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));

            colliders = new Collider[] { box };
        }

        foreach (Collider col in colliders)
        {
            col.isTrigger = true;
            ApplyHazardTag(col.gameObject);
        }

        // A prefab with a Rigidbody would drift or sink once the level starts.
        foreach (Rigidbody body in hazard.GetComponentsInChildren<Rigidbody>())
        {
            body.isKinematic = true;
            body.useGravity = false;
        }
    }

    private void ApplyHazardTag(GameObject target)
    {
        if (target.CompareTag(HazardTag)) return;

        try
        {
            target.tag = HazardTag;
        }
        catch (UnityException)
        {
            _hazardTagMissing = true;
        }
    }

    private void BuildStartPort()
    {
        if (StartPortPrefab == null) return;

        GameObject port = Instantiate(StartPortPrefab, Vector3.zero, Quaternion.identity, _levelRoot);
        port.name = "StartPort";

        // One cell astern of the spawn point, sitting on the water. Anchoring by
        // bounds is what keeps it off the hull regardless of its pivot.
        Bounds bounds = MeasureBounds(port);
        Vector3 target = new Vector3(0f, bounds.size.y * 0.5f, -CellSize - (bounds.size.z * 0.5f));
        PlaceByBounds(port, target, bounds);
    }

    private void BuildEndPort(float minX, float maxX)
    {
        GameObject gate = SpawnStretched(EndPortPrefab, "EndPort", EndPosition,
                                         (maxX - minX) + CellSize, CellSize * 0.5f, Anchor.Center, 0f);

        // Height is set independently — the gate has to swallow the hull
        // vertically, and stretching it by footprint alone would not.
        Bounds bounds = MeasureBounds(gate);
        if (bounds.size.y > 0.0001f)
        {
            Vector3 scale = gate.transform.localScale;
            scale.y *= EndGateHeight / bounds.size.y;
            gate.transform.localScale = scale;
        }

        PlaceByBounds(gate, EndPosition, MeasureBounds(gate));

        // EndPort.OnTriggerEnter is the mission-success hook, so this must be a
        // trigger. Add the component too if the prefab is bare art.
        if (gate.GetComponentInChildren<EndPort>() == null) gate.AddComponent<EndPort>();

        Collider[] colliders = gate.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
        {
            gate.AddComponent<BoxCollider>().isTrigger = true;
            return;
        }

        foreach (Collider col in colliders)
        {
            col.isTrigger = true;
        }
    }

    // ==============================
    // MAP DATA
    // ==============================

    /// <summary>
    /// Keeps the validated grid around after spawning so the chart has something
    /// to draw. The world is already fully described by this data, so a map needs
    /// no extra camera, render texture or scene geometry.
    /// </summary>
    private void CaptureMapData(Node[,] grid, int width, int length, float originX)
    {
        GridColumns = width;
        GridRows = length;
        _gridOrigin = new Vector2(originX, 0f);

        if (_obstacleMap == null || _obstacleMap.GetLength(0) != width || _obstacleMap.GetLength(1) != length)
        {
            _obstacleMap = new bool[width, length];
        }

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < length; y++)
            {
                _obstacleMap[x, y] = grid[x, y].IsObstacle;
            }
        }

        StartCell = new Vector2Int(width / 2, 0);
        EndCell = new Vector2Int(width / 2, length - 1);
    }

    public bool IsObstacle(int column, int row)
    {
        if (_obstacleMap == null) return false;
        if (column < 0 || column >= GridColumns || row < 0 || row >= GridRows) return false;

        return _obstacleMap[column, row];
    }

    /// <summary>World XZ of a cell centre.</summary>
    public Vector3 WorldFromCell(int column, int row)
    {
        return new Vector3(_gridOrigin.x + (column * CellSize), 0f, _gridOrigin.y + (row * CellSize));
    }

    /// <summary>
    /// Position across the grid as 0..1 on each axis, which is all a UI chart
    /// needs. Values fall outside 0..1 when the ship strays past the grid edge.
    /// </summary>
    public Vector2 NormalizedFromWorld(Vector3 worldPosition)
    {
        if (GridColumns <= 1 || GridRows <= 1) return Vector2.zero;

        float spanX = (GridColumns - 1) * CellSize;
        float spanZ = (GridRows - 1) * CellSize;

        return new Vector2(
            (worldPosition.x - _gridOrigin.x) / spanX,
            (worldPosition.z - _gridOrigin.y) / spanZ);
    }

    public Vector2Int CellFromWorld(Vector3 worldPosition)
    {
        return new Vector2Int(
            Mathf.RoundToInt((worldPosition.x - _gridOrigin.x) / CellSize),
            Mathf.RoundToInt((worldPosition.z - _gridOrigin.y) / CellSize));
    }

    // ==============================
    // MEASURE / FIT / ANCHOR
    // ==============================

    /// <summary>
    /// Instantiates a prefab and stretches its measured footprint to targetX by
    /// targetZ, then anchors it. Replaces the old assumption that a sea floor
    /// prefab is "exactly 1x1" — true for a Cube or Quad, wrong by 10x for a Plane.
    /// </summary>
    private GameObject SpawnStretched(GameObject prefab, string name, Vector3 center,
                                      float targetX, float targetZ, Anchor anchor, float anchorY)
    {
        GameObject instance = Instantiate(prefab, Vector3.zero, Quaternion.identity, _levelRoot);
        instance.name = name;

        Bounds bounds = MeasureBounds(instance);
        Vector3 scale = instance.transform.localScale;

        if (bounds.size.x > 0.0001f) scale.x *= targetX / bounds.size.x;
        if (bounds.size.z > 0.0001f) scale.z *= targetZ / bounds.size.z;
        instance.transform.localScale = scale;

        bounds = MeasureBounds(instance);

        float y = anchor == Anchor.Top ? anchorY - (bounds.size.y * 0.5f) : anchorY;

        PlaceByBounds(instance, new Vector3(center.x, y, center.z), bounds);
        return instance;
    }

    /// <summary>
    /// Moves an object so the centre of its measured bounds lands exactly on
    /// target, cancelling out whatever offset exists between its transform and
    /// its art.
    /// </summary>
    private static void PlaceByBounds(GameObject instance, Vector3 target, Bounds bounds)
    {
        Vector3 pivotOffset = bounds.center - instance.transform.position;
        instance.transform.position = target - pivotOffset;
    }

    /// <summary>
    /// World-space bounds of everything drawn by an instance. Falls back to
    /// colliders for collider-only prefabs, then to a unit cube, so a bad prefab
    /// produces a wrong-looking level rather than a divide-by-zero.
    /// </summary>
    private static Bounds MeasureBounds(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        Collider[] colliders = instance.GetComponentsInChildren<Collider>();
        if (colliders.Length > 0)
        {
            Bounds bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
            {
                bounds.Encapsulate(colliders[i].bounds);
            }
            return bounds;
        }

        Debug.LogWarning($"⚠️ [LEVEL GEN]: \"{instance.name}\" has no renderer or collider to measure — " +
                         "treating it as a 1x1x1 box.");
        return new Bounds(instance.transform.position, Vector3.one);
    }
}
