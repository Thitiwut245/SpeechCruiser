using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The moment an upgrade is fitted in the shipyard, made to be seen: the ship gives a little
/// bounce, two rings spread on the water round it, the horn sounds, and the upgrade's name and
/// what it does rise over the ship and fade (ShipLook has already put the new part on the hull).
/// Lives on the drawn ship for the few seconds it plays, then removes itself.
/// </summary>
public class UpgradeFanfare : MonoBehaviour
{
    private const float Seconds = 2.8f;
    private static Material _ring;

    /// <summary>Plays over <paramref name="ship"/> (the drawn hull) and the shipyard's UI <paramref name="root"/>.</summary>
    public static void Play(Transform ship, VisualElement root, string title, string effect)
    {
        if (ship != null)
        {
            var old = ship.GetComponent<UpgradeFanfare>();
            if (old != null) { old.StopAllCoroutines(); old.Finish(); Destroy(old); }
            var fanfare = ship.gameObject.AddComponent<UpgradeFanfare>();
            fanfare.StartCoroutine(fanfare.Bounce());
            fanfare.StartCoroutine(fanfare.Ring(0f));
            fanfare.StartCoroutine(fanfare.Ring(0.3f));
        }
        // Over the ship, which stands right of the store page.
        ScreenBanner.Show(root, "UPGRADE FITTED", title, effect, Seconds, left: 42f);
        var audio = AudioService.Instance;
        if (audio != null && audio.Sfx != null && audio.Sfx.ShipHorn != null) audio.Play(audio.Sfx.ShipHorn, AudioBus.Effects, 0.4f, 1.15f);
    }

    private Vector3 _restScale;
    private bool _scaled;

    private IEnumerator Bounce()
    {
        _restScale = transform.localScale;
        _scaled = true;
        // Up and back with a little overshoot, like a hull settling after a lift.
        for (float t = 0f; t < 0.7f; t += Time.unscaledDeltaTime)
        {
            float k = t / 0.7f;
            float s = 1f + 0.09f * Mathf.Sin(k * Mathf.PI) * (1f - k * 0.5f);
            transform.localScale = _restScale * s;
            yield return null;
        }
        Finish();
        yield return new WaitForSecondsRealtime(Seconds);
        Destroy(this);
    }

    private void Finish()
    {
        if (_scaled) transform.localScale = _restScale;
        _scaled = false;
    }

    private void OnDestroy() => Finish();

    /// <summary>A ring spreading on the water from the ship and fading.</summary>
    private IEnumerator Ring(float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        var go = new GameObject("UpgradeRing");
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.TransformZ;
        line.loop = true;
        line.positionCount = 48;
        line.widthMultiplier = 0.9f;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        if (_ring == null) _ring = new Material(Shader.Find("Sprites/Default")) { name = "Upgrade ring", renderQueue = 3100 };
        line.sharedMaterial = _ring;

        var points = new Vector3[48];
        for (float t = 0f; t < 1.4f; t += Time.unscaledDeltaTime)
        {
            float k = t / 1.4f;
            float r = Mathf.Lerp(7f, 24f, 1f - (1f - k) * (1f - k));
            Vector3 c = transform.position;
            for (int i = 0; i < 48; i++)
            {
                float a = i * Mathf.PI * 2f / 48f;
                float x = c.x + Mathf.Cos(a) * r, z = c.z + Mathf.Sin(a) * r;
                points[i] = new Vector3(x, SeaWaves.Displacement(x, z, SeaWaves.Now).y + 0.3f, z);
            }
            line.SetPositions(points);
            Color col = new Color(1f, 0.85f, 0.35f, 0.9f * (1f - k));
            line.startColor = line.endColor = col;
            yield return null;
        }
        Destroy(go);
    }
}
