using UnityEngine;

public class CalibButton : MonoBehaviour
{
    public int Field;   // 0=RotX 1=RotY 2=RotZ 3=PosX 4=PosY 5=PosZ  -1=close
    public int Dir;     // +1 or -1
    public CtrlCalibMenu Owner;

    public void Press() => Owner?.Press(Field, Dir);
}
