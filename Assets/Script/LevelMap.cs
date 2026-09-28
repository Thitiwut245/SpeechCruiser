using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Toggleable navigation chart for the procedurally generated course.
///
/// The generator already holds a complete description of the world as a grid, so
/// this needs no second camera, no render texture and no scene geometry — it
/// paints one pixel per cell into a small Texture2D and lets UI Toolkit scale it
/// up with point filtering, which is also what gives it the blocky chart look.
///
/// The whole panel is built in code and parented to the existing ShipUI document,
/// so there is nothing to author in UXML.
/// </summary>
public class LevelMap : MonoBehaviour
{
    /// <summary>How much of the course the chart gives away.</summary>
    public enum RevealMode
    {
        /// <summary>Everything visible. The bridge has a survey chart.</summary>
        FullChart,

        /// <summary>Only water the ship has already passed near. Keeps the lookout worth asking.</summary>
        ChartedAsYouSail,
    }

    [Header("Hook-up")]
    [Tooltip("The UIDocument to attach the chart to. Defaults to one on this GameObject.")]
    [SerializeField] private UIDocument Document;

    [Header("Input")]
    [Tooltip("Key that shows and hides the chart. M by default — T, Y, Tab, 1 and 2 are already taken by the radio.")]
    [SerializeField] private Key ToggleKey = Key.M;

    /// <summary>The key that opens the chart, for help text.</summary>
    public string ToggleKeyName => ToggleKey.ToString().ToUpperInvariant();

    [Tooltip("Whether the chart starts open when a mission begins.")]
    [SerializeField] private bool OpenByDefault = false;

    [Header("Layout")]
    [Tooltip("Height of the chart in pixels. Width follows the grid's aspect ratio.")]
    [SerializeField] private float MapHeightPixels = 440f;

    [Tooltip("Distance from the right edge of the screen.")]
    [SerializeField] private float MarginPixels = 24f;

    [Header("Reveal")]
    [Tooltip("FullChart shows the whole course. ChartedAsYouSail hides everything the ship has not sailed near, " +
             "which keeps asking the lookout what is ahead worthwhile.")]
    [SerializeField] private RevealMode Reveal = RevealMode.FullChart;

    [Tooltip("Radius, in cells, that the ship charts around itself when using ChartedAsYouSail.")]
    [SerializeField] private int RevealRadiusCells = 3;

    [Header("Chart Colours")]
    [SerializeField] private Color WaterColor = new Color(0.07f, 0.20f, 0.30f);
    [SerializeField] private Color HazardColor = new Color(0.85f, 0.42f, 0.20f);
    [SerializeField] private Color StartColor = new Color(0.30f, 0.65f, 0.45f);
    [SerializeField] private Color EndColor = new Color(0.95f, 0.82f, 0.25f);
    [SerializeField] private Color UnchartedColor = new Color(0.04f, 0.07f, 0.10f);
    [SerializeField] private Color ShipColor = new Color(0.95f, 0.97f, 1f);

    private VisualElement _panel;
    private VisualElement _chartArea;
    private VisualElement _shipMarker;
    private Image _chartImage;

    private Texture2D _texture;
    private Color32[] _pixels;
    private bool[,] _revealed;
    private Vector2Int _lastShipCell = new Vector2Int(int.MinValue, int.MinValue);
    private bool _chartDirty;
    private bool _isOpen;

    public bool IsOpen => _isOpen;

    private void Start()
    {
        if (Document == null) Document = GetComponent<UIDocument>();
        if (Document == null)
        {
            Debug.LogError("[LevelMap]: no UIDocument assigned or on this GameObject — the chart cannot be shown.");
            enabled = false;
            return;
        }

        BuildPanel();

        if (LevelGenerator.Instance != null)
        {
            LevelGenerator.Instance.OnLevelGenerated += HandleLevelGenerated;

            // A level may already exist if this component woke up late.
            if (LevelGenerator.Instance.IsLevelReady) HandleLevelGenerated();
        }
        else
        {
            Debug.LogWarning("[LevelMap]: no LevelGenerator in the scene — nothing to chart.");
        }

        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged += HandleStateChanged;

        SetOpen(false);
    }

