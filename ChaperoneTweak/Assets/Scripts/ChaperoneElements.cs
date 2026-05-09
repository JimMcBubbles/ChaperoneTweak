using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Valve.VR;

public enum TweakActionType { wHeight, wEdgePosition, wAdd, wRemove, psEdgeResize, psSetFront, psHeight, psPivotScale, psPivot, psMove, save, reload, menu, none }
public enum TweakActionArea { topLeft, top, topRight, left, middle, right, bottomLeft, bottom, bottomRight, none }

public class ChaperoneElements : MonoBehaviour
{

    // Don't shrink wall height or playscale width/length below this size
    public float MinimumScale = 0.10f;

    // Snap wall corners to 90° when within this distance (metres) of the perfect position
    public float CornerSnapDistance = 0.15f;
    // Snap play space rotation to 90° increments when within this many degrees
    public float RotationSnapDegrees = 15f;

    // used for the assorted Tweak functions
    private TweakActionArea TweakArea;
    private TweakActionType TweakAction = TweakActionType.none;
    private Transform TweakControllerTrans;
    private float TweakInitScaleF;
    private float TweakMinScale;
    private float TweakYawDelta;
    private GameObject TweakLeftWall;
    private GameObject TweakRightWall;
    private Vector3 TweakLeftPivotPoint;
    private Vector3 TweakRightPivotPoint;
    private Vector3 TweakTargetInitPoint;
    private Vector3 TweakPivotPoint;
    private Vector3 TweakInitScaleV;
    private Vector3 TweakControllerInitPos;

    private bool ChaperoneLoading = false;
    private bool ChaperoneSaving = false;
    private Coroutine _boundsCheckCoroutine;
    private GameObject _outOfBoundsMessage;
    private ChaperonePlaneProperties PlaySpace = null;
    private ChaperonePlaneProperties FirstWall = null;
    private int WallCount = 0;

    public TweakActionType CurrentAction { get { return TweakAction; } }
    public Material WallMaterial, PlaySpaceMaterial;
    public Transform Head;

    public void SetMaterials(Material wallmat, Material playspacemat)
    {
        WallMaterial = wallmat;
        PlaySpaceMaterial = playspacemat;
        ChaperonePlaneProperties wall = FirstWall;
        for (int i = 0; i < WallCount; i++)
        {
            wall.gameObject.GetComponent<MeshRenderer>().material = WallMaterial;
            wall = wall.RightWall;
        }
        PlaySpace.gameObject.GetComponent<MeshRenderer>().material = PlaySpaceMaterial;
    }

