using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

/// <summary>
/// OpenXR feature that loads Valve Index controller render models via XR_EXT_render_model.
/// Enable this in Project Settings > XR Plug-in Management > OpenXR > Features.
/// </summary>
[Serializable]
#if UNITY_EDITOR
[OpenXRFeature(
    UiName               = "Controller Model Provider",
    BuildTargetGroups    = new[] { BuildTargetGroup.Standalone },
    Company              = "ChaperoneTweak",
    Desc                 = "Loads controller render models via XR_EXT_render_model.",
    Version              = "0.0.1",
    FeatureId            = "com.chaperoneTweak.controllermodel")]
#endif
public class ControllerModelFeature : OpenXRFeature
{
    // ── Public API ────────────────────────────────────────────────────────────
    public static Mesh[] LeftMeshes  { get; private set; }
    public static Mesh[] RightMeshes { get; private set; }
    public static event Action OnModelsLoaded;

    // ── XR_EXT_render_model constants ─────────────────────────────────────────
    const uint XR_TYPE_RENDER_MODEL_PATH_INFO_EXT  = 1000119000;
    const uint XR_TYPE_RENDER_MODEL_PROPERTIES_EXT = 1000119001;
    const uint XR_TYPE_RENDER_MODEL_BUFFER_EXT     = 1000119002;
    const uint XR_TYPE_RENDER_MODEL_LOAD_INFO_EXT  = 1000119003;

    // Struct sizes and field offsets (64-bit, matching C standard alignment).
    // All OpenXR structs start with:  uint32 type | uint32 padding | void* next
    //
    // XrRenderModelPathInfoEXT:  type(4)+pad(4)+next(8)+path(8)         = 24
    const int PI_SIZE      = 24, PI_PATH   = 16;
    // XrRenderModelPropertiesEXT: type(4)+pad(4)+next(8)+vendorId(4)+name[256]+pad(4)+modelKey(8)+ver(4)+pad(4)+flags(8) = 304
    const int PR_SIZE      = 304, PR_KEY   = 280;
    // XrRenderModelLoadInfoEXT:  type(4)+pad(4)+next(8)+modelKey(8)     = 24
    const int LI_SIZE      = 24, LI_KEY   = 16;
    // XrRenderModelBufferEXT:   type(4)+pad(4)+next(8)+capIn(4)+countOut(4)+buf*(8) = 32
    const int BUF_SIZE     = 32, BUF_CAP  = 16, BUF_CNT = 20, BUF_PTR = 24;

    // ── Delegate types ────────────────────────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int PfnGetProcAddr(ulong instance, [MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr func);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int PfnEnumeratePaths(ulong session, uint capIn, out uint countOut, IntPtr paths);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int PfnGetProperties(ulong session, ulong path, IntPtr props);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int PfnLoadModel(ulong session, IntPtr loadInfo, IntPtr buffer);

    ulong              _xrSession;
    PfnEnumeratePaths  _enumPaths;
    PfnGetProperties   _getProps;
    PfnLoadModel       _loadModel;
    bool               _loaded;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    protected override bool OnInstanceCreate(ulong xrInstance)
    {
        // Probe for the extension functions via xrGetInstanceProcAddr regardless of whether
        // the extension was formally requested — SteamVR exposes the functions anyway.
        var getProcAddr = Marshal.GetDelegateForFunctionPointer<PfnGetProcAddr>(xrGetInstanceProcAddr);
        IntPtr ptr;

        if (getProcAddr(xrInstance, "xrEnumerateRenderModelPathsEXT", out ptr) == 0 && ptr != IntPtr.Zero)
            _enumPaths = Marshal.GetDelegateForFunctionPointer<PfnEnumeratePaths>(ptr);
        if (getProcAddr(xrInstance, "xrGetRenderModelPropertiesEXT",  out ptr) == 0 && ptr != IntPtr.Zero)
            _getProps  = Marshal.GetDelegateForFunctionPointer<PfnGetProperties>(ptr);
        if (getProcAddr(xrInstance, "xrLoadRenderModelEXT",            out ptr) == 0 && ptr != IntPtr.Zero)
            _loadModel = Marshal.GetDelegateForFunctionPointer<PfnLoadModel>(ptr);

        bool ok = _enumPaths != null && _getProps != null && _loadModel != null;
        Debug.Log("[CtrlModel] XR_EXT_render_model functions resolved: " + ok);
        return true;
    }

    protected override void OnSessionBegin(ulong xrSession)
    {
        _xrSession = xrSession;
        if (!_loaded) TryLoad();
    }

    // ── Model loading ─────────────────────────────────────────────────────────
    void TryLoad()
    {
        if (_enumPaths == null) return;
        try { LoadModels(); }
        catch (Exception e) { Debug.LogError("[CtrlModel] Exception: " + e); }
    }

