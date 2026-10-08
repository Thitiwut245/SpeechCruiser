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
    [Tooltip("Harbour model, used at both ends: the ship departs from its quay and docks at the same model " +
             "at the destination. Placed to the ship's starboard side, with a berth (Quay) fitted to its edge.")]
    [SerializeField] private GameObject StartPortPrefab;

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

    [Header("Hazard Variety")]
    [Tooltip("Share of blocked cells that are one big reef (with a few rocks around it); the rest are " +
             "medium reefs or scatters of small rocks.")]
    [Range(0f, 1f)]
    [SerializeField] private float BigReefShare = 0.35f;

    [Tooltip("Share of blocked cells that are a scatter of small rocks.")]
    [Range(0f, 1f)]
    [SerializeField] private float RockScatterShare = 0.25f;

    [Tooltip("Share of blocked cells that are a sandbank (run aground, no damage) instead of rock.")]
    [Range(0f, 1f)]
    [SerializeField] private float SandbankShare = 0.18f;

    [Tooltip("On mine jobs, the share of blocked cells that are a small minefield (2-3 mines).")]
    [Range(0f, 1f)]
    [SerializeField] private float MineFieldShare = 0.28f;

    [Tooltip("Diameter of a naval mine, in metres. Real moored mines are about a metre; this is a little " +
             "bigger so it reads from the camera.")]
    [SerializeField] private float MineSize = 3f;

    [Tooltip("Fraction of a hazard's height below the waterline. 0.5 = half submerged.")]
    [Range(0f, 1f)]
    [SerializeField] private float HazardSubmersion = 0.55f;

    [Tooltip("Shortest a hazard may end up. Long, flat art fitted to the cell width would otherwise scale down " +
             "to something paper-thin the player cannot see. Height costs nothing in channel width, so it is " +
             "stretched independently.")]
    [SerializeField] private float MinHazardHeight = 6f;

    [Header("Environment")]
    [Tooltip("Depth of the sea floor's top surface below the water. Not only cosmetic: the sea shader " +
             "(Resources/Sea/Sea.shader) takes its colour from the depth of whatever is behind it, turquoise " +
             "in the shallows to deep blue-green by _DepthRange (7 m), and draws contact foam within " +
             "_ShoreFoam (1.4 m). Keep the floor deeper than the waves reach (about 3 m in a storm).")]
    [SerializeField] private float SeaFloorDepth = 6f;

    [Tooltip("Extra world units of water and sea floor beyond the grid edge, so the horizon never shows a seam.")]
    [SerializeField] private float EnvironmentMargin = 150f;

    [Header("Reefs")]
    [Tooltip("Build reefs from colourful generated variants (rock and coral) instead of the single Reef prefab.")]
    [SerializeField] private bool ReefVariants = true;

    [Header("Coast")]
    [Tooltip("Land around the sea: beach and hills just outside the reef wall, behind both harbours. Scenery only.")]
    [SerializeField] private bool BuildCoast = true;
    [Tooltip("How far inland the land reaches.")]
    [SerializeField] private float CoastDepth = 140f;
    [Tooltip("Size of the land's triangles: smaller is smoother and heavier.")]
    [SerializeField] private float CoastCell = 8f;
    [Tooltip("How far the shoreline wanders outward from the reef wall.")]
    [SerializeField] private float CoastRagged = 14f;
    [Tooltip("Trees per 1000 square units of grass.")]
    [SerializeField] private float CoastTrees = 1.5f;

    [Tooltip("Height the ship floats at. The generator owns this because it owns where the course starts.")]
    [SerializeField] private float ShipSpawnHeight = 1f;

    [Header("Mooring")]
    [Tooltip("Half the ship's width. The quay's near edge goes this far plus QuayGap to the ship's right.")]
    [SerializeField] private float ShipHalfBeam = 3f;
    [Tooltip("Water between the hull and the quay when alongside.")]
    [SerializeField] private float QuayGap = 1.2f;
    [Tooltip("Slides the quay along the ship (+ = towards the bow).")]
    [SerializeField] private float QuayOffsetZ = 0f;

    [Tooltip("Turn applied to the harbour model so its long side runs along the ship as the quay.")]
    [SerializeField] private float HarbourYaw = 90f;

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

    /// <summary>
    /// Length of the channel as a helmsman would sail it: the grid path steps from cell to cell,
    /// so it is pulled tight wherever there is clear water between two of its points.
    /// The par time is set from this.
    /// </summary>
    public float SmoothedSolutionLength
    {
        get
        {
            var path = SolutionWorldPath;
            if (path == null || path.Count < 2) return 0f;
            float length = 0f;
            int i = 0;
            while (i < path.Count - 1)
            {
                int j = path.Count - 1;
                while (j > i + 1 && !ClearLine(path[i], path[j])) j--;
                length += Vector3.Distance(path[i], path[j]);
                i = j;
            }
            return length;
        }
    }

    // No hazard cell (or its neighbour on either side of the line) between two points.
    private bool ClearLine(Vector3 a, Vector3 b)
    {
        float step = CellSize * 0.25f;
        int samples = Mathf.CeilToInt(Vector3.Distance(a, b) / step);
        Vector3 side = Vector3.Cross(Vector3.up, (b - a).normalized) * (CellSize * 0.35f);
        for (int s = 0; s <= samples; s++)
        {
            Vector3 p = Vector3.Lerp(a, b, s / (float)Mathf.Max(1, samples));
            foreach (var q in new[] { p, p + side, p - side })
            {
                var cell = CellFromWorld(q);
                if (IsObstacle(cell.x, cell.y)) return false;
            }
        }
        return true;
    }

    /// <summary>Length of the guaranteed channel through the level, in world units (cell to cell).</summary>
    public float SolutionLength
    {
        get
        {
            float length = 0f;
            if (SolutionWorldPath == null) return 0f;
            for (int i = 1; i < SolutionWorldPath.Count; i++) length += Vector3.Distance(SolutionWorldPath[i - 1], SolutionWorldPath[i]);
            return length;
        }
    }

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
        MaxRetries = Mathf.Max(1, MaxRetries);
    }

    /// <summary>Rows in the route for this job at the player's current difficulty tier.</summary>
    public int RouteRows(JobSpec job)
    {
        if (job.Rows > 0) return job.Rows; // a story mission's route is set by the mission
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

        var session = GameSession.Instance;
        _job = session != null ? session.CurrentJob : JobSpec.Normal;
        // A story mission has its own fixed seed, so its map is the same on every attempt.
        if (_job.Seed != 0) Random.InitState(_job.Seed);
        else if (Seed >= 0) Random.InitState(Seed);

        ClearLevel();

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

        if (StartPortPrefab == null)
        {
            Debug.LogError("❌ [LEVEL GEN]: no StartPortPrefab (harbour model) assigned — there would be no berth to dock at.");
            return false;
        }

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
                // A clear lane out of the dock, and clear harbour water round the destination
                // berth, reaching east over the harbour model beside it.
                if (Mathf.Abs(x - startX) <= 1 && y <= clearRows) continue;
                if (x >= endX - 1 && x <= endX + 2 && y >= endY - 1) continue;

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
        SeaWaves.ClearCalm(); // the harbours below set their sheltered water
        BuildWater();
        BuildHazards(grid, width, length, originX);
        BuildBoundaryWalls(minX, maxX, lengthZ);
        BuildStartPort();
        BuildEndPort();
        if (BuildCoast) BuildCoastline(minX, maxX, lengthZ);

        CaptureMapData(grid, width, length, originX);
        OnLevelGenerated?.Invoke();

        if (_hazardTagMissing)
        {
            Debug.LogError($"❌ [LEVEL GEN]: the \"{HazardTag}\" tag does not exist in this project, so ShipHull " +
                           "will never register a collision. Add it under Project Settings > Tags and Layers.");
        }
    }

    /// <summary>The sandy bottom (Seabed), SeaFloorDepth down: what the sea shader measures its depth against.</summary>
    private void BuildSeaFloor(Vector3 envCenter, float envX, float envZ)
    {
        Seabed.Build(_levelRoot, envCenter, envX, envZ, SeaFloorDepth);
    }

    /// <summary>The sea, with waves to suit the weather: a storm mission's are about twice as big.</summary>
    private void BuildWater()
    {
        var mission = GameManager.Instance != null ? StoryProgress.ActiveMission : null;
        SeaSurface.Create(_levelRoot, mission != null && mission.Weather == Weather.Storm ? 1.9f : 1f);
    }

    /// <summary>The mine among the hazard prefabs, or null.</summary>
    private GameObject FindMinePrefab()
    {
        foreach (var p in HazardPrefabs) if (p != null && IsMinePrefab(p)) return p;
        return null;
    }

    /// <summary>A random generated reef variant, or the given prefab when variants are off or unavailable.</summary>
    private GameObject ReefArt(GameObject[] variants, GameObject fallback)
    {
        if (!ReefVariants || variants == null || variants.Length == 0) return fallback;
        return variants[Random.Range(0, variants.Length)];
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

        // The wall uses the rockier variants, so the shoreline isn't a rainbow.
        for (float z = south; z <= north + 0.01f; z += spacing)
        {
            BuildHazard(ReefArt(ReefFactory.Shore, reef), new Vector3(left, 0f, z), footprint, MinHazardHeight);
            BuildHazard(ReefArt(ReefFactory.Shore, reef), new Vector3(right, 0f, z), footprint, MinHazardHeight);
            pieces += 2;
        }

        for (float x = left + spacing; x < right - 0.01f; x += spacing)
        {
            BuildHazard(ReefArt(ReefFactory.Shore, reef), new Vector3(x, 0f, south), footprint, MinHazardHeight);
            pieces++;
        }

        Debug.Log($"🧱 [LEVEL GEN]: {pieces} wall pieces close the channel (x {left:F0}..{right:F0}).");
    }

    /// <summary>
    /// Fills each blocked cell with one kind of danger, so the sea varies: a big reef with a
    /// few rocks round it, a medium reef, a scatter of small rocks, a sandbank, or (on mine
    /// jobs) a small field of chained mines.
    /// Everything stays within maxReach of its cell centre, which is what protects
    /// MinChannelWidth: a free cell between two blocked ones still has that much clear water.
    /// </summary>
    private void BuildHazards(Node[,] grid, int width, int length, float originX)
    {
        float maxReach = (CellSize - MinChannelWidth) * 0.5f;
        float big = Mathf.Min(CellSize * HazardCellFill, maxReach * 2f);
        int reefs = 0, rocks = 0, mines = 0, banks = 0;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < length; y++)
            {
                if (!grid[x, y].IsObstacle) continue;

                Vector3 cell = new Vector3(originX + (x * CellSize), 0f, y * CellSize);
                GameObject minePrefab = _job.Mines ? FindMinePrefab() : null;
                if (minePrefab != null && Random.value < MineFieldShare)
                {
                    mines += BuildMineField(minePrefab, cell, maxReach);
                    continue;
                }
                GameObject prefab = FindReefPrefab();
                if (prefab == null) continue;
                // Sandbanks are a lesson in reading the water; not on the very first voyage.
                if (!_gentle && Random.value < SandbankShare)
                {
                    Shoal.Build(_levelRoot, cell, maxReach, SeaFloorDepth);
                    banks++;
                    continue;
                }

                float roll = Random.value;
                int smallRocks;
                if (roll < BigReefShare)
                {
                    float size = big * Random.Range(0.8f, 1f);
                    BuildHazard(ReefArt(ReefFactory.Coral, prefab), Spot(cell, maxReach, size), size, MinHazardHeight);
                    reefs++;
                    smallRocks = Random.Range(0, 3);
                }
                else if (roll < BigReefShare + RockScatterShare)
                {
                    smallRocks = Random.Range(2, 5);
                }
                else
                {
                    float size = big * Random.Range(0.45f, 0.7f);
                    BuildHazard(ReefArt(ReefFactory.Coral, prefab), Spot(cell, maxReach, size), size, MinHazardHeight * 0.75f);
                    reefs++;
                    smallRocks = Random.Range(0, 2);
                }

                for (int i = 0; i < smallRocks; i++)
                {
                    float size = Random.Range(2.5f, 5f);
                    BuildHazard(ReefArt(ReefFactory.Shore, prefab), Spot(cell, maxReach, size), size, Mathf.Max(2.5f, size * 0.8f));
                    rocks++;
                }
            }
        }

        Debug.Log($"🪨 [LEVEL GEN]: {reefs} reefs, {rocks} small rocks, {banks} sandbanks, {mines} mines, each within " +
                  $"{maxReach:F1}u of its cell centre — guaranteed channel {MinChannelWidth:F1}u for a ~6u hull.");
    }

    /// <summary>A random spot in the cell where something <paramref name="size"/> across stays within maxReach.</summary>
    private static Vector3 Spot(Vector3 cell, float maxReach, float size)
    {
        Vector2 offset = Random.insideUnitCircle * Mathf.Max(0f, maxReach - size * 0.5f);
        return cell + new Vector3(offset.x, 0f, offset.y);
    }

    /// <summary>
    /// Two or three moored mines a few metres apart, each floating at the surface on a chain
    /// down to the seabed. Returns how many were laid.
    /// </summary>
    private int BuildMineField(GameObject prefab, Vector3 cell, float maxReach)
    {
        int want = Random.Range(2, 4), laid = 0;
        var placed = new List<Vector3>();
        for (int tries = 0; tries < 20 && laid < want; tries++)
        {
            Vector3 at = Spot(cell, maxReach, MineSize);
            bool clear = true;
            foreach (var p in placed) if ((p - at).sqrMagnitude < 5.5f * 5.5f) clear = false;
            if (!clear) continue;
            placed.Add(at);
            GameObject mine = BuildHazard(prefab, at, MineSize, 0f);
            AddMooring(mine.transform);
            laid++;
        }
        return laid;
    }

    /// <summary>The mine's chain, a dark line from under it down to the seabed.</summary>
    private void AddMooring(Transform mine)
    {
        var chain = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        chain.name = "Chain";
        Destroy(chain.GetComponent<Collider>());
        float top = mine.position.y - MineSize * 0.4f, bottom = -SeaFloorDepth;
        chain.transform.position = new Vector3(mine.position.x, (top + bottom) * 0.5f, mine.position.z);
        chain.transform.localScale = new Vector3(0.12f, (top - bottom) * 0.5f, 0.12f);
        chain.transform.SetParent(mine, true);
        var r = chain.GetComponent<Renderer>();
        if (_chainMaterial == null)
        {
            _chainMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Chain" };
            _chainMaterial.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.13f));
        }
        r.sharedMaterial = _chainMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static Material _chainMaterial;

    /// <summary>
    /// One hazard, fitted to <paramref name="footprint"/> across and stretched to at least
    /// <paramref name="minHeight"/> tall (0 leaves its proportions alone, for mines).
    /// </summary>
    private GameObject BuildHazard(GameObject prefab, Vector3 cell, float footprint, float minHeight)
    {
        GameObject hazard = Instantiate(prefab, Vector3.zero, prefab.transform.rotation, _levelRoot);
        hazard.SetActive(true); // generated reef templates are stored switched off
        // A random turn, so repeated variants don't line up.
        if (!IsMinePrefab(prefab)) hazard.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * hazard.transform.rotation;

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
        if (bounds.size.y > 0.0001f && bounds.size.y < minHeight)
        {
            Vector3 stretched = hazard.transform.localScale;
            
            float dotY = Mathf.Abs(hazard.transform.up.y);
            float dotX = Mathf.Abs(hazard.transform.right.y);
            float dotZ = Mathf.Abs(hazard.transform.forward.y);
            
            if (dotY >= dotX && dotY >= dotZ) stretched.y *= minHeight / bounds.size.y;
            else if (dotX >= dotY && dotX >= dotZ) stretched.x *= minHeight / bounds.size.y;
            else stretched.z *= minHeight / bounds.size.y;

            hazard.transform.localScale = stretched;
            bounds = MeasureBounds(hazard);
        }

        // Use the prefab's authored Y position directly, giving the artist full control
        hazard.transform.position = new Vector3(cell.x, prefab.transform.position.y, cell.z);
        if (IsMinePrefab(prefab))
        {
            // Mines float, a little more than half out of the water, and ride the waves.
            Bounds placed = MeasureBounds(hazard);
            hazard.transform.position += Vector3.up * (0.25f * placed.size.y - placed.center.y);
            hazard.AddComponent<SeaFloat>().Radius = Mathf.Max(1f, placed.size.x * 0.5f);
        }

        MakeHazardDeadly(hazard);
        return hazard;
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

    /// <summary>
    /// The home harbour: the model alongside the ship's starboard side with its quay fitted,
    /// so the ship starts moored at the berth. Its water is sheltered (calm).
    /// </summary>
    private void BuildStartPort()
    {
        Vector3 berth = new Vector3(0f, ShipSpawnHeight, 0f);
        GameObject port = PlaceHarbour("StartPort", berth);
        Quay.Build(_levelRoot, port, berth, berth.x + ShipHalfBeam + QuayGap, destination: false);
        SeaWaves.SetCalm(0, berth, 30f, 75f);
    }

    /// <summary>
    /// The destination: the same harbour model at the end of the route, its berth marked out
    /// on the water, and the DockingPilot that brings the ship in when the player asks.
    /// The voyage ends only here, tied up at the berth.
    /// </summary>
    private void BuildEndPort()
    {
        Vector3 berth = new Vector3(EndPosition.x, ShipSpawnHeight, EndPosition.z);
        GameObject port = PlaceHarbour("EndDock", berth);
        // Art only: the ship never collides with scenery, and the berth decides arrival.
        foreach (var old in port.GetComponentsInChildren<EndPort>()) Destroy(old);
        foreach (var col in port.GetComponentsInChildren<Collider>()) Destroy(col);
        Quay quay = Quay.Build(_levelRoot, port, berth, berth.x + ShipHalfBeam + QuayGap, destination: true);
        DockingPilot.Create(_levelRoot, quay);
        SeaWaves.SetCalm(1, berth, 30f, 75f);
    }

    /// <summary>
    /// The harbour model to the starboard side of a ship lying at <paramref name="berth"/>, its
    /// near edge QuayGap off the hull. Turned so its long side is the quay, as real ships lie.
    /// </summary>
    private GameObject PlaceHarbour(string name, Vector3 berth)
    {
        GameObject port = Instantiate(StartPortPrefab, Vector3.zero, Quaternion.Euler(0f, HarbourYaw, 0f), _levelRoot);
        port.name = name;
        Bounds bounds = MeasureBounds(port);
        Vector3 target = new Vector3(berth.x + (bounds.size.x * 0.5f) + ShipHalfBeam + QuayGap, bounds.size.y * 0.5f, berth.z + QuayOffsetZ);
        PlaceByBounds(port, target, bounds);
        return port;
    }

    /// <summary>
    /// Land just outside the reef wall on three sides, and behind the destination dock, so the
    /// sea reads as a channel between coasts and both harbours sit against land.
    /// </summary>
    private void BuildCoastline(float minX, float maxX, float lengthZ)
    {
        // The beach starts under the reef wall, so its rocks sit on the shore and the land
        // shows from the channel. The wall is one cell beyond the outer column.
        const float gap = 4f;
        float west = minX - CellSize, east = maxX + CellSize;
        float south = -CellSize * 2f, north = lengthZ + CellSize;

        // Keep both harbours in the water: the coast starts behind them.
        var start = _levelRoot.Find("StartPort");
        if (start != null) south = Mathf.Min(south, MeasureBounds(start.gameObject).min.z - gap);
        var dock = _levelRoot.Find("EndDock");
        if (dock != null) north = Mathf.Max(north, MeasureBounds(dock.gameObject).max.z + 2f);

        CoastBuilder.Build(_levelRoot, new CoastBuilder.Settings
        {
            Water = Rect.MinMaxRect(west, south, east, north),
            Depth = CoastDepth,
            Cell = Mathf.Max(2f, CoastCell),
            Ragged = CoastRagged,
            TreeDensity = CoastTrees,
            Seed = Random.Range(0, int.MaxValue),
        });
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

    /// <summary>Raised when a hazard is removed during a voyage (a mine went off), so the chart redraws.</summary>
    public event System.Action OnHazardCleared;

    /// <summary>A hazard at this world position is gone: its cell becomes open water on the chart.</summary>
    /// <summary>
    /// Marks the cell at <paramref name="worldPosition"/> as open water on the chart, unless
    /// something dangerous is still in it (the rest of a minefield).
    /// </summary>
    public void ClearHazardAt(Vector3 worldPosition)
    {
        Vector2Int cell = CellFromWorld(worldPosition);
        if (!IsObstacle(cell.x, cell.y)) return;
        Vector3 centre = WorldFromCell(cell.x, cell.y);
        var half = new Vector3(CellSize * 0.5f, 20f, CellSize * 0.5f);
        foreach (var c in Physics.OverlapBox(centre, half, Quaternion.identity, ~0, QueryTriggerInteraction.Collide))
            if (c.CompareTag(HazardTag)) return;
        _obstacleMap[cell.x, cell.y] = false;
        OnHazardCleared?.Invoke();
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
