using System;
using SineusArenaVersus.Match;
using Steamworks;
using UnityEngine;

namespace SineusArenaVersus.Hud;

public static class RivalCardView
{
    public static void Draw(Rect rect, PeerState peer, string displayName)
    {
        var previousColor = GUI.color;
        GUI.color = peer.IsAlive ? previousColor : new Color(0.55f, 0.55f, 0.55f, 1f);

        GUI.Box(rect, GUIContent.none);
        var inner = Inset(rect, 6f);
        GUI.Label(new Rect(inner.x, inner.y, inner.width, 18f), displayName);

        var barRect = new Rect(inner.x, inner.y + 22f, inner.width, 14f);
        GUI.Box(barRect, GUIContent.none);
        var fillWidth = barRect.width * Mathf.Clamp01(peer.StrongholdHp01);
        if (fillWidth > 0f)
        {
            var fillRect = new Rect(barRect.x + 1f, barRect.y + 1f, fillWidth - 2f, barRect.height - 2f);
            var fillColor = peer.IsAlive ? new Color(0.25f, 0.75f, 0.35f) : new Color(0.35f, 0.35f, 0.35f);
            DrawSolidRect(fillRect, fillColor);
        }

        var status = peer.IsAlive ? $"{Mathf.RoundToInt(peer.StrongholdHp01 * 100f)}%" : "Eliminated";
        GUI.Label(new Rect(inner.x, inner.y + 40f, inner.width, 16f), status);
        GUI.color = previousColor;
    }

    public static string FormatPeerName(ulong peerId)
    {
        try
        {
            if (SteamClient.IsValid)
            {
                var name = new Friend(peerId).Name;
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch (Exception exception)
        {
            VersusPlugin.Log.LogDebug($"Steam name lookup failed for {peerId}: {exception.Message}");
        }

        return $"Peer {peerId % 10000UL:D4}";
    }

    private static Rect Inset(Rect rect, float padding) =>
        new(rect.x + padding, rect.y + padding, rect.width - padding * 2f, rect.height - padding * 2f);

    private static void DrawSolidRect(Rect rect, Color color)
    {
        var previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previousColor;
    }
}