    public ChaperonePlaneProperties CreatePlane(string planename, bool iswall, Material planeMat)
    {
        GameObject go = new GameObject(planename);
        ChaperonePlaneProperties props = go.AddComponent<ChaperonePlaneProperties>();
        MeshFilter mf = go.AddComponent(typeof(MeshFilter)) as MeshFilter;
        MeshRenderer mr = go.AddComponent(typeof(MeshRenderer)) as MeshRenderer;
        MeshCollider mc = go.AddComponent(typeof(MeshCollider)) as MeshCollider;
        Mesh m = new Mesh();

        if (!iswall)
        {
            go.layer = 9;
            m.vertices = new Vector3[] { new Vector3(-0.5f, 0, -0.5f),
                new Vector3(-0.5f, 0, 0.5f),
                new Vector3(0.5f, 0, 0.5f),
                new Vector3(0.5f, 0, -0.5f) };

            m.triangles = new int[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            m.uv = new Vector2[]
            {
                new Vector2(0,0),
                new Vector2(0,1),
                new Vector2(1,1),
                new Vector2(1,0)
            };
        }
        else
        {
            go.layer = 8;
            m.vertices = new Vector3[] { new Vector3(0.5f, 0, 0f),
                new Vector3(0.5f, 1, 0f),
                new Vector3(-0.5f, 1, 0f),
                new Vector3(-0.5f, 0, 0f) };

            m.triangles = new int[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
            m.uv = new Vector2[]
            {
                new Vector2(1,0),
                new Vector2(1,1),
                new Vector2(0,1),
                new Vector2(0,0)
            };
        }
        mf.mesh = m;
        mr.material = planeMat;
        m.RecalculateNormals();
        m.RecalculateBounds();
        mc.sharedMesh = m;
        return props;
    }

    public void ReloadChaperone()
    {
        TweakAction = TweakActionType.reload;
        Vector3 v0;
        Vector3 v1;
        Vector3 v3;
        float x = 0;
        float y = 0;
        HmdMatrix34_t mat = new HmdMatrix34_t();
        HmdQuad_t[] quads;
        ChaperonePlaneProperties wall, prevwall;
        ChaperoneLoading = true;

        if (!ChaperoneFileIO.Load(out mat, out x, out y, out quads))
        {
            ChaperoneLoading = false;
            TweakAction = TweakActionType.none;
            return;
        }

        // Destroy walls/playspace if they already exist
        if (PlaySpace != null)
        {
            Destroy(PlaySpace.gameObject);
            PlaySpace = null;
        }
        if (FirstWall != null)
        {
            wall = FirstWall;
            for (int i = 0; i < WallCount; i++)
            {
                prevwall = wall.RightWall;
                Destroy(wall.gameObject);
                wall = prevwall;

            }
            FirstWall = null;
            WallCount = 0;
        }

        //this object will become the origin point
        SteamVR_Utils.RigidTransform rt = new SteamVR_Utils.RigidTransform(mat);
        transform.position = rt.pos; //+ new Vector3(0, 1, 0);
        transform.rotation = rt.rot;
        transform.localScale = Vector3.one;

        //create the rectangular playspace object
        PlaySpace = CreatePlane("ChapPlaySpace", false, PlaySpaceMaterial);
        PlaySpace.transform.parent = transform;

        //set the playspace transform
        PlaySpace.transform.localScale = new Vector3(x, 1, y);
        PlaySpace.transform.localPosition = Vector3.zero;
        PlaySpace.transform.localRotation = Quaternion.identity;

        //Create the wall objects
        WallCount = quads.Length;
        prevwall = null;
        foreach (HmdQuad_t quad in quads)
        {
            wall = CreatePlane("ChapWall", true, WallMaterial);
            wall.transform.parent = transform;

            // convert the necessary corners to a usable format
            v0 = new Vector3(quad.vCorners0.v0, quad.vCorners0.v1, -quad.vCorners0.v2);
            v1 = new Vector3(quad.vCorners1.v0, quad.vCorners1.v1, -quad.vCorners1.v2);
            v3 = new Vector3(quad.vCorners3.v0, quad.vCorners3.v1, -quad.vCorners3.v2);

            // set the walls transform
            wall.transform.localPosition = Vector3.Lerp(v0, v3, 0.5f);
            wall.transform.localScale = new Vector3(Vector3.Distance(v0, v3), Vector3.Distance(v0, v1), 1);
            wall.transform.localRotation = Quaternion.LookRotation(v3 - v0, Vector3.up) * Quaternion.Euler(0, 90, 0);

            //each wall has a properties script with a link to the left and right wall. Also save a reference to the 1st wall
            if (prevwall != null)
            {
                prevwall.LeftWall = wall;
            }
            else
            {
                FirstWall = wall;
            }
            wall.RightWall = prevwall;
            prevwall = wall;

            //Link first wall with last wall
            FirstWall.RightWall = wall;
            wall.LeftWall = FirstWall;
        }
        ChaperoneLoading = false;
        TweakAction = TweakActionType.none;
    }

    public void SaveChaperone()
    {
        TweakAction = TweakActionType.save;
        ChaperoneSaving = true;

        SteamVR_Utils.RigidTransform rt = new SteamVR_Utils.RigidTransform();
        rt.pos = PlaySpace.transform.position;
        rt.rot = PlaySpace.transform.rotation;
        HmdMatrix34_t mat = rt.ToHmdMatrix34();

        HmdQuad_t[] pQuadsBuffer = new HmdQuad_t[WallCount];
        ChaperonePlaneProperties wall = FirstWall;
        for (int index = 0; index < WallCount; index++)
        {
            Vector3 wallcorner = Vector3.Scale(PlaySpace.transform.InverseTransformPoint(wall.transform.TransformPoint(new Vector3(0.5f, 0f, 0f))), PlaySpace.transform.localScale);
            pQuadsBuffer[index].vCorners0.v0 = wallcorner.x;
            pQuadsBuffer[index].vCorners0.v1 = wallcorner.y;
            pQuadsBuffer[index].vCorners0.v2 = -wallcorner.z;

            wallcorner = Vector3.Scale(PlaySpace.transform.InverseTransformPoint(wall.transform.TransformPoint(new Vector3(0.5f, 1f, 0f))), PlaySpace.transform.localScale);
            pQuadsBuffer[index].vCorners1.v0 = wallcorner.x;
            pQuadsBuffer[index].vCorners1.v1 = wallcorner.y;
            pQuadsBuffer[index].vCorners1.v2 = -wallcorner.z;

            wallcorner = Vector3.Scale(PlaySpace.transform.InverseTransformPoint(wall.transform.TransformPoint(new Vector3(-0.5f, 1f, 0f))), PlaySpace.transform.localScale);
            pQuadsBuffer[index].vCorners2.v0 = wallcorner.x;
            pQuadsBuffer[index].vCorners2.v1 = wallcorner.y;
            pQuadsBuffer[index].vCorners2.v2 = -wallcorner.z;

            wallcorner = Vector3.Scale(PlaySpace.transform.InverseTransformPoint(wall.transform.TransformPoint(new Vector3(-0.5f, 0f, 0f))), PlaySpace.transform.localScale);
            pQuadsBuffer[index].vCorners3.v0 = wallcorner.x;
            pQuadsBuffer[index].vCorners3.v1 = wallcorner.y;
            pQuadsBuffer[index].vCorners3.v2 = -wallcorner.z;

            wall = wall.LeftWall;
        }

        ChaperoneFileIO.Save(mat, PlaySpace.transform.localScale.x, PlaySpace.transform.localScale.z, pQuadsBuffer);
        ReloadChaperone();
        ChaperoneSaving = false;
    }

    // Use this for initialization
    void Start()
    {
        SetupControllerTracking();
        ReloadChaperone();
        StartBoundsCheck();
    }

    void StartBoundsCheck()
    {
        if (_boundsCheckCoroutine != null) StopCoroutine(_boundsCheckCoroutine);
        _boundsCheckCoroutine = StartCoroutine(OutOfBoundsCheck());
    }

    bool IsHmdInsidePlaySpace()
    {
        if (PlaySpace == null || Head == null) return true;
        Vector3 local = PlaySpace.transform.InverseTransformPoint(Head.position);
        return Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.z) <= 0.5f;
    }

    IEnumerator OutOfBoundsCheck()
    {
        yield return new WaitUntil(() => UnityEngine.Rendering.SplashScreen.isFinished);
        // Don't check bounds until the HMD is actually reporting valid tracking data.
        yield return new WaitUntil(() => {
            var devs = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, devs);
            return devs.Count > 0 && devs[0].isValid;
        });

        Debug.Log("[OOB] Initial bounds check: inside=" + IsHmdInsidePlaySpace()
            + " Head.pos=" + Head.position + " Head.localPos=" + Head.localPosition
            + " rig.pos=" + transform.position + " rig.rot=" + transform.rotation.eulerAngles
            + " PS.pos=" + PlaySpace.transform.position);
        if (IsHmdInsidePlaySpace()) yield break;

        ShowOutOfBoundsMessage(10);
        float timer = 10f;
        float logTimer = 0f;
        Vector3 lastGoodLocalPos = Head.localPosition;
        while (timer > 0f)
        {
            if (IsHmdInsidePlaySpace())
            {
                Debug.Log("[OOB] Player returned inside, cancelling recenter");
                HideOutOfBoundsMessage();
                yield break;
            }
            if (Head.localPosition != Vector3.zero)
                lastGoodLocalPos = Head.localPosition;
            UpdateOutOfBoundsMessage(Mathf.CeilToInt(timer));
            logTimer -= Time.deltaTime;
            if (logTimer <= 0f)
            {
                Debug.Log("[OOB] countdown=" + Mathf.CeilToInt(timer)
                    + " Head.pos=" + Head.position + " Head.localPos=" + Head.localPosition
                    + " lastGood=" + lastGoodLocalPos + " rig.pos=" + transform.position);
                logTimer = 2f;
            }
            yield return null;
            timer -= Time.deltaTime;
        }

        Debug.Log("[OOB] Timer expired, recentering. lastGoodLocalPos=" + lastGoodLocalPos);
        HideOutOfBoundsMessage();
        RecenterOnPlayer(lastGoodLocalPos);
    }