    private void Update()
    {
        if (Keyboard.current != null && !TypedOrders.BlocksHotkeys && Keyboard.current[ToggleKey].wasPressedThisFrame) SetOpen(!_isOpen);

        // Charting runs whether or not the panel is visible. Gating it on _isOpen
        // would mean sailing with the map closed and then opening it to a blank
        // chart of water you had already passed through.
        if (Reveal == RevealMode.ChartedAsYouSail) TrackCharting();

        if (!_isOpen) return;

        UpdateShipMarker();

        if (_chartDirty) RedrawChart();
    }

    private void OnDestroy()
    {
        if (LevelGenerator.Instance != null) LevelGenerator.Instance.OnLevelGenerated -= HandleLevelGenerated;
        if (GameManager.Instance != null) GameManager.Instance.OnStateChanged -= HandleStateChanged;

        if (_texture != null) Destroy(_texture);
    }

    public void SetOpen(bool open)
    {
        _isOpen = open;
        if (_panel == null) return;

        _panel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

        // Catch up on anything charted while the panel was hidden.
        if (open && _chartDirty) RedrawChart();
    }

    private void HandleStateChanged(GameState state)
    {
        if (state == GameState.Playing)
        {
            SetOpen(OpenByDefault);
            return;
        }

        SetOpen(false);
    }

