using Unity.VisualScripting;

static public class CarPhysics
{
    // エンジンの二回転分の移動量
    public static readonly float m_oneCycle = 4 * UnityEngine.Mathf.PI;
    // AngularVelocity[rad/sec] to RPM[N/min]
    public static readonly float m_radiansToRpm = 30f / UnityEngine.Mathf.PI;
    // RPM[N/min] to AngularVelocity[rad/sec]
    public static readonly float m_rpmToRadians = 1f / m_radiansToRpm;
    // RangeClamp01
    public static float RangeClamp01(float _value, float _rangeA, float _rangeB)
    {
        return UnityEngine.Mathf.Clamp01((_value - _rangeA) / (_rangeB - _rangeA));
    }
}