    void LoadModels()
    {
        // 1. How many paths?
        _enumPaths(_xrSession, 0, out uint pathCount, IntPtr.Zero);
        if (pathCount == 0) { Debug.Log("[CtrlModel] No render model paths"); return; }

        IntPtr pathsBuf = Marshal.AllocHGlobal((int)pathCount * PI_SIZE);
        try
        {
            // Initialize each XrRenderModelPathInfoEXT
            for (int i = 0; i < (int)pathCount; i++)
            {
                IntPtr p = pathsBuf + i * PI_SIZE;
                Zero(p, PI_SIZE);
                W32(p, 0, XR_TYPE_RENDER_MODEL_PATH_INFO_EXT);
            }
            _enumPaths(_xrSession, pathCount, out _, pathsBuf);

            Mesh[] left = null, right = null;

            for (int i = 0; i < (int)pathCount; i++)
            {
                ulong  xrPath  = R64(pathsBuf + i * PI_SIZE, PI_PATH);
                string pathStr = PathToString(xrPath) ?? "";
                Debug.Log("[CtrlModel] path[" + i + "] = " + pathStr);

                bool isLeft  = pathStr.Contains("left");
                bool isRight = pathStr.Contains("right");
                if (!isLeft && !isRight) continue;

                var meshes = LoadMeshesForPath(xrPath);
                if (meshes != null)
                {
                    if (isLeft)  left  = meshes;
                    if (isRight) right = meshes;
                }
            }

            if (left != null || right != null)
            {
                LeftMeshes  = left;
                RightMeshes = right;
                _loaded     = true;
                OnModelsLoaded?.Invoke();
                Debug.Log("[CtrlModel] Models loaded — L=" + (left?.Length ?? 0) + " R=" + (right?.Length ?? 0));
            }
        }
        finally { Marshal.FreeHGlobal(pathsBuf); }
    }

    Mesh[] LoadMeshesForPath(ulong xrPath)
    {
        // 2. Get modelKey from properties
        IntPtr propsBuf = Marshal.AllocHGlobal(PR_SIZE);
        try
        {
            Zero(propsBuf, PR_SIZE);
            W32(propsBuf, 0, XR_TYPE_RENDER_MODEL_PROPERTIES_EXT);
            if (_getProps(_xrSession, xrPath, propsBuf) != 0) return null;
            ulong modelKey = R64(propsBuf, PR_KEY);
            if (modelKey == 0) return null;
            return LoadMeshesForKey(modelKey);
        }
        finally { Marshal.FreeHGlobal(propsBuf); }
    }

    Mesh[] LoadMeshesForKey(ulong modelKey)
    {
        // 3. Two-call to get GLB data size then data
        IntPtr loadInfo = Marshal.AllocHGlobal(LI_SIZE);
        IntPtr bufStruct = Marshal.AllocHGlobal(BUF_SIZE);
        try
        {
            Zero(loadInfo, LI_SIZE);
            W32(loadInfo, 0, XR_TYPE_RENDER_MODEL_LOAD_INFO_EXT);
            W64(loadInfo, LI_KEY, modelKey);

            Zero(bufStruct, BUF_SIZE);
            W32(bufStruct, 0, XR_TYPE_RENDER_MODEL_BUFFER_EXT);
            W32(bufStruct, BUF_CAP, 0);

            int res = _loadModel(_xrSession, loadInfo, bufStruct);
            // XR_SUCCESS (0) or XR_ERROR_SIZE_INSUFFICIENT (-4) both fill bufferCountOutput
            if (res != 0 && res != -4) { Debug.Log("[CtrlModel] LoadModel size query: " + res); return null; }

            uint dataSize = R32(bufStruct, BUF_CNT);
            if (dataSize == 0) return null;

            IntPtr dataBuf = Marshal.AllocHGlobal((int)dataSize);
            try
            {
                W32(bufStruct, BUF_CAP, dataSize);
                WPtr(bufStruct, BUF_PTR, dataBuf);

                res = _loadModel(_xrSession, loadInfo, bufStruct);
                if (res != 0) { Debug.Log("[CtrlModel] LoadModel data: " + res); return null; }

                byte[] glb = new byte[dataSize];
                Marshal.Copy(dataBuf, glb, 0, (int)dataSize);
                return GlbMeshLoader.Load(glb);
            }
            finally { Marshal.FreeHGlobal(dataBuf); }
        }
        finally
        {
            Marshal.FreeHGlobal(loadInfo);
            Marshal.FreeHGlobal(bufStruct);
        }
    }

    // ── Raw memory helpers ────────────────────────────────────────────────────
    static void Zero(IntPtr p, int len)
    {
        for (int i = 0; i < len; i++) Marshal.WriteByte(p, i, 0);
    }
    static void  W32(IntPtr p, int off, uint v)   => Marshal.WriteInt32(p + off, (int)v);
    static void  W64(IntPtr p, int off, ulong v)  => Marshal.WriteInt64(p + off, (long)v);
    static void  WPtr(IntPtr p, int off, IntPtr v) => Marshal.WriteIntPtr(p + off, v);
    static uint  R32(IntPtr p, int off)            => (uint)Marshal.ReadInt32(p + off);
    static ulong R64(IntPtr p, int off)            => (ulong)Marshal.ReadInt64(p + off);
}