    private void HandleLevelGenerated()
    {
        var generator = LevelGenerator.Instance;
        int columns = generator.GridColumns;
        int rows = generator.GridRows;

        if (columns <= 0 || rows <= 0) return;

        if (_texture == null || _texture.width != columns || _texture.height != rows)
        {
            if (_texture != null) Destroy(_texture);

            _texture = new Texture2D(columns, rows, TextureFormat.RGBA32, false)
            {
                // One pixel per cell, scaled up by the layout. Point filtering is
                // what keeps the cells crisp instead of a blurry smear.
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            _pixels = new Color32[columns * rows];
            _chartImage.image = _texture;
        }

        _revealed = new bool[columns, rows];
        _lastShipCell = new Vector2Int(int.MinValue, int.MinValue);

        // Keep the chart's proportions honest — a 15x30 grid is a tall, narrow course.
        _chartArea.style.height = MapHeightPixels;
        _chartArea.style.width = MapHeightPixels * ((float)columns / rows);

        _chartDirty = true;
        if (_isOpen) RedrawChart();
    }

    private void UpdateShipMarker()
    {
        var generator = LevelGenerator.Instance;
        if (generator == null || !generator.IsLevelReady) return;

        var navigator = ShipNavigator.Instance;
        if (navigator == null) return;

        Vector3 shipPosition = navigator.transform.position;
        Vector2 normalized = generator.NormalizedFromWorld(shipPosition);

        // Grid row 0 is the start line and sits at the bottom of the chart, so the
        // vertical axis is inverted for UI Toolkit, whose Y grows downward.
        _shipMarker.style.left = Length.Percent(Mathf.Clamp01(normalized.x) * 100f);
        _shipMarker.style.top = Length.Percent((1f - Mathf.Clamp01(normalized.y)) * 100f);

        // World yaw and UI rotation both run clockwise with +Z up the chart, so
        // the heading maps across directly.
        _shipMarker.style.rotate = new Rotate(new Angle(navigator.transform.eulerAngles.y, AngleUnit.Degree));
    }

    private void TrackCharting()
    {
        var generator = LevelGenerator.Instance;
        if (generator == null || !generator.IsLevelReady || _revealed == null) return;

        var navigator = ShipNavigator.Instance;
        if (navigator == null) return;

        Vector2Int cell = generator.CellFromWorld(navigator.transform.position);
        if (cell == _lastShipCell) return;

        _lastShipCell = cell;

        int radius = Mathf.Max(0, RevealRadiusCells);
        int radiusSquared = radius * radius;

        for (int x = cell.x - radius; x <= cell.x + radius; x++)
        {
            for (int y = cell.y - radius; y <= cell.y + radius; y++)
            {
                if (x < 0 || x >= generator.GridColumns || y < 0 || y >= generator.GridRows) continue;

                int dx = x - cell.x;
                int dy = y - cell.y;
                if ((dx * dx) + (dy * dy) > radiusSquared) continue;

                if (_revealed[x, y]) continue;

                _revealed[x, y] = true;
                _chartDirty = true;
            }
        }
    }

    private void RedrawChart()
    {
        var generator = LevelGenerator.Instance;
        if (generator == null || _texture == null || _pixels == null) return;

        int columns = generator.GridColumns;
        int rows = generator.GridRows;

        for (int y = 0; y < rows; y++)
        {
            int rowStart = y * columns;

            for (int x = 0; x < columns; x++)
            {
                // Texture2D row 0 is the bottom row, so the start line lands at
                // the bottom of the chart with no flipping needed.
                _pixels[rowStart + x] = ColorForCell(generator, x, y);
            }
        }

        _texture.SetPixels32(_pixels);
        _texture.Apply(false);
        _chartDirty = false;
    }

    private Color32 ColorForCell(LevelGenerator generator, int column, int row)
    {
        if (Reveal == RevealMode.ChartedAsYouSail && _revealed != null && !_revealed[column, row])
        {
            return UnchartedColor;
        }

        // Full rows, so departure and arrival read as lines rather than dots.
        if (row == 0) return StartColor;
        if (row == generator.GridRows - 1) return EndColor;

        return generator.IsObstacle(column, row) ? HazardColor : WaterColor;
    }

    private void BuildPanel()
    {
        VisualElement root = Document.rootVisualElement;

        _panel = new VisualElement();
        _panel.style.position = Position.Absolute;
        _panel.style.right = MarginPixels;
        _panel.style.top = MarginPixels + 60f; // below the hull widget, which shares the top-right corner
        _panel.style.paddingTop = 6f;
        _panel.style.paddingBottom = 6f;
        _panel.style.paddingLeft = 6f;
        _panel.style.paddingRight = 6f;
        _panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
        _panel.style.display = DisplayStyle.None;

        // Never steal clicks from the rest of the interface.
        _panel.pickingMode = PickingMode.Ignore;

        _chartArea = new VisualElement();
        _chartArea.style.position = Position.Relative;
        _chartArea.style.height = MapHeightPixels;
        _chartArea.style.width = MapHeightPixels * 0.5f;
        _chartArea.pickingMode = PickingMode.Ignore;

        _chartImage = new Image();
        _chartImage.scaleMode = ScaleMode.StretchToFill;
        _chartImage.style.position = Position.Absolute;
        _chartImage.style.left = 0f;
        _chartImage.style.top = 0f;
        _chartImage.style.right = 0f;
        _chartImage.style.bottom = 0f;
        _chartImage.pickingMode = PickingMode.Ignore;

        _shipMarker = new VisualElement();
        _shipMarker.style.position = Position.Absolute;
        _shipMarker.style.width = 9f;
        _shipMarker.style.height = 15f;
        _shipMarker.style.backgroundColor = ShipColor;

        // Rounded bow, square stern — enough to read which way the ship is facing.
        _shipMarker.style.borderTopLeftRadius = 5f;
        _shipMarker.style.borderTopRightRadius = 5f;

        // Anchor the marker by its centre so left/top are the ship's position.
        _shipMarker.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
        _shipMarker.pickingMode = PickingMode.Ignore;

        _chartArea.Add(_chartImage);
        _chartArea.Add(_shipMarker);
        _panel.Add(_chartArea);
        root.Add(_panel);
    }
}
