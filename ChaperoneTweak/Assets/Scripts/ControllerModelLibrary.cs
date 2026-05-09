using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Maps XR device name substrings to GLB filenames in StreamingAssets/Controllers/.
/// Each entry has a left and right filename; pass null for controllers with no handedness.
/// </summary>
public static class ControllerModelLibrary
{
    // (deviceNameSubstring, leftFile, rightFile)
    // Checked in order — first match wins.
    static readonly (string key, string left, string right)[] _table =
    {
        ("Index",            "valve-index-left.glb",          "valve-index-right.glb"),
        ("Knuckles",         "valve-index-left.glb",          "valve-index-right.glb"),
        ("pico 4",           "pico4-left.glb",                "pico4-right.glb"),
        ("pico4",            "pico4-left.glb",                "pico4-right.glb"),
        ("quest touch pro",  "meta-quest-pro-left.glb",       "meta-quest-pro-right.glb"),
        ("quest touch plus", "meta-quest-plus-left.glb",      "meta-quest-plus-right.glb"),
        ("quest touch",      "meta-quest-plus-left.glb",      "meta-quest-plus-right.glb"),
        ("oculus touch",     "oculus-touch-v3-left.glb",      "oculus-touch-v3-right.glb"),
        ("meta",             "meta-quest-plus-left.glb",      "meta-quest-plus-right.glb"),
        ("hp reverb",        "hp-mr-left.glb",                "hp-mr-right.glb"),
        ("hp mixed",         "hp-mr-left.glb",                "hp-mr-right.glb"),
        ("045e-065d",        "microsoft-065d-left.glb",       "microsoft-065d-right.glb"),
        ("065d",             "microsoft-065d-left.glb",       "microsoft-065d-right.glb"),
        ("045e-065b",        "microsoft-065b-left.glb",       "microsoft-065b-right.glb"),
        ("065b",             "microsoft-065b-left.glb",       "microsoft-065b-right.glb"),
        ("windows mixed",    "microsoft-wmr-left.glb",        "microsoft-wmr-right.glb"),
        ("windows mr",       "microsoft-wmr-left.glb",        "microsoft-wmr-right.glb"),
        ("microsoft",        "microsoft-wmr-left.glb",        "microsoft-wmr-right.glb"),
        ("magic leap",       "magicleap-one.glb",             "magicleap-one.glb"),
        ("logitech",         "logitech-mx-ink.glb",           "logitech-mx-ink.glb"),
    };

    static string _dir;
    static string Dir => _dir ??= Path.Combine(Application.streamingAssetsPath, "Controllers");

    /// <summary>
    /// Returns loaded meshes for the best-matching controller model, or null if none found.
    /// </summary>
    public static Mesh[] GetMeshes(string deviceName, bool isLeft)
    {
        if (string.IsNullOrEmpty(deviceName)) return null;
        string lower = deviceName.ToLowerInvariant();

        foreach (var (key, left, right) in _table)
        {
            if (!lower.Contains(key.ToLowerInvariant())) continue;
            string file = isLeft ? left : right;
            if (string.IsNullOrEmpty(file)) continue;

            string path = Path.Combine(Dir, file);
            var meshes = GlbMeshLoader.Load(path);
            if (meshes != null && meshes.Length > 0)
            {
                Debug.Log("[CtrlLib] " + deviceName + " → " + file + " (" + meshes.Length + " meshes)");
                return meshes;
            }
            Debug.LogWarning("[CtrlLib] Matched " + file + " for " + deviceName + " but load failed");
            return null;
        }

        Debug.Log("[CtrlLib] No model match for: " + deviceName);
        return null;
    }
}
