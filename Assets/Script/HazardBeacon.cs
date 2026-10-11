using UnityEngine;

/// <summary>
/// Blinking red warning light on a naval mine, so mines read at a glance against the
/// bright water. Each beacon starts at a random phase so a minefield doesn't pulse in sync.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class HazardBeacon : MonoBehaviour
{
    [SerializeField] private float Period = 1.2f;
    [Range(0.05f, 0.95f)]
    [SerializeField] private float OnFraction = 0.35f;
    [SerializeField] private Color Glow = new Color(1f, 0.12f, 0.06f);
    [SerializeField] private float Intensity = 4f;

    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    private Renderer _renderer;
    private MaterialPropertyBlock _block;
    private float _phase;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _block = new MaterialPropertyBlock();
        _phase = Random.value * Period;
    }

    private void Update()
    {
        bool on = Mathf.Repeat(Time.time + _phase, Period) < Period * OnFraction;
        float level = on ? 1f : 0.15f;

        _renderer.GetPropertyBlock(_block);
        _block.SetColor(EmissionColor, Glow * (Intensity * level));
        _block.SetColor(BaseColor, Glow * level);
        _renderer.SetPropertyBlock(_block);
    }
}