    void ShowOutOfBoundsMessage(int seconds)
    {
        if (_outOfBoundsMessage != null) return;
        _outOfBoundsMessage = new GameObject("OutOfBoundsMsg");
        _outOfBoundsMessage.transform.SetParent(Head, false);
        _outOfBoundsMessage.transform.localPosition = new Vector3(0, 0, 0.5f);
        // Rotate 180° so the TextMesh face (-Z) points back toward the camera (+Z).
        // Euler(0,180,0) makes the face visible but mirrors X. Negative X scale cancels the mirror.
        _outOfBoundsMessage.transform.localRotation = Quaternion.Euler(0, 180, 0);
        _outOfBoundsMessage.transform.localScale = new Vector3(-1, 1, 1);
        var tm = _outOfBoundsMessage.AddComponent<TextMesh>();
        tm.text = "Outside play space\nRecentering in " + seconds + "s";
        tm.characterSize = 0.04f;
        tm.fontSize = 40;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.red;
    }

    void UpdateOutOfBoundsMessage(int seconds)
    {
        if (_outOfBoundsMessage == null) return;
        var tm = _outOfBoundsMessage.GetComponent<TextMesh>();
        if (tm != null) tm.text = "Outside play space\nRecentering in " + seconds + "s";
    }

    void HideOutOfBoundsMessage()
    {
        if (_outOfBoundsMessage != null)
        {
            Destroy(_outOfBoundsMessage);
            _outOfBoundsMessage = null;
        }
    }

    void RecenterOnPlayer(Vector3 cachedHmdLocalPos)
    {
        if (Head == null || PlaySpace == null || FirstWall == null)
        {
            Debug.Log("[OOB] RecenterOnPlayer: missing reference (Head=" + Head + " PlaySpace=" + PlaySpace + " FirstWall=" + FirstWall + ")");
            return;
        }

        // Also sample XR device position for diagnostics
        var devs = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, devs);
        Vector3 xrDevicePos = Vector3.zero;
        if (devs.Count > 0) devs[0].TryGetFeatureValue(CommonUsages.devicePosition, out xrDevicePos);
        Vector3 rigInverseHead = transform.InverseTransformPoint(Head.position);

        Debug.Log("[OOB] RecenterOnPlayer:"
            + " Head.localPos=" + Head.localPosition
            + " Head.worldPos=" + Head.position
            + " rigInverseHead=" + rigInverseHead
            + " xrDevicePos=" + xrDevicePos
            + " cachedHmdLocalPos=" + cachedHmdLocalPos
            + " rig.pos=" + transform.position
            + " rig.rot=" + transform.rotation.eulerAngles);

