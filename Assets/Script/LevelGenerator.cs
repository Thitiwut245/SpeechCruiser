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

    [Tooltip("Cells long (Z) at difficulty tier 1.")]
    [SerializeField] private int BaseGridLength = 30;

    [Tooltip("Extra cells of length per difficulty tier above 1. The tier grows with deliveries (GameSession.DifficultyTier).")]
    [SerializeField] private int CellsPerDifficulty = 5;

    [Header("Departure")]
    [Tooltip("Rows ahead of the dock kept free of hazards (3 cells wide), so the first order can't hit anything.")]
    [SerializeField] private int StartClearRows = 5;

    [Tooltip("Clear rows in the gentle learning layout (Easy job, first voyages).")]
    [SerializeField] private int FirstVoyageClearRows = 8;

    [Tooltip("Hazard density multiplier in the gentle learning layout.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float FirstVoyageDensityScale = 0.6f;

    [Tooltip("Weave required in the gentle learning layout. Each job sets its own otherwise (JobSpec.MinWeave).")]
    [SerializeField] private float FirstVoyageMinPathWeave = 1.05f;

    [Tooltip("How much the weave requirement relaxes after each rejected layout, so a sparse job still gets " +
             "a real course instead of running out of attempts.")]
    [SerializeField] private float WeaveRelaxPerAttempt = 0.03f;

    [Tooltip("The most the weave requirement ever relaxes. Below that, the most winding layout found is " +
             "used instead, so relaxing never accepts a straight run.")]
    [SerializeField] private float MaxWeaveRelax = 0.06f;

    [Tooltip("Extra attempts, with hazards thinned a little each time, when every normal attempt was blocked.")]
    [SerializeField] private int BlockedExtraAttempts = 10;

    [Tooltip("How much neighbouring wall pieces overlap, in world units, so no gap opens between them.")]
    [SerializeField] private float WallOverlap = 3f;

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

    [Header("Destination")]
    [Tooltip("Glowing material for the finish line drawn across the channel. Leave empty for no line.")]
    [SerializeField] private Material FinishLineMaterial;
    [SerializeField] private float FinishLineHeight = 0.3f;
    [SerializeField] private float FinishLineDepth = 2.5f;
    [Tooltip("Water between the finish line and the destination harbour.")]
    [SerializeField] private float DockSetBack = 6f;
    [Tooltip("Turn applied to the harbour model at the destination so its quay faces the arriving ship.")]
    [SerializeField] private float DockYaw = -90f;

    [Tooltip("Height the ship floats at. The generator owns this because it owns where the course starts.")]
    [SerializeField] private float ShipSpawnHeight = 1f;

    [Header("Mooring")]
    [Tooltip("Half the ship's width. The quay's near edge goes this far plus QuayGap to the ship's right.")]
    [SerializeField] private float ShipHalfBeam = 3f;
    [Tooltip("Water between the hull and the quay at the start.")]
    [SerializeField] private float QuayGap = 1.2f;
    [Tooltip("Slides the quay along the ship (+ = towards the bow).")]
    [SerializeField] private float QuayOffsetZ = 0f;

    [Header("Difficulty")]
    [Range(0.1f, 0.4f)]
    [Tooltip("Base density of obstacles. Recommended 0.2 to 0.35.")]
    [SerializeField] private float ObstacleDensity = 0.25f;

    [Tooltip("Attempts at a winding layout. After the last one the best connected layout found is used.")]
    [SerializeField] private int MaxRetries = 10;

    [Tooltip("Fixed seed for reproducible levels. Leave at -1 for a different level every mission.")]
    [SerializeField] private int Seed = -1;

    private Transform _levelRoot;
    private int _currentDifficulty = 1;
    private bool _gentle;
    private JobSpec _job = JobSpec.Normal;
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

    /// <summary>The current level is the gentle learning layout (the first Easy voyages).</summary>
    public bool IsLearningLayout => _gentle;
    
    /// <summary>The sequence of world coordinates safely avoiding hazards.</summary>
    public List<Vector3> SolutionWorldPath { get; private set; }

    private List<Node> _finalPathNodes;

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

    /// <summary>Rows in the route for this job at the player's current difficulty tier.</summary>
    public int RouteRows(JobSpec job)
    {
        int tier = GameSession.Instance != null ? GameSession.Instance.DifficultyTier : 1;
        return BaseGridLength + (tier - 1) * CellsPerDifficulty + job.ExtraRows;
    }

    /// <summary>Distance from the dock to the destination for this job, in metres (world units).</summary>
    public float RouteLength(JobSpec job)
    {
        return (RouteRows(job) - 1) * CellSize;
    }

    [ContextMenu("Regenerate Now")]
    public void GenerateLevel()
    {
        if (!HasRequiredPrefabs()) return;

        if (Seed >= 0) Random.InitState(Seed);

        ClearLevel();

        var session = GameSession.Instance;
        _job = session != null ? session.CurrentJob : JobSpec.Normal;
        _currentDifficulty = session != null ? session.DifficultyTier : 1;
        _gentle = session != null && session.UseGentleLayout;

        int gridLength = RouteRows(_job);
        float targetWeave = _gentle ? FirstVoyageMinPathWeave : _job.MinWeave;
        // RetracePath excludes the start node, so a dead-straight run is (gridLength - 1) nodes.
        float straight = gridLength - 1;

        Node[,] finalGrid = null;
        Node[,] bestGrid = null;
        List<Node> bestPath = null;

        // Relaxing stops above a straight run: Easy 1.04, Normal 1.14, Hard 1.24.
        float floorWeave = Mathf.Max(1f, targetWeave - MaxWeaveRelax);
        int blockedAttempts = 0;

        // Past MaxRetries, keep going only while every layout so far was blocked, thinning the hazards.
        for (int attempt = 1; attempt <= MaxRetries || (bestPath == null && attempt <= MaxRetries + BlockedExtraAttempts); attempt++)
        {
            float thin = attempt <= MaxRetries ? 1f : Mathf.Pow(0.9f, attempt - MaxRetries);
            Node[,] grid = CreateAndScatterGrid(GridWidth, gridLength, thin);
            List<Node> path = FindPathAStar(grid, grid[GridWidth / 2, 0], grid[GridWidth / 2, gridLength - 1]);
            if (path == null) { blockedAttempts++; continue; } // blocked: never usable

            // Keep the most winding connected layout in case none reaches the target.
            if (bestPath == null || path.Count > bestPath.Count)
            {
                bestGrid = grid;
                bestPath = path;
            }

            float required = Mathf.Max(floorWeave, targetWeave - WeaveRelaxPerAttempt * (attempt - 1));
            if (path.Count < straight * required) continue;

            finalGrid = grid;
            _finalPathNodes = path;
            Debug.Log($"🌊 [LEVEL GEN]: {_job.Name}{(_gentle ? " (gentle)" : "")}, tier {_currentDifficulty}, grid {GridWidth}x{gridLength}, " +
                      $"weave {path.Count / straight:0.00} (needed {required:0.00}) on attempt {attempt}" +
                      (blockedAttempts > 0 ? $", {blockedAttempts} blocked." : "."));
            break;
        }

        if (finalGrid == null && bestGrid != null)
        {
            // Every layout was straighter than wanted, but it still has its hazards and a
            // guaranteed way through: far better than an empty sea.
            finalGrid = bestGrid;
            _finalPathNodes = bestPath;
            Debug.Log($"🌊 [LEVEL GEN]: {_job.Name}, no layout reached the weave needed ({floorWeave:0.00} at the least); using the most winding found " +
                      $"({bestPath.Count / straight:0.00}).");
        }

        if (finalGrid == null)
        {
            // Only possible if every attempt was completely blocked.
            Debug.LogError($"❌ [LEVEL GEN]: no connected layout in {MaxRetries + BlockedExtraAttempts} attempts — falling back to open sea. " +
                           "Lower ObstacleDensity or widen the grid.");
            finalGrid = CreateEmptyGrid(GridWidth, gridLength);
            _finalPathNodes = new List<Node>();
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

    /// <param name="thin">Scales every cell's hazard chance; below 1 only after blocked attempts.</param>
    private Node[,] CreateAndScatterGrid(int width, int length, float thin = 1f)
    {
        Node[,] grid = CreateEmptyGrid(width, length);

        int startX = width / 2;
        int endX = width / 2;
        int endY = length - 1;

        int clearRows = _gentle ? FirstVoyageClearRows : StartClearRows;
        float densityScale = (_gentle ? FirstVoyageDensityScale : 1f) * _job.DensityScale * thin;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < length; y++)
            {
                // A clear lane out of the dock, and a 3x3 apron around the arrival.
                if (Mathf.Abs(x - startX) <= 1 && y <= clearRows) continue;
                if (Mathf.Abs(x - endX) <= 1 && Mathf.Abs(y - endY) <= 1) continue;

                // Choke points tighten towards the finish.
                float depthFactor = (float)y / length;
                float density = Mathf.Lerp(ObstacleDensity * 0.5f, ObstacleDensity * 1.5f, depthFactor);
                density = Mathf.Clamp(density, 0.1f, 0.4f) * densityScale;

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

        // Membership lives in flag arrays: the old List.Contains made every step O(n).
        var openSet = new List<Node>(width * 4);
        var inOpen = new bool[width, length];
        var closed = new bool[width, length];

        openSet.Add(startNode);
        inOpen[startNode.X, startNode.Y] = true;

        while (openSet.Count > 0)
        {
            int bestIndex = 0;
            for (int i = 1; i < openSet.Count; i++)
            {
                Node candidate = openSet[i];
                Node best = openSet[bestIndex];
                if (candidate.FCost < best.FCost || (candidate.FCost == best.FCost && candidate.HCost < best.HCost))
                {
                    bestIndex = i;
                }
            }

            Node currentNode = openSet[bestIndex];
            openSet[bestIndex] = openSet[openSet.Count - 1];
            openSet.RemoveAt(openSet.Count - 1);
            inOpen[currentNode.X, currentNode.Y] = false;
            closed[currentNode.X, currentNode.Y] = true;

            if (currentNode == endNode) return RetracePath(startNode, endNode);

            GetNeighbors(grid, currentNode, width, length, _neighbours);
            foreach (Node neighbor in _neighbours)
            {
                if (neighbor.IsObstacle || closed[neighbor.X, neighbor.Y]) continue;

                int moveCost = currentNode.GCost + GetDistance(currentNode, neighbor);
                bool open = inOpen[neighbor.X, neighbor.Y];

                if (moveCost < neighbor.GCost || !open)
                {
                    neighbor.GCost = moveCost;
                    neighbor.HCost = GetDistance(neighbor, endNode);
                    neighbor.Parent = currentNode;

                    if (!open)
                    {
                        openSet.Add(neighbor);
                        inOpen[neighbor.X, neighbor.Y] = true;
                    }
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

    private readonly List<Node> _neighbours = new List<Node>(4);

    private static void GetNeighbors(Node[,] grid, Node node, int width, int length, List<Node> neighbors)
    {
        neighbors.Clear();

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
        BuildBoundaryWalls(minX, maxX, lengthZ);
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

    /// <summary>Random hazard art; reefs only when the job has no mines.</summary>
    private GameObject PickHazardPrefab()
    {
        for (int tries = 0; tries < 8; tries++)
        {
            GameObject prefab = HazardPrefabs[Random.Range(0, HazardPrefabs.Length)];
            if (prefab == null) continue;
            if (!_job.Mines && IsMinePrefab(prefab)) continue;
            return prefab;
        }
        return FindReefPrefab();
    }

    private static bool IsMinePrefab(GameObject prefab)
    {
        return prefab.name.IndexOf("mine", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private GameObject FindReefPrefab()
    {
        foreach (var p in HazardPrefabs) if (p != null && !IsMinePrefab(p)) return p;
        return HazardPrefabs.Length > 0 ? HazardPrefabs[0] : null;
    }

    /// <summary>
    /// Reef walls down both sides of the channel and behind the start, so the only way to
    /// the dock is through the hazard field. Pieces overlap, leaving no gap a hull fits through.
    /// </summary>
    private void BuildBoundaryWalls(float minX, float maxX, float lengthZ)
    {
        GameObject reef = FindReefPrefab();
        if (reef == null) return;

        float spacing = CellSize * 0.5f;
        float footprint = spacing + WallOverlap;
        float left = minX - CellSize;
        float right = maxX + CellSize;
        float south = -CellSize * 2f;
        float north = lengthZ + CellSize;
        int pieces = 0;

        for (float z = south; z <= north + 0.01f; z += spacing)
        {
            BuildHazard(reef, new Vector3(left, 0f, z), footprint);
            BuildHazard(reef, new Vector3(right, 0f, z), footprint);
            pieces += 2;
        }

        for (float x = left + spacing; x < right - 0.01f; x += spacing)
        {
            BuildHazard(reef, new Vector3(x, 0f, south), footprint);
            pieces++;
        }

        Debug.Log($"🧱 [LEVEL GEN]: {pieces} wall pieces close the channel (x {left:F0}..{right:F0}).");
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

                GameObject prefab = PickHazardPrefab();
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
        GameObject hazard = Instantiate(prefab, Vector3.zero, prefab.transform.rotation, _levelRoot);

        // Measuring the rotated bounds is what makes the channel guarantee exact: 
        // it is the rotated footprint that intrudes on the neighbouring lane, so that is what gets fitted.

        Bounds bounds = MeasureBounds(hazard);
        float widest = Mathf.Max(bounds.size.x, bounds.size.z);
        if (widest > 0.0001f)
        {
            hazard.transform.localScale *= footprint / widest;
        }

        bounds = MeasureBounds(hazard);

        // Height is free — it costs nothing in channel width — so stretch it
        // rather than let elongated art fit down to something invisible.
        // We must stretch the local axis that corresponds most closely to World UP,
        // otherwise a prefab rotated 90 degrees would stretch horizontally instead.
        if (bounds.size.y > 0.0001f && bounds.size.y < MinHazardHeight)
        {
            Vector3 stretched = hazard.transform.localScale;
            
            float dotY = Mathf.Abs(hazard.transform.up.y);
            float dotX = Mathf.Abs(hazard.transform.right.y);
            float dotZ = Mathf.Abs(hazard.transform.forward.y);
            
            if (dotY >= dotX && dotY >= dotZ) stretched.y *= MinHazardHeight / bounds.size.y;
            else if (dotX >= dotY && dotX >= dotZ) stretched.x *= MinHazardHeight / bounds.size.y;
            else stretched.z *= MinHazardHeight / bounds.size.y;

            hazard.transform.localScale = stretched;
            bounds = MeasureBounds(hazard);
        }

        // Use the prefab's authored Y position directly, giving the artist full control
        hazard.transform.position = new Vector3(cell.x, prefab.transform.position.y, cell.z);

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

        colliders = FitCollider(hazard, colliders);

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

    /// <summary>
    /// Replaces the prefab's collision with a shape that matches what is drawn: a sphere
    /// for a round mine, and a box at 85% of the drawn footprint for a rock, so a hit only
    /// happens when the hull visibly touches the hazard.
    /// </summary>
    private Collider[] FitCollider(GameObject hazard, Collider[] existing)
    {
        Bounds bounds = MeasureBounds(hazard);
        if (bounds.size.sqrMagnitude < 0.0001f) return existing;

        foreach (var col in existing) Destroy(col);

        Vector3 scale = hazard.transform.lossyScale;
        Vector3 localCenter = hazard.transform.InverseTransformPoint(bounds.center);
        bool mine = hazard.name.IndexOf("mine", System.StringComparison.OrdinalIgnoreCase) >= 0;

        if (mine)
        {
            var sphere = hazard.AddComponent<SphereCollider>();
            float worldRadius = 0.45f * Mathf.Max(bounds.size.x, bounds.size.z);
            sphere.center = localCenter;
            sphere.radius = worldRadius / Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
            return new Collider[] { sphere };
        }

        var box = hazard.AddComponent<BoxCollider>();
        box.center = localCenter;
        box.size = new Vector3(
            0.85f * bounds.size.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
            bounds.size.y / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
            0.85f * bounds.size.z / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
        return new Collider[] { box };
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

        // Alongside the ship's starboard (right) side, sitting on the water, so the ship
        // starts moored at the quay. Anchoring by bounds keeps it off the hull whatever the pivot.
        Bounds bounds = MeasureBounds(port);
        Vector3 target = new Vector3((bounds.size.x * 0.5f) + ShipHalfBeam + QuayGap, bounds.size.y * 0.5f, QuayOffsetZ);
        PlaceByBounds(port, target, bounds);
    }

    /// <summary>
    /// The destination: an invisible finish trigger across the whole channel, a glowing gold
    /// line on the water where it starts, and the harbour model at its own size just beyond,
    /// so the player can see where they are going long before they arrive.
    /// </summary>
    private void BuildEndPort(float minX, float maxX)
    {
        float width = (maxX - minX) + CellSize;

        var gate = new GameObject("EndPort");
        gate.transform.SetParent(_levelRoot, false);
        gate.transform.position = new Vector3(EndPosition.x, EndGateHeight * 0.5f - 4f, EndPosition.z);
        var trigger = gate.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(width, EndGateHeight, CellSize * 0.5f);
        gate.AddComponent<EndPort>();

        float lineZ = EndPosition.z - CellSize * 0.25f;
        if (FinishLineMaterial != null)
        {
            var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = "FinishLine";
            Destroy(line.GetComponent<Collider>());
            line.transform.SetParent(_levelRoot, false);
            line.transform.position = new Vector3(EndPosition.x, FinishLineHeight, lineZ);
            line.transform.localScale = new Vector3(width, 0.25f, FinishLineDepth);
            line.GetComponent<Renderer>().sharedMaterial = FinishLineMaterial;
        }

        // The same harbour model as the start, turned so its quay faces the arriving ship.
        GameObject dockArt = StartPortPrefab != null ? StartPortPrefab : EndPortPrefab;
        if (dockArt == null) return;

        GameObject dock = Instantiate(dockArt, Vector3.zero, Quaternion.Euler(0f, DockYaw, 0f) * dockArt.transform.rotation, _levelRoot);
        dock.name = "EndDock";
        // Art only: the trigger above decides arrival, and the ship never collides with scenery.
        foreach (var port in dock.GetComponentsInChildren<EndPort>()) Destroy(port);
        foreach (var col in dock.GetComponentsInChildren<Collider>()) Destroy(col);

        Bounds bounds = MeasureBounds(dock);
        Vector3 target = new Vector3(EndPosition.x, bounds.size.y * 0.5f,
                                     EndPosition.z + CellSize * 0.25f + DockSetBack + bounds.size.z * 0.5f);
        PlaceByBounds(dock, target, bounds);
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
        
        SolutionWorldPath = new List<Vector3>();
        if (_finalPathNodes != null)
        {
            // Add Start Position explicitly since RetracePath excludes it
            SolutionWorldPath.Add(WorldFromCell(StartCell.x, StartCell.y));
            foreach (var node in _finalPathNodes)
            {
                SolutionWorldPath.Add(WorldFromCell(node.X, node.Y));
            }
        }
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
