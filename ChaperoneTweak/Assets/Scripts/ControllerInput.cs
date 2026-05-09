using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using Valve.VR;

public enum XRHand { Left, Right }
enum ChaperoneEditMode { wall, playSpace }

public class ControllerInput : MonoBehaviour
{
    // Set Left or Right in the Inspector for each controller GameObject.
    public XRHand Hand = XRHand.Right;

    public GameObject HandCamera;
    public GameObject SelectionCircle;
    private bool IsEnabled = true;

    public Camera UICam;
    public GameObject Menu;
    public Transform Head;
    public bool InUse;
    public ControllerInput OtherControllerInput;

    private ChaperoneEditMode EditMode;
    public ChaperoneElements ChapElements;
    public GameObject origin;
    public LaserPointer Laser;

    public GameObject testing;

    private bool GripAction    = false;
    private bool TriggerAction = false;
    private bool MenuAction    = false;
    private bool CalibAction   = false;
    private int  _savedLaserMask;

    public Material PlaySpaceMatEdit, WallMatEdit, PlaySpaceMatTrans, WallMatTrans;

    private InputDevice _device;
    private bool _prevGrip, _prevTrigger, _prevMenu, _prevTouchpad, _prevPrimary, _prevSecondary;

    public void SetEnabled(bool isenabled)
    {
        IsEnabled = isenabled;
        Laser.SetEnabled(isenabled);
    }

    void Awake()
    {
        EditMode  = ChaperoneEditMode.wall;
        Laser.Mask = 1 << 8;
        InUse     = false;
    }