        // Use cachedHmdLocalPos (last frame where Head.localPosition was non-zero during countdown).
        // Fall back to rigInverseHead if cache is still zero.
        Vector3 bestLocalPos = (cachedHmdLocalPos != Vector3.zero) ? cachedHmdLocalPos
                             : (rigInverseHead != Vector3.zero) ? rigInverseHead
                             : xrDevicePos;

        Vector3 localOffset = new Vector3(bestLocalPos.x, 0f, bestLocalPos.z);
        Debug.Log("[OOB] RecenterOnPlayer: bestLocalPos=" + bestLocalPos + " shifting by " + localOffset);

        PlaySpace.transform.localPosition += localOffset;

        ChaperonePlaneProperties wall = FirstWall;
        for (int i = 0; i < WallCount; i++)
        {
            wall.transform.localPosition += localOffset;
            wall = wall.LeftWall;
        }

        // Write to the chaperone file directly so SteamVR's overlay updates.
        // Do NOT call SaveChaperone() — it ends with ReloadChaperone() which re-anchors the rig
        // at the new mat position, adding the device offset a second time.
        SteamVR_Utils.RigidTransform rt = new SteamVR_Utils.RigidTransform();
        rt.pos = PlaySpace.transform.position;
        rt.rot = PlaySpace.transform.rotation;
        HmdMatrix34_t mat = rt.ToHmdMatrix34();

        HmdQuad_t[] pQuadsBuffer = new HmdQuad_t[WallCount];
        wall = FirstWall;
        for (int index = 0; index < WallCount; index++)
        {
            Vector3 wc = Vector3.Scale(PlaySpace.transform.InverseTransformPoint(wall.transform.TransformPoint(new Vector3(0.5f, 0f, 0f))), PlaySpace.transform.localScale);
            pQuadsBuffer[index].vCorners0.v0 = wc.x; pQuadsBuffer[index].vCorners0.v1 = wc.y; pQuadsBuffer[index].vCorners0.v2 = -wc.z;
            wc = Vector3.Scale(PlaySpace.transform.InverseTransformPoint(wall.transform.TransformPoint(new Vector3(0.5f, 1f, 0f))), PlaySpace.transform.localScale);
            pQuadsBuffer[index].vCorners1.v0 = wc.x; pQuadsBuffer[index].vCorners1.v1 = wc.y; pQuadsBuffer[index].vCorners1.v2 = -wc.z;
            wc = Vector3.Scale(PlaySpace.transform.InverseTransformPoint(wall.transform.TransformPoint(new Vector3(-0.5f, 1f, 0f))), PlaySpace.transform.localScale);
            pQuadsBuffer[index].vCorners2.v0 = wc.x; pQuadsBuffer[index].vCorners2.v1 = wc.y; pQuadsBuffer[index].vCorners2.v2 = -wc.z;
            wc = Vector3.Scale(PlaySpace.transform.InverseTransformPoint(wall.transform.TransformPoint(new Vector3(-0.5f, 0f, 0f))), PlaySpace.transform.localScale);
            pQuadsBuffer[index].vCorners3.v0 = wc.x; pQuadsBuffer[index].vCorners3.v1 = wc.y; pQuadsBuffer[index].vCorners3.v2 = -wc.z;
            wall = wall.LeftWall;
        }

