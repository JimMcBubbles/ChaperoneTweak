using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Valve.VR;

// Reads and writes chaperone_info.vrchap directly so the Unity process never
// calls OpenVR.Init (which conflicts with the OpenXR session).
// SteamVR watches the file and auto-reloads chaperone when it changes.
public static class ChaperoneFileIO
{
    static string FilePath()
    {
        // Check common Steam installation locations
        string[] candidates = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam", "config", "chaperone_info.vrchap"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Steam", "config", "chaperone_info.vrchap"),
            @"C:\Steam\config\chaperone_info.vrchap",
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        // Fall back to searching vrpathregistry for the Steam install path
        try
        {
            var vrPathReg = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "openvr", "openvrpaths.vrpath");
            if (File.Exists(vrPathReg))
            {
                var text = File.ReadAllText(vrPathReg);
                // Simple extraction: find "config" : [ "path" ]
                var jo = JObject.Parse(text);
                var configArr = jo["config"] as JArray;
                if (configArr != null)
                {
                    foreach (var entry in configArr)
                    {
                        var p = Path.Combine(entry.ToString(), "chaperone_info.vrchap");
                        if (File.Exists(p)) return p;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public static bool Load(out HmdMatrix34_t mat, out float playW, out float playH, out HmdQuad_t[] quads)
    {
        mat = default; playW = 0; playH = 0; quads = null;

        var path = FilePath();
        if (path == null) { Debug.Log("chaperone_info.vrchap not found"); return false; }

        JObject root;
        try { root = JObject.Parse(File.ReadAllText(path)); }
        catch (Exception e) { Debug.Log("chaperone JSON parse error: " + e.Message); return false; }

        var universe = root["universes"]?[0];
        if (universe == null) { Debug.Log("No universe in chaperone file"); return false; }

        var standing = universe["standing"];
        if (standing == null) { Debug.Log("No standing section"); return false; }

        var trans = standing["translation"];
        float tx = (float)trans[0], ty = (float)trans[1], tz = (float)trans[2];
        float yaw = (float)standing["yaw"];

        float cy = Mathf.Cos(yaw), sy = Mathf.Sin(yaw);
        mat.m0 =  cy; mat.m1 = 0f; mat.m2 =  sy; mat.m3 =  tx;
        mat.m4 =  0f; mat.m5 = 1f; mat.m6 =  0f; mat.m7 =  ty;
        mat.m8 = -sy; mat.m9 = 0f; mat.m10 = cy; mat.m11 = tz;

        var pa = universe["play_area"];
        playW = (float)pa[0];
        playH = (float)pa[1];

        var bounds = universe["collision_bounds"] as JArray;
        if (bounds == null) { Debug.Log("No collision_bounds"); return false; }

        quads = new HmdQuad_t[bounds.Count];
        for (int i = 0; i < bounds.Count; i++)
        {
            var quad = bounds[i] as JArray;
            quads[i].vCorners0 = ToVec(quad[0]);
            quads[i].vCorners1 = ToVec(quad[1]);
            quads[i].vCorners2 = ToVec(quad[2]);
            quads[i].vCorners3 = ToVec(quad[3]);
        }

        return true;
    }

    public static bool Save(HmdMatrix34_t mat, float playW, float playH, HmdQuad_t[] quads)
    {
        var path = FilePath();
        if (path == null) { Debug.Log("chaperone_info.vrchap not found"); return false; }

        JObject root;
        try { root = JObject.Parse(File.ReadAllText(path)); }
        catch (Exception e) { Debug.Log("chaperone JSON parse error: " + e.Message); return false; }

        var universe = root["universes"]?[0] as JObject;
        if (universe == null) { Debug.Log("No universe in chaperone file"); return false; }

        float yaw = Mathf.Atan2(mat.m2, mat.m0);
        float tx = mat.m3, ty = mat.m7, tz = mat.m11;

        universe["standing"] = new JObject(
            new JProperty("translation", new JArray(tx, ty, tz)),
            new JProperty("yaw", yaw)
        );

        universe["play_area"] = new JArray(playW, playH);

        var boundsArr = new JArray();
        foreach (var q in quads)
        {
            boundsArr.Add(new JArray(
                FromVec(q.vCorners0),
                FromVec(q.vCorners1),
                FromVec(q.vCorners2),
                FromVec(q.vCorners3)
            ));
        }
        universe["collision_bounds"] = boundsArr;
        universe["time"] = DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy");

        File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.Indented));
        return true;
    }

    static HmdVector3_t ToVec(JToken t) =>
        new HmdVector3_t { v0 = (float)t[0], v1 = (float)t[1], v2 = (float)t[2] };

    static JArray FromVec(HmdVector3_t v) => new JArray(v.v0, v.v1, v.v2);
}