    void Start()
    {
        if (Menu == null) return;

        var existing = Menu.transform.Find("Calibrate");
        if (existing != null)
        {
            // Sphere already exists (e.g. saved to scene) — make sure it's on the right layer.
            existing.gameObject.layer = 5;
            Debug.Log("[ChaperoneTweak] Calibrate sphere found, ensured layer=5");
            return;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Calibrate";
        go.layer = 5;
        go.transform.SetParent(Menu.transform, false);
        go.transform.localPosition = new Vector3(0.3f, 0, 0);
        go.transform.localScale    = Vector3.one * 0.15f;

        var label = new GameObject("Label");
        label.transform.SetParent(go.transform, false);
        label.transform.localPosition = new Vector3(0, 1.2f, 0);
        label.transform.localRotation = Quaternion.Euler(0, 180, 0);
        label.transform.localScale    = new Vector3(-10, 10, 10);
        var tm = label.AddComponent<TextMesh>();
        tm.text          = "Calibrate";
        tm.characterSize = 0.06f;
        tm.fontSize      = 50;
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.color         = Color.cyan;

        var icon = go.AddComponent<MenuIcon>();
        icon.Camera = Head;
        Debug.Log("[ChaperoneTweak] Calibrate sphere created on layer 5");
    }

    private InputDevice FindDevice()
    {
        var chars = InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Controller
            | (Hand == XRHand.Left ? InputDeviceCharacteristics.Left : InputDeviceCharacteristics.Right);
        var found = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(chars, found);
        return found.Count > 0 ? found[0] : default;
    }

    void Update()
    {
        if (!_device.isValid)
            _device = FindDevice();

        if (!_device.isValid || !IsEnabled)
        {
            _prevGrip = _prevTrigger = _prevMenu = _prevTouchpad = false;
            return;
        }

        _device.TryGetFeatureValue(CommonUsages.triggerButton,      out bool trigger);
        _device.TryGetFeatureValue(CommonUsages.secondaryButton,   out bool grip);    // B button = drag/move
        _device.TryGetFeatureValue(CommonUsages.primaryButton,     out bool menu);    // A button = menu
        _device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool touchpad);
        bool primary = grip;   // A = drag/move
        bool secondary = menu; // B = menu

        bool gripDown    = grip    && !_prevGrip;
        bool gripUp      = !grip   && _prevGrip;
        bool triggerDown = trigger && !_prevTrigger;
        bool triggerUp   = !trigger && _prevTrigger;
        bool menuDown    = menu    && !_prevMenu;
        bool menuUp      = !menu   && _prevMenu;
        bool touchpadDown = touchpad && !_prevTouchpad;

        Collider[] collisions;
        Vector3 tempv1;
        TweakActionArea hitZone;

        if (touchpadDown)
        {
            ChapElements.StartFloorFix(OtherControllerInput.transform);
        }

        // Calib menu: laser points at layer-10 buttons; trigger clicks them.
        if (CalibAction)
        {
            // Auto-exit if the menu was destroyed or closed externally.
            if (CtrlCalibMenu.Instance == null || !CtrlCalibMenu.Instance.IsOpen)
            {
                EndCalibAction();
                // Fall through to normal input handling this frame.
            }
            else
            {
                if (triggerDown && Laser.Target != null)
                {
                    var btn = Laser.Target.GetComponent<CalibButton>();
                    if (btn != null) btn.Press();
                }
                // A button also closes the calib menu.
                if (menuDown) EndCalibAction();

                _prevGrip = grip; _prevTrigger = trigger; _prevMenu = menu; _prevTouchpad = touchpad;
                _prevPrimary = primary; _prevSecondary = secondary;
                return;
            }
        }

        if (!MenuAction && !TriggerAction && !GripAction)
        {
            if (Laser.Target != null)
            {
                tempv1 = Laser.Target.transform.InverseTransformPoint(Laser.TargetPoint);
                if (EditMode == ChaperoneEditMode.wall)
                {
                    if (tempv1.y >= 0.75)
                    {
                        if (tempv1.x <= -0.25)      { hitZone = TweakActionArea.topLeft; }
                        else if (tempv1.x >= 0.25)  { hitZone = TweakActionArea.topRight; }
                        else                         { hitZone = TweakActionArea.top; }
                    }
                    else if (tempv1.y <= 0.25)
                    {
                        if (tempv1.x <= -0.25)      { hitZone = TweakActionArea.bottomLeft; }
                        else if (tempv1.x >= 0.25)  { hitZone = TweakActionArea.bottomRight; }
                        else                         { hitZone = TweakActionArea.bottom; }
                    }
                    else
                    {
                        if (tempv1.x <= -0.25)      { hitZone = TweakActionArea.left; }
                        else if (tempv1.x >= 0.25)  { hitZone = TweakActionArea.right; }
                        else                         { hitZone = TweakActionArea.middle; }
                    }
                }
                else
                {
                    if (tempv1.z >= 0.25)
                    {
                        if (tempv1.x <= -0.25)      { hitZone = TweakActionArea.topLeft; }
                        else if (tempv1.x >= 0.25)  { hitZone = TweakActionArea.topRight; }
                        else                         { hitZone = TweakActionArea.top; }
                    }
                    else if (tempv1.z <= -0.25)
                    {
                        if (tempv1.x <= -0.25)      { hitZone = TweakActionArea.bottomLeft; }
                        else if (tempv1.x >= 0.25)  { hitZone = TweakActionArea.bottomRight; }
                        else                         { hitZone = TweakActionArea.bottom; }
                    }
                    else
                    {
                        if (tempv1.x <= -0.25)      { hitZone = TweakActionArea.left; }
                        else if (tempv1.x >= 0.25)  { hitZone = TweakActionArea.right; }
                        else                         { hitZone = TweakActionArea.middle; }
                    }
                }
            }
            else
            {
                hitZone = TweakActionArea.none;
            }

            if (gripDown)
            {
                if (EditMode == ChaperoneEditMode.wall)
                {
                    if ((hitZone == TweakActionArea.topLeft) || (hitZone == TweakActionArea.top) || (hitZone == TweakActionArea.topRight))
                    {
                        if (ChapElements.StartWallHeightAdjust(transform))
                        {
                            GripAction = true;
                            OtherControllerInput.SetEnabled(false);
                        }
                    }
                    else if (hitZone != TweakActionArea.none)
                    {
                        if (ChapElements.StartWallEdgeAdjust(transform, Laser.Target, Laser.TargetPoint))
                        {
                            GripAction = true;
                            OtherControllerInput.SetEnabled(false);
                        }
                    }
                }
                else
                {
                    if ((hitZone == TweakActionArea.topLeft) || (hitZone == TweakActionArea.topRight) || (hitZone == TweakActionArea.bottomLeft) || (hitZone == TweakActionArea.bottomRight))
                    {
                        if (ChapElements.StartPlaySpaceCornerPivot(transform, hitZone))
                        {
                            GripAction = true;
                            OtherControllerInput.SetEnabled(false);
                        }
                    }
                    else if ((hitZone == TweakActionArea.top) || (hitZone == TweakActionArea.left) || (hitZone == TweakActionArea.bottom) || (hitZone == TweakActionArea.right))
                    {
                        if (ChapElements.StartPlaySpaceEdgeResize(transform, hitZone))
                        {
                            GripAction = true;
                            OtherControllerInput.SetEnabled(false);
                        }
                    }
                    else if (hitZone == TweakActionArea.middle)
                    {
                        if (ChapElements.StartPlaySpaceMove(transform))
                        {
                            GripAction = true;
                            OtherControllerInput.SetEnabled(false);
                        }
                    }
                }
            }
            else if (triggerDown)
            {
                if (EditMode == ChaperoneEditMode.wall)
                {
                    if ((hitZone == TweakActionArea.top) || (hitZone == TweakActionArea.middle) || (hitZone == TweakActionArea.bottom))
                    {
                        if (ChapElements.SplitWallSegment(transform, Laser.Target, Laser.TargetPoint))
                        {
                            TriggerAction = true;
                            OtherControllerInput.SetEnabled(false);
                        }
                    }
                    else if (hitZone != TweakActionArea.none)
                    {
                        if (ChapElements.DeleteWallSegment(Laser.Target, Laser.TargetPoint))
                        {
                            TriggerAction = true;
                            OtherControllerInput.SetEnabled(false);
                        }
                    }
                }
                else
                {
                    if (hitZone == TweakActionArea.middle)
                    {
                        if (ChapElements.StartPlaySpaceHeightAdjust(transform))
                        {
                            TriggerAction = true;
                            OtherControllerInput.SetEnabled(false);
                        }
                    }
                    else if ((hitZone == TweakActionArea.left) || (hitZone == TweakActionArea.right) || (hitZone == TweakActionArea.bottom))
                    {
                        if (ChapElements.SetFront(hitZone))
                        {
                            TriggerAction = true;
                            OtherControllerInput.SetEnabled(false);
                        }
                    }
                    else if ((hitZone == TweakActionArea.topLeft) || (hitZone == TweakActionArea.topRight) || (hitZone == TweakActionArea.bottomLeft) || (hitZone == TweakActionArea.bottomRight))
                    {
                        if (ChapElements.StartPlaySpaceCentrePivot(transform, hitZone))
                        {
                            TriggerAction = true;
                            OtherControllerInput.SetEnabled(false);
                        }
                    }
                }
            }
            else if (menuDown)
            {
                OtherControllerInput.SetEnabled(false);
                Laser.SetEnabled(false);
                UICam.enabled = true;
                MenuAction = true;
                Menu.SetActive(true);
                Menu.transform.position = transform.position;
                Menu.transform.rotation = Quaternion.LookRotation(transform.TransformPoint(0, -0.034f, 0.015f) - Head.position, Vector3.up);
            }
        }

        if (menuUp)
        {
            if (MenuAction)
            {
                collisions = Physics.OverlapSphere(transform.TransformPoint(0, -0.034f, 0.015f), 0.1f, 1 << 5);
                Debug.Log($"[ChaperoneTweak] menuUp hits: {collisions.Length}" + (collisions.Length > 0 ? " first=" + collisions[0].name + " layer=" + collisions[0].gameObject.layer : ""));
                if (collisions.Length > 0)
                {
                    if (collisions[0].name == "Walls")
                    {
                        EditMode = ChaperoneEditMode.wall;
                        Laser.Mask = 1 << 8;
                        OtherControllerInput.EditMode = ChaperoneEditMode.wall;
                        OtherControllerInput.Laser.Mask = 1 << 8;
                        ChapElements.SetMaterials(WallMatEdit, PlaySpaceMatTrans);
                    }
                    else if (collisions[0].name == "PlaySpace")
                    {
                        EditMode = ChaperoneEditMode.playSpace;
                        Laser.Mask = 1 << 9;
                        OtherControllerInput.EditMode = ChaperoneEditMode.playSpace;
                        OtherControllerInput.Laser.Mask = 1 << 9;
                        ChapElements.SetMaterials(WallMatTrans, PlaySpaceMatEdit);
                    }
                    else if (collisions[0].name == "Reload")
                    {
                        ChapElements.ReloadChaperone();
                    }
                    else if (collisions[0].name == "Save")
                    {
                        ChapElements.SaveChaperone();
                    }
                    else if (collisions[0].name == "Calibrate")
                    {
                        Debug.Log("[ChaperoneTweak] Calibrate selected");
                        var cm = CtrlCalibMenu.GetOrCreate(ChapElements);
                        // Use ChapElements.Head (same object as OOB message) for reliable placement.
                        Transform h = ChapElements.Head != null ? ChapElements.Head : Head;
                        cm.Toggle(h);
                        // Do menu cleanup here and return so we don't re-enable OtherController.
                        SelectionCircle.SetActive(false);
                        UICam.enabled = false;
                        MenuAction = false;
                        Menu.SetActive(false);
                        if (cm.IsOpen)
                        {
                            Laser.SetEnabled(true);
                            BeginCalibAction();
                        }
                        else
                        {
                            OtherControllerInput.SetEnabled(true);
                            Laser.SetEnabled(true);
                        }
                        return;
                    }
                }

                OtherControllerInput.SetEnabled(true);
                Laser.SetEnabled(true);
                UICam.enabled = false;
                MenuAction = false;
                Menu.SetActive(false);
            }
        }

        if (gripUp)
        {
            if (GripAction)
            {
                ChapElements.EndAction();
                GripAction = false;
                OtherControllerInput.SetEnabled(true);
            }
        }

        if (triggerUp)
        {
            if (TriggerAction)
            {
                ChapElements.EndAction();
                TriggerAction = false;
                OtherControllerInput.SetEnabled(true);
            }
        }

        if (MenuAction)
        {
            collisions = Physics.OverlapSphere(transform.TransformPoint(0, -0.034f, 0.015f), 0.1f, 1 << 5);
            if (collisions.Length > 0)
            {
                SelectionCircle.transform.parent        = collisions[0].transform;
                SelectionCircle.transform.localScale    = Vector3.one;
                SelectionCircle.transform.localRotation = Quaternion.identity;
                SelectionCircle.transform.localPosition = Vector3.zero;
                SelectionCircle.SetActive(true);
            }
            else
            {
                SelectionCircle.SetActive(false);
            }
        }

        _prevGrip      = grip;
        _prevTrigger   = trigger;
        _prevMenu      = menu;
        _prevTouchpad  = touchpad;
        _prevPrimary   = primary;
        _prevSecondary = secondary;
    }

    void BeginCalibAction()
    {
        CalibAction = true;
        _savedLaserMask = Laser.Mask;
        Laser.Mask = 1 << 10;   // only hit calib buttons
        OtherControllerInput.SetEnabled(false);
    }

    void EndCalibAction()
    {
        CalibAction = false;
        Laser.Mask = _savedLaserMask;
        OtherControllerInput.SetEnabled(true);
        CtrlCalibMenu.Instance?.Close();
    }
}