        bool saved = ChaperoneFileIO.Save(mat, PlaySpace.transform.localScale.x, PlaySpace.transform.localScale.z, pQuadsBuffer);
        Debug.Log("[OOB] RecenterOnPlayer: file save result=" + saved + " PlaySpace.worldPos=" + PlaySpace.transform.position);
    }

    void SetupControllerTracking()
    {
        // SteamVR_ControllerManager.OnEnable() deactivates both controllers and waits
        // for SteamVR device-connected events that never fire when SteamVR is disabled.
        // Grab the controller references from it, activate them manually, then disable
        // the manager so it can't deactivate them again.
        var mgr = GetComponent<SteamVR_ControllerManager>();
        if (mgr == null) { Debug.Log("[XRCtrl] no SteamVR_ControllerManager found"); return; }

        mgr.enabled = false;

        AddControllerTracker(mgr.right, SteamVR_TrackedObject.EIndex.Device1);
        AddControllerTracker(mgr.left,  SteamVR_TrackedObject.EIndex.Device2);
    }

    void AddControllerTracker(GameObject go, SteamVR_TrackedObject.EIndex deviceIndex)
    {
        if (go == null) { Debug.Log("[XRCtrl] controller GO is null for " + deviceIndex); return; }
        go.SetActive(true);
        if (go.GetComponent<SteamVR_TrackedObject>() != null) return;
        var tracker = go.AddComponent<SteamVR_TrackedObject>();
        tracker.index = deviceIndex;
        Debug.Log("[XRCtrl] tracking " + go.name + " as " + deviceIndex);
    }

    public bool StartPlaySpaceMove(Transform controller)
    {
        if (TweakAction != TweakActionType.none)
        {
            return false;
        }
        TweakAction = TweakActionType.psMove;
        TweakControllerInitPos = controller.position;
        TweakControllerTrans = controller;
        TweakTargetInitPoint = PlaySpace.transform.position;
        return true;
    }

    public bool StartPlaySpaceEdgeResize(Transform controller, TweakActionArea area)
    {
        if (TweakAction != TweakActionType.none)
        {
            return false;
        }
        TweakAction = TweakActionType.psEdgeResize;
        TweakControllerInitPos = controller.position;
        TweakControllerTrans = controller;
        if ((area == TweakActionArea.bottom) || (area == TweakActionArea.top))
        {
            TweakInitScaleF = PlaySpace.transform.localScale.z;
        }
        else
        {
            TweakInitScaleF = PlaySpace.transform.localScale.x;
        }
        TweakArea = area;
        TweakTargetInitPoint = PlaySpace.transform.position;
        return true;
    }

    public bool StartPlaySpaceCornerPivot(Transform controller, TweakActionArea area)
    {
        if (TweakAction != TweakActionType.none)
        {
            return false;
        }
        TweakAction = TweakActionType.psPivotScale;
        TweakControllerInitPos = controller.position;
        TweakControllerTrans = controller;
        if (area == TweakActionArea.bottomLeft)
        {
            TweakTargetInitPoint = PlaySpace.transform.TransformPoint(new Vector3(-0.5f, 0, -0.5f));
            TweakPivotPoint = PlaySpace.transform.TransformPoint(new Vector3(0.5f, 0, 0.5f));
        }
        else if (area == TweakActionArea.bottomRight)
        {
            TweakTargetInitPoint = PlaySpace.transform.TransformPoint(new Vector3(0.5f, 0, -0.5f));
            TweakPivotPoint = PlaySpace.transform.TransformPoint(new Vector3(-0.5f, 0, 0.5f));
        }
        else if (area == TweakActionArea.topLeft)
        {
            TweakTargetInitPoint = PlaySpace.transform.TransformPoint(new Vector3(-0.5f, 0, 0.5f));
            TweakPivotPoint = PlaySpace.transform.TransformPoint(new Vector3(0.5f, 0, -0.5f));
        }
        else if (area == TweakActionArea.topRight)
        {
            TweakTargetInitPoint = PlaySpace.transform.TransformPoint(new Vector3(0.5f, 0, 0.5f));
            TweakPivotPoint = PlaySpace.transform.TransformPoint(new Vector3(-0.5f, 0, -0.5f));
        }

        TweakMinScale = Mathf.Sqrt(Mathf.Pow(MinimumScale / Mathf.Min(PlaySpace.transform.localScale.x, PlaySpace.transform.localScale.z) * PlaySpace.transform.localScale.x, 2) + Mathf.Pow(MinimumScale / Mathf.Min(PlaySpace.transform.localScale.x, PlaySpace.transform.localScale.z) * PlaySpace.transform.localScale.z, 2));
        TweakInitScaleV = PlaySpace.transform.localScale;
        TweakYawDelta = PlaySpace.transform.localEulerAngles.y - Quaternion.LookRotation(TweakTargetInitPoint - TweakPivotPoint, Vector3.up).eulerAngles.y;

        return true;
    }

    public bool StartPlaySpaceCentrePivot(Transform controller, TweakActionArea area)
    {
        if (TweakAction != TweakActionType.none)
        {
            return false;
        }
        TweakAction = TweakActionType.psPivot;
        TweakControllerInitPos = controller.position;
        TweakControllerTrans = controller;
        TweakPivotPoint = PlaySpace.transform.position;
        if (area == TweakActionArea.bottomLeft)
        {
            TweakTargetInitPoint = PlaySpace.transform.TransformPoint(new Vector3(-0.5f, 0, -0.5f));
        }
        else if (area == TweakActionArea.bottomRight)
        {
            TweakTargetInitPoint = PlaySpace.transform.TransformPoint(new Vector3(0.5f, 0, -0.5f));
        }
        else if (area == TweakActionArea.topLeft)
        {
            TweakTargetInitPoint = PlaySpace.transform.TransformPoint(new Vector3(-0.5f, 0, 0.5f));
        }
        else if (area == TweakActionArea.topRight)
        {
            TweakTargetInitPoint = PlaySpace.transform.TransformPoint(new Vector3(0.5f, 0, 0.5f));
        }
        TweakYawDelta = PlaySpace.transform.localEulerAngles.y - Quaternion.LookRotation(TweakTargetInitPoint - TweakPivotPoint, Vector3.up).eulerAngles.y;
        return true;
    }

    public bool StartPlaySpaceHeightAdjust(Transform controller)
    {
        if (TweakAction != TweakActionType.none)
        {
            return false;
        }
        TweakAction = TweakActionType.psHeight;
        TweakControllerInitPos = controller.position;
        TweakControllerTrans = controller;
        TweakTargetInitPoint = PlaySpace.transform.position;
        return true;
    }

    public bool StartWallHeightAdjust(Transform controller)
    {
        if (TweakAction != TweakActionType.none)
        {
            return false;
        }
        TweakAction = TweakActionType.wHeight;
        TweakControllerInitPos = controller.position;
        TweakControllerTrans = controller;
        TweakInitScaleF = FirstWall.transform.localScale.y;
        return true;
    }

    public bool StartWallEdgeAdjust(Transform controller, GameObject wall, Vector3 grabpoint)
    {
        if (TweakAction != TweakActionType.none)
        {
            return false;
        }
        ChaperonePlaneProperties wallprops = wall.GetComponent<ChaperonePlaneProperties>();
        TweakAction = TweakActionType.wEdgePosition;
        TweakControllerInitPos = controller.position;
        TweakControllerTrans = controller;
        if (Vector3.Distance(wall.transform.TransformPoint(new Vector3(-0.5f, 0f, 0f)), grabpoint) < Vector3.Distance(wall.transform.TransformPoint(new Vector3(0.5f, 0f, 0f)), grabpoint))
        {
            TweakLeftWall = wallprops.LeftWall.gameObject;
            TweakRightWall = wall;
        }
        else
        {
            TweakLeftWall = wall;
            TweakRightWall = wallprops.RightWall.gameObject;
        }
        TweakLeftPivotPoint = TweakLeftWall.transform.TransformPoint(new Vector3(-0.5f, 0f, 0f));
        TweakRightPivotPoint = TweakRightWall.transform.TransformPoint(new Vector3(0.5f, 0f, 0f));
        TweakTargetInitPoint = TweakLeftWall.transform.TransformPoint(new Vector3(0.5f, 0f, 0f));
        return true;
    }

    public bool SplitWallSegment(Transform controller, GameObject wall, Vector3 targetpoint)
    {
        if (TweakAction != TweakActionType.none)
        {
            return false;
        }
        ChaperonePlaneProperties wallprops = wall.GetComponent<ChaperonePlaneProperties>();
        TweakAction = TweakActionType.wAdd;
        WallCount++;
        Vector3 leftcorner = wall.transform.TransformPoint(new Vector3(-0.5f, 0f, 0f));
        Vector3 rightcorner = targetpoint;
        rightcorner.y = leftcorner.y;
        wall.transform.position = Vector3.Lerp(leftcorner, rightcorner, 0.5f);
        wall.transform.localScale = new Vector3(Vector3.Distance(leftcorner, rightcorner), wall.transform.localScale.y, 1);
        wall.transform.rotation = Quaternion.LookRotation(leftcorner - rightcorner, Vector3.up) * Quaternion.Euler(0, 90, 0);
        ChaperonePlaneProperties newwall = CreatePlane("newwall", true, WallMaterial);
        newwall.transform.parent = transform;
        leftcorner = rightcorner;
        rightcorner = wallprops.RightWall.transform.TransformPoint(new Vector3(-0.5f, 0f, 0f));
        newwall.transform.position = Vector3.Lerp(leftcorner, rightcorner, 0.5f);
        newwall.transform.localScale = new Vector3(Vector3.Distance(leftcorner, rightcorner), wall.transform.localScale.y, 1);
        newwall.transform.rotation = Quaternion.LookRotation(leftcorner - rightcorner, Vector3.up) * Quaternion.Euler(0, 90, 0);
        newwall.RightWall = wallprops.RightWall;
        newwall.LeftWall = wallprops;
        newwall.LeftWall.RightWall = newwall;
        newwall.RightWall.LeftWall = newwall;
        TweakAction = TweakActionType.none;
        StartWallEdgeAdjust(controller, newwall.gameObject, targetpoint);
        return true;
    }

    public bool DeleteWallSegment(GameObject wall, Vector3 targetpoint)
    {
        if (TweakAction != TweakActionType.none)
        {
            return false;
        }
        ChaperonePlaneProperties wallprops = wall.GetComponent<ChaperonePlaneProperties>();
        TweakAction = TweakActionType.wRemove;
        if (WallCount > 3)
        {
            WallCount--;
            if (Vector3.Distance(wall.transform.TransformPoint(new Vector3(-0.5f, 0f, 0f)), targetpoint) > Vector3.Distance(wall.transform.TransformPoint(new Vector3(0.5f, 0f, 0f)), targetpoint))
            {
                wallprops = wallprops.RightWall;
            }
            if (FirstWall == wallprops) { FirstWall = wallprops.LeftWall; }
            wallprops.LeftWall.RightWall = wallprops.RightWall;
            wallprops.RightWall.LeftWall = wallprops.LeftWall;
            Vector3 leftcorner = wallprops.LeftWall.transform.TransformPoint(new Vector3(-0.5f, 0f, 0f));
            Vector3 rightcorner = wallprops.RightWall.transform.TransformPoint(new Vector3(-0.5f, 0f, 0f));
            wallprops.LeftWall.transform.position = Vector3.Lerp(leftcorner, rightcorner, 0.5f);
            wallprops.LeftWall.transform.localScale = new Vector3(Vector3.Distance(leftcorner, rightcorner), wallprops.LeftWall.transform.localScale.y, 1);
            wallprops.LeftWall.transform.rotation = Quaternion.LookRotation(leftcorner - rightcorner, Vector3.up) * Quaternion.Euler(0, 90, 0);
            Destroy(wallprops.gameObject);
        }
        return true;
    }

    public bool SetFront(TweakActionArea area)
    {
        if (TweakAction != TweakActionType.none)
        {
            return false;
        }
        TweakAction = TweakActionType.psSetFront;
        if (area == TweakActionArea.left)
        {
            PlaySpace.transform.localScale = new Vector3(PlaySpace.transform.localScale.z, PlaySpace.transform.localScale.y, PlaySpace.transform.localScale.x);
            PlaySpace.transform.Rotate(0, -90, 0);
        }
        else if (area == TweakActionArea.right)
        {
            PlaySpace.transform.localScale = new Vector3(PlaySpace.transform.localScale.z, PlaySpace.transform.localScale.y, PlaySpace.transform.localScale.x);
            PlaySpace.transform.Rotate(0, 90, 0);
        }
        else if (area == TweakActionArea.bottom)
        {
            PlaySpace.transform.Rotate(0, 180, 0);
        }

        return true;
    }

    private float SnapTo90(float angle)
    {
        float snapped = Mathf.Round(angle / 90f) * 90f;
        return (Mathf.Abs(Mathf.DeltaAngle(angle, snapped)) < RotationSnapDegrees) ? snapped : angle;
    }

    public void EndAction()
    {
        TweakAction = TweakActionType.none;
    }

    // Update is called once per frame
    void Update()
    {
        //Local variables
        float foffset;
        Vector3 voffset;
        Vector3 newcornerpos;
        Vector3 temp;
        ChaperonePlaneProperties wall;

        if (ChaperoneSaving)
        {
            SaveChaperone();
            return;
        }
        if (ChaperoneLoading)
        {
            ReloadChaperone();
            return;
        }

        //adjust wall height
        if (TweakAction == TweakActionType.wHeight)
        {
            foffset = TweakControllerTrans.position.y - TweakControllerInitPos.y;
            wall = FirstWall;
            for (int i = 0; i < WallCount; i++)
            {
                wall.transform.localScale = new Vector3(wall.transform.localScale.x, Mathf.Max(TweakInitScaleF + foffset, MinimumScale), wall.transform.localScale.z);
                wall = wall.LeftWall;
            }
        }

        //adjust wall Edge
        if (TweakAction == TweakActionType.wEdgePosition)
        {
            voffset = TweakControllerTrans.position - TweakControllerInitPos;
            voffset.y = 0;
            Vector3 cornerPos = TweakTargetInitPoint + voffset;

            // Snap corner to 90° using Thales' theorem:
            // all points where two walls meet at 90° lie on the circle whose diameter
            // connects the two far wall endpoints (TweakLeftPivotPoint, TweakRightPivotPoint).
            Vector3 leftH  = new Vector3(TweakLeftPivotPoint.x,  0, TweakLeftPivotPoint.z);
            Vector3 rightH = new Vector3(TweakRightPivotPoint.x, 0, TweakRightPivotPoint.z);
            Vector3 midH   = (leftH + rightH) * 0.5f;
            float   radius = Vector3.Distance(leftH, rightH) * 0.5f;
            Vector3 toCorner = new Vector3(cornerPos.x, 0, cornerPos.z) - midH;
            if (radius > 0.05f && toCorner.magnitude > 0.01f &&
                Mathf.Abs(toCorner.magnitude - radius) < CornerSnapDistance)
            {
                Vector3 snappedH = midH + toCorner.normalized * radius;
                cornerPos.x = snappedH.x;
                cornerPos.z = snappedH.z;
            }

            TweakLeftWall.transform.position  = Vector3.Lerp(TweakLeftPivotPoint, cornerPos, 0.5f);
            TweakLeftWall.transform.localScale = new Vector3(Vector3.Distance(TweakLeftPivotPoint, cornerPos), TweakLeftWall.transform.localScale.y, 1);
            TweakLeftWall.transform.rotation   = Quaternion.LookRotation(TweakLeftPivotPoint - cornerPos, Vector3.up) * Quaternion.Euler(0, 90, 0);
            TweakRightWall.transform.position  = Vector3.Lerp(TweakRightPivotPoint, cornerPos, 0.5f);
            TweakRightWall.transform.localScale = new Vector3(Vector3.Distance(TweakRightPivotPoint, cornerPos), TweakRightWall.transform.localScale.y, 1);
            TweakRightWall.transform.rotation  = Quaternion.LookRotation(cornerPos - TweakRightPivotPoint, Vector3.up) * Quaternion.Euler(0, 90, 0);
        }

        //adjust playspace horizontal position
        if (TweakAction == TweakActionType.psMove)
        {
            voffset = TweakControllerTrans.position - TweakControllerInitPos;
            voffset.y = 0;
            PlaySpace.transform.position = TweakTargetInitPoint + voffset;
        }

        //adjust playspace vertical position
        if (TweakAction == TweakActionType.psHeight)
        {
            voffset = TweakControllerTrans.position - TweakControllerInitPos;
            voffset.x = 0;
            voffset.z = 0;
            PlaySpace.transform.position = TweakTargetInitPoint + voffset;
            wall = FirstWall;
            for (int i = 0; i < WallCount; i++)
            {
                wall.transform.position = new Vector3(wall.transform.position.x, PlaySpace.transform.position.y, wall.transform.position.z);
                wall = wall.LeftWall;
            }
        }

        //adjust playspace scale in one direction at a time
        if (TweakAction == TweakActionType.psEdgeResize)
        {
            voffset = TweakControllerTrans.position - TweakControllerInitPos;
            if (TweakArea == TweakActionArea.top)
            {
                voffset = Vector3.Project(voffset, PlaySpace.transform.forward);
                if (TweakInitScaleF + Vector3.Dot(voffset, PlaySpace.transform.forward) < MinimumScale)
                {
                    voffset = PlaySpace.transform.forward * (MinimumScale - TweakInitScaleF);
                }
                PlaySpace.transform.localScale = new Vector3(PlaySpace.transform.localScale.x, PlaySpace.transform.localScale.y, TweakInitScaleF + Vector3.Dot(voffset, PlaySpace.transform.forward));
                PlaySpace.transform.position = TweakTargetInitPoint + voffset / 2;
            }
            else if (TweakArea == TweakActionArea.bottom)
            {
                voffset = Vector3.Project(voffset, -PlaySpace.transform.forward);
                if (TweakInitScaleF + Vector3.Dot(voffset, -PlaySpace.transform.forward) < MinimumScale)
                {
                    voffset = -PlaySpace.transform.forward * (MinimumScale - TweakInitScaleF);
                }
                PlaySpace.transform.localScale = new Vector3(PlaySpace.transform.localScale.x, PlaySpace.transform.localScale.y, TweakInitScaleF + Vector3.Dot(voffset, -PlaySpace.transform.forward));
                PlaySpace.transform.position = TweakTargetInitPoint + voffset / 2;
            }
            else if (TweakArea == TweakActionArea.right)
            {
                voffset = Vector3.Project(voffset, PlaySpace.transform.right);
                if (TweakInitScaleF + Vector3.Dot(voffset, PlaySpace.transform.right) < MinimumScale)
                {
                    voffset = PlaySpace.transform.right * (MinimumScale - TweakInitScaleF);
                }
                PlaySpace.transform.localScale = new Vector3(TweakInitScaleF + Vector3.Dot(voffset, PlaySpace.transform.right), PlaySpace.transform.localScale.y, PlaySpace.transform.localScale.z);
                PlaySpace.transform.position = TweakTargetInitPoint + voffset / 2;
            }
            else if (TweakArea == TweakActionArea.left)
            {
                voffset = Vector3.Project(voffset, -PlaySpace.transform.right);
                if (TweakInitScaleF + Vector3.Dot(voffset, -PlaySpace.transform.right) < MinimumScale)
                {
                    voffset = -PlaySpace.transform.right * (MinimumScale - TweakInitScaleF);
                }
                PlaySpace.transform.localScale = new Vector3(TweakInitScaleF + Vector3.Dot(voffset, -PlaySpace.transform.right), PlaySpace.transform.localScale.y, PlaySpace.transform.localScale.z);
                PlaySpace.transform.position = TweakTargetInitPoint + voffset / 2;
            }
        }

        //pivot and scale playspace corner around opposite corner
        if (TweakAction == TweakActionType.psPivotScale)
        {
            voffset = TweakControllerTrans.position - TweakControllerInitPos;
            voffset.y = 0;
            newcornerpos = TweakTargetInitPoint + voffset;
            if (Vector3.Magnitude(newcornerpos - TweakPivotPoint) < TweakMinScale)
            {
                newcornerpos = TweakPivotPoint + Vector3.Normalize(newcornerpos - TweakPivotPoint) * TweakMinScale;
            }
            PlaySpace.transform.localEulerAngles = new Vector3(PlaySpace.transform.localEulerAngles.x, SnapTo90(TweakYawDelta + Quaternion.LookRotation(newcornerpos - TweakPivotPoint, Vector3.up).eulerAngles.y), PlaySpace.transform.localEulerAngles.z);
            PlaySpace.transform.position = Vector3.Lerp(newcornerpos, TweakPivotPoint, 0.5f);
            temp.x = Mathf.Sqrt(Mathf.Pow(Vector3.Magnitude(newcornerpos - TweakPivotPoint), 2) / (Mathf.Pow(TweakInitScaleV.z / TweakInitScaleV.x, 2) + 1));
            temp.y = 1;
            temp.z = temp.x * TweakInitScaleV.z / TweakInitScaleV.x;
            PlaySpace.transform.localScale = temp;
        }

        if (TweakAction == TweakActionType.psPivot)
        {
            voffset = TweakControllerTrans.position - TweakControllerInitPos;
            voffset.y = 0;
            PlaySpace.transform.localEulerAngles = new Vector3(PlaySpace.transform.localEulerAngles.x, SnapTo90(TweakYawDelta + Quaternion.LookRotation(TweakTargetInitPoint + voffset - TweakPivotPoint, Vector3.up).eulerAngles.y), PlaySpace.transform.localEulerAngles.z);
        }

    }
}
