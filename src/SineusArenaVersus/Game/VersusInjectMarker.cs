using UnityEngine;

namespace SineusArenaVersus.Game;

/// <summary>
/// World marker on Versus-injected enemies: tinted light + billboard label of the sender.
/// </summary>
public sealed class VersusInjectMarker : MonoBehaviour
{
    private const float LightHeight = 1.6f;
    private const float LabelHeight = 2.1f;
    private const float MaxLabelDistance = 48f;

    private string _label = "P?";
    private Color _color = Color.white;
    private Light? _light;

    public static void Attach(GameObject host, string label, Color color)
    {
        if (host is null)
            return;

        var marker = host.GetComponent<VersusInjectMarker>();
        if (marker is null)
            marker = host.AddComponent<VersusInjectMarker>();

        marker.Configure(label, color);
    }

    public void Configure(string label, Color color)
    {
        _label = string.IsNullOrWhiteSpace(label) ? "P?" : label;
        _color = color;
        EnsureLight();
        if (_light is not null)
        {
            _light.color = _color;
            _light.enabled = VersusConfig.ShowInjectSenderLights.Value;
        }
    }

    private void EnsureLight()
    {
        if (_light is not null)
            return;

        var lightGo = new GameObject("VersusSenderLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, LightHeight, 0f);
        _light = lightGo.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.range = VersusConfig.InjectMarkerLightRange.Value;
        _light.intensity = VersusConfig.InjectMarkerLightIntensity.Value;
        _light.shadows = LightShadows.None;
    }

    private void LateUpdate()
    {
        if (_light is not null)
            _light.enabled = VersusConfig.ShowInjectSenderLights.Value;
    }

    private void OnGUI()
    {
        if (!VersusConfig.ShowInjectSenderLabels.Value)
            return;

        var cam = Camera.main;
        if (cam is null)
            return;

        var world = transform.position + Vector3.up * LabelHeight;
        var screen = cam.WorldToScreenPoint(world);
        if (screen.z <= 0f)
            return;

        var distance = Vector3.Distance(cam.transform.position, world);
        if (distance > MaxLabelDistance)
            return;

        var guiPoint = new Vector2(screen.x, Screen.height - screen.y);
        var size = new Vector2(110f, 22f);
        var rect = new Rect(guiPoint.x - size.x * 0.5f, guiPoint.y - size.y, size.x, size.y);

        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.Box(rect, GUIContent.none);
        GUI.color = _color;
        GUI.Label(rect, _label);
        GUI.color = prev;
    }
}
