using UnityEngine;

/// <summary>
/// World-space calibration panel for controller model offset/rotation.
/// Spawned by ControllerInput; laser pointer on layer 10 hits the buttons.
/// </summary>
public class CtrlCalibMenu : MonoBehaviour
{
    public static CtrlCalibMenu Instance { get; private set; }
    public bool IsOpen => _root != null && _root.activeSelf;

    static readonly string[] Labels = { "Rot X", "Rot Y", "Rot Z", "Pos X", "Pos Y", "Pos Z" };
    static readonly float[]  Steps  = { 1f,      1f,      1f,      0.005f,  0.005f,  0.005f  };

    ChaperoneElements _chap;
    GameObject _root;
    TextMesh[] _vals = new TextMesh[6];

    void Awake() { Instance = this; }

    public static CtrlCalibMenu GetOrCreate(ChaperoneElements chap)
    {
        if (Instance == null)
        {
            var go = new GameObject("[CtrlCalibMenu]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CtrlCalibMenu>();
        }
        Instance._chap = chap;
        return Instance;
    }

    public void Toggle(Transform head)
    {
        if (IsOpen) Close();
        else Open(head);
    }

    void Open(Transform head)
    {
        // Use Camera.main as ultimate fallback so the panel always appears somewhere visible.
        if (head == null && Camera.main != null) head = Camera.main.transform;

        _root = new GameObject("CalibPanel");
        _root.transform.SetParent(head, false);
        // 0.5 m in front of head — same proven distance as the OOB message.
        _root.transform.localPosition = new Vector3(0f, -0.02f, 0.5f);
        _root.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        _root.transform.localScale    = new Vector3(-1f, 1f, 1f);

        try
        {
            AddBackground(new Vector3(0.015f, -0.01f, 0.004f), 0.27f, 0.29f);

            AddText(_root.transform, "Controller Calibration", new Vector3(0.015f, 0.095f, 0), 0.013f, Color.white);

            for (int i = 0; i < 6; i++)
            {
                float y = 0.055f - i * 0.025f;
                AddTextLeft(_root.transform, Labels[i],    new Vector3(-0.10f,  y, 0),       0.009f, Color.white);
                _vals[i] = AddText(_root.transform, "---", new Vector3( 0.025f, y, 0),       0.009f, Color.yellow);
                AddButton(_root.transform, "[-]",          new Vector3( 0.090f, y, -0.001f), i, -1);
                AddButton(_root.transform, "[+]",          new Vector3( 0.120f, y, -0.001f), i, +1);
            }

            AddButton(_root.transform, "[ SAVE + CLOSE ]", new Vector3(0.015f, -0.105f, -0.001f), -1, 0);
            Refresh();
            Debug.Log("[CalibMenu] Panel opened. head=" + (head != null ? head.name + " pos=" + head.position : "null"));
        }
        catch (System.Exception e)
        {
            Debug.LogError("[CalibMenu] Exception in Open(): " + e.Message);
            // Destroy the partial panel so IsOpen returns false and CalibAction is not entered.
            Destroy(_root);
            _root = null;
        }
    }

    void AddBackground(Vector3 lp, float w, float h)
    {
        var shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            Debug.LogWarning("[CalibMenu] Unlit/Color shader not found, skipping background");
            return;
        }
        var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "BG";
        bg.transform.SetParent(_root.transform, false);
        bg.transform.localPosition = lp;
        bg.transform.localScale    = new Vector3(w, h, 1f);
        Destroy(bg.GetComponent<Collider>());
        var mat = new Material(shader);
        mat.color       = new Color(0.05f, 0.05f, 0.12f);
        mat.renderQueue = 4400;
        bg.GetComponent<Renderer>().material = mat;
    }

    TextMesh AddTextLeft(Transform parent, string text, Vector3 lp, float charSize, Color col)
    {
        var tm = AddText(parent, text, lp, charSize, col);
        tm.anchor    = TextAnchor.MiddleLeft;
        tm.alignment = TextAlignment.Left;
        return tm;
    }

    TextMesh AddText(Transform parent, string text, Vector3 lp, float charSize, Color col)
    {
        var go = new GameObject("T");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp;
        var tm = go.AddComponent<TextMesh>();
        tm.text          = text;
        tm.characterSize = charSize;
        tm.fontSize      = 50;
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.color         = col;
        SetOverlay(go);
        return tm;
    }

    static void SetOverlay(GameObject go)
    {
        var r = go.GetComponent<MeshRenderer>();
        if (r == null || r.sharedMaterial == null) return;
        var mat = new Material(r.sharedMaterial);
        mat.renderQueue = 4500;
        mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        r.material = mat;
    }

    void AddButton(Transform parent, string label, Vector3 lp, int field, int dir)
    {
        var go = new GameObject("CalibBtn_" + (field < 0 ? "Close" : Labels[field] + (dir > 0 ? "+" : "-")));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp;
        go.layer = 10;

        var tm = go.AddComponent<TextMesh>();
        tm.text          = label;
        tm.characterSize = 0.009f;
        tm.fontSize      = 50;
        tm.fontStyle     = FontStyle.Bold;
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.color         = field < 0 ? Color.cyan : Color.green;

        var col = go.AddComponent<BoxCollider>();
        col.size = field < 0
            ? new Vector3(0.08f, 0.018f, 0.01f)
            : new Vector3(0.022f, 0.015f, 0.01f);

        SetOverlay(go);
        var btn = go.AddComponent<CalibButton>();
        btn.Field = field;
        btn.Dir   = dir;
        btn.Owner = this;
    }

    public void Press(int field, int dir)
    {
        if (field < 0) { SaveAndClose(); return; }
        if (_chap == null) return;
        _chap.AdjustCalib(field, Steps[field] * dir);
        Refresh();
    }

    void Refresh()
    {
        if (_chap == null) return;
        float[] v = {
            _chap.CtrlRotX,    _chap.CtrlRotY,    _chap.CtrlRotZ,
            _chap.CtrlOffsetX, _chap.CtrlOffsetY, _chap.CtrlOffsetZ
        };
        for (int i = 0; i < 6; i++)
            if (_vals[i] != null)
                _vals[i].text = i < 3
                    ? v[i].ToString("F1") + "°"
                    : (v[i] * 1000f).ToString("F1") + "mm";
    }

    void SaveAndClose()
    {
        _chap?.SaveCalibPublic();
        Close();
    }

    public void Close()
    {
        if (_root != null) { Destroy(_root); _root = null; }
    }
}
