using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SineusArenaVersus.Game;

/// <summary>
/// Stable per-match sender colors / short labels (P1..P4) for injected packs.
/// </summary>
public static class VersusSenderStyle
{
    private static readonly Color[] SlotColors =
    {
        new(0.25f, 0.85f, 1f, 1f),   // cyan
        new(1f, 0.45f, 0.2f, 1f),    // orange
        new(0.55f, 1f, 0.35f, 1f),   // lime
        new(1f, 0.35f, 0.85f, 1f),   // magenta
    };

    public static int SlotIndex(IReadOnlyList<ulong> peerOrder, ulong peerId)
    {
        for (var i = 0; i < peerOrder.Count; i++)
        {
            if (peerOrder[i] == peerId)
                return i;
        }

        return Math.Abs(peerId.GetHashCode()) % SlotColors.Length;
    }

    public static Color ColorForSlot(int slot) =>
        SlotColors[Math.Abs(slot) % SlotColors.Length];

    public static string ShortLabel(int slot, string? displayName)
    {
        var prefix = $"P{slot + 1}";
        if (string.IsNullOrWhiteSpace(displayName))
            return prefix;

        var trimmed = displayName!.Trim();
        if (trimmed.Length <= 12)
            return $"{prefix} {trimmed}";
        return $"{prefix} {trimmed.Substring(0, 11)}...";
    }

    public static IReadOnlyList<ulong> OrderedPeers(IEnumerable<ulong> peers) =>
        peers.Distinct().OrderBy(id => id).ToArray();
}
