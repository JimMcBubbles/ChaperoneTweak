using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public static class GlbMeshLoader
{
    const uint GLB_MAGIC   = 0x46546C67; // "glTF"
    const uint CHUNK_JSON  = 0x4E4F534A; // "JSON"
    const uint CHUNK_BIN   = 0x004E4942; // "BIN\0"

    public static Mesh[] Load(string path)
    {
        if (!File.Exists(path)) return null;
        try   { return ParseGlb(File.ReadAllBytes(path)); }
        catch (Exception e) { Debug.LogWarning("[GlbMeshLoader] Failed to load " + path + ": " + e.Message); return null; }
    }

    public static Mesh[] Load(byte[] data)
    {
        if (data == null || data.Length == 0) return null;
        try   { return ParseGlb(data); }
        catch (Exception e) { Debug.LogWarning("[GlbMeshLoader] Failed to parse GLB bytes: " + e.Message); return null; }
    }

    static Mesh[] ParseGlb(byte[] bytes)
    {
        if (bytes.Length < 12) return null;
        if (BitConverter.ToUInt32(bytes, 0) != GLB_MAGIC) return null;

        byte[] jsonChunk = null, binChunk = null;
        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            int  chunkLen  = (int)BitConverter.ToUInt32(bytes, offset);
            uint chunkType = BitConverter.ToUInt32(bytes, offset + 4);
            offset += 8;
            if (offset + chunkLen > bytes.Length) break;

            if (chunkType == CHUNK_JSON && jsonChunk == null)
            {
                jsonChunk = new byte[chunkLen];
                Buffer.BlockCopy(bytes, offset, jsonChunk, 0, chunkLen);
            }
            else if (chunkType == CHUNK_BIN && binChunk == null)
            {
                binChunk = new byte[chunkLen];
                Buffer.BlockCopy(bytes, offset, binChunk, 0, chunkLen);
            }
            offset += chunkLen;
        }

        if (jsonChunk == null) return null;

        var gltf        = JObject.Parse(Encoding.UTF8.GetString(jsonChunk));
        var accessors   = (JArray)gltf["accessors"];
        var bufferViews = (JArray)gltf["bufferViews"];
        var gltfMeshes  = (JArray)gltf["meshes"];
        if (gltfMeshes == null || accessors == null || bufferViews == null) return null;

        var result = new List<Mesh>();
        foreach (JToken gm in gltfMeshes)
        {
            var primitives = (JArray)gm["primitives"];
            if (primitives == null) continue;
            foreach (JToken prim in primitives)
            {
                var attrs = prim["attributes"];
                if (attrs == null) continue;

                int posIdx  = attrs["POSITION"]?.Value<int>() ?? -1;
                int normIdx = attrs["NORMAL"]?.Value<int>()     ?? -1;
                int uvIdx   = attrs["TEXCOORD_0"]?.Value<int>() ?? -1;
                int idxIdx  = prim["indices"]?.Value<int>()     ?? -1;
                if (posIdx < 0 || idxIdx < 0) continue;

                Vector3[] verts = ReadVec3(accessors[posIdx],  bufferViews, binChunk, flipZ: true);
                Vector3[] norms = normIdx >= 0 ? ReadVec3(accessors[normIdx], bufferViews, binChunk, flipZ: true) : null;
                Vector2[] uvs   = uvIdx   >= 0 ? ReadVec2(accessors[uvIdx],   bufferViews, binChunk) : null;
                int[]     tris  = ReadIndices(accessors[idxIdx], bufferViews, binChunk);
                if (verts == null || tris == null) continue;

                // GLTF is right-handed; flipping Z mirrors the mesh, so flip winding order.
                for (int i = 0; i < tris.Length; i += 3)
                    (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);

                var mesh = new Mesh { name = gm["name"]?.Value<string>() ?? "glb" };
                if (verts.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.vertices  = verts;
                if (norms != null) mesh.normals = norms;
                if (uvs   != null) mesh.uv      = uvs;
                mesh.triangles = tris;
                if (norms == null) mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                result.Add(mesh);
            }
        }
        return result.Count > 0 ? result.ToArray() : null;
    }

    static Vector3[] ReadVec3(JToken accessor, JArray bufferViews, byte[] bin, bool flipZ)
    {
        if (accessor == null || bin == null) return null;
        int count  = accessor["count"].Value<int>();
        int start  = DataStart(accessor, bufferViews);
        int stride = DataStride(accessor, bufferViews, 12);
        if (start < 0) return null;

        var r = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            int o = start + i * stride;
            r[i] = new Vector3(
                BitConverter.ToSingle(bin, o),
                BitConverter.ToSingle(bin, o + 4),
                flipZ ? -BitConverter.ToSingle(bin, o + 8) : BitConverter.ToSingle(bin, o + 8));
        }
        return r;
    }

    static Vector2[] ReadVec2(JToken accessor, JArray bufferViews, byte[] bin)
    {
        if (accessor == null || bin == null) return null;
        int count  = accessor["count"].Value<int>();
        int start  = DataStart(accessor, bufferViews);
        int stride = DataStride(accessor, bufferViews, 8);
        if (start < 0) return null;

        var r = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            int o = start + i * stride;
            r[i] = new Vector2(BitConverter.ToSingle(bin, o), BitConverter.ToSingle(bin, o + 4));
        }
        return r;
    }

    static int[] ReadIndices(JToken accessor, JArray bufferViews, byte[] bin)
    {
        if (accessor == null || bin == null) return null;
        int count    = accessor["count"].Value<int>();
        int compType = accessor["componentType"].Value<int>();
        int start    = DataStart(accessor, bufferViews);
        if (start < 0) return null;

        var r = new int[count];
        switch (compType)
        {
            case 5121: for (int i = 0; i < count; i++) r[i] = bin[start + i]; break;           // UNSIGNED_BYTE
            case 5123: for (int i = 0; i < count; i++) r[i] = BitConverter.ToUInt16(bin, start + i * 2); break; // UNSIGNED_SHORT
            case 5125: for (int i = 0; i < count; i++) r[i] = (int)BitConverter.ToUInt32(bin, start + i * 4); break; // UNSIGNED_INT
            default:   return null;
        }
        return r;
    }

    static int DataStart(JToken accessor, JArray bufferViews)
    {
        var bvToken = accessor["bufferView"];
        if (bvToken == null || bvToken.Type == JTokenType.Null) return -1;
        int bvIdx     = bvToken.Value<int>();
        int bvOffset  = bufferViews[bvIdx]["byteOffset"]?.Value<int>() ?? 0;
        int accOffset = accessor["byteOffset"]?.Value<int>() ?? 0;
        return bvOffset + accOffset;
    }

    static int DataStride(JToken accessor, JArray bufferViews, int defaultStride)
    {
        var bvToken = accessor["bufferView"];
        if (bvToken == null || bvToken.Type == JTokenType.Null) return defaultStride;
        int bvIdx = bvToken.Value<int>();
        return bufferViews[bvIdx]["byteStride"]?.Value<int>() ?? defaultStride;
    }
}
