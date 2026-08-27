using UnityEngine;

[System.Serializable]
public class Differential : MonoBehaviour
{
    enum DifferentialType { None = 0, Open, Lock, LimitedSlip }

    [SerializeField] float m_differentialGearRatio = 3.941f;
    [SerializeField] DifferentialType m_differentialType = DifferentialType.LimitedSlip;
    [SerializeField, ShowInInspector] float m_lockRatio;
    [SerializeField, Range(0f, 1f)] float m_customLockRatio_LSD = 0.35f;

    [Header("GR-FOUR NORMAL")]
    [SerializeField, Range(0f, 1f)] float m_frontTorqueRatio = 0.60f;
    [SerializeField, Range(0f, 1f)] float m_launchFrontTorqueRatio = 0.50f;
    [SerializeField, Range(0f, 1f)] float m_centerLockRatio = 0.35f;

    public void SetFinalDriveRatio(float ratio)
    {
        m_differentialGearRatio = Mathf.Max(0.01f, ratio);
    }

    float m_wheelAngularVelocity_LeftFront;
    float m_wheelAngularVelocity_LeftRear;
    float m_wheelAngularVelocity_RightFront;
    float m_wheelAngularVelocity_RightRear;
    float m_wheelInertia;

    //入力トルク
    float m_inputTorque;
    public float InputTorque
    {
        get {  return m_inputTorque; }
        set { m_inputTorque = value; }
    }

    public float GetDriveTorque(bool isFront, bool isRight)
    {
        //発進時は50:50で4輪を使い、速度が乗るにつれてNORMALモードの60:40へ移行する。
        float averageWheelSpeed = (Mathf.Abs(m_wheelAngularVelocity_LeftFront) + Mathf.Abs(m_wheelAngularVelocity_RightFront) +
            Mathf.Abs(m_wheelAngularVelocity_LeftRear) + Mathf.Abs(m_wheelAngularVelocity_RightRear)) * 0.25f;
        float estimatedSpeedKph = averageWheelSpeed * 0.334f * 3.6f;
        float dynamicFrontRatio = Mathf.Lerp(m_launchFrontTorqueRatio, m_frontTorqueRatio, Mathf.Clamp01(estimatedSpeedKph / 60f));
        float axleRatio = isFront ? dynamicFrontRatio : 1f - dynamicFrontRatio;
        float baseWheelTorque = m_inputTorque * m_differentialGearRatio * axleRatio * 0.5f;

        //前後どちらかだけが空転した時、センターカップリング相当の範囲で遅い側へトルクを移す。
        float frontSpeed = (Mathf.Abs(m_wheelAngularVelocity_LeftFront) + Mathf.Abs(m_wheelAngularVelocity_RightFront)) * 0.5f;
        float rearSpeed = (Mathf.Abs(m_wheelAngularVelocity_LeftRear) + Mathf.Abs(m_wheelAngularVelocity_RightRear)) * 0.5f;
        float centerRequest = (frontSpeed - rearSpeed) * m_wheelInertia / Mathf.Max(Time.fixedDeltaTime, 0.001f) * m_centerLockRatio * 0.5f;
        float centerLimit = Mathf.Abs(m_inputTorque * m_differentialGearRatio) * 0.12f;
        float centerTransfer = Mathf.Clamp(centerRequest, -centerLimit, centerLimit);
        baseWheelTorque += isFront ? -centerTransfer : centerTransfer;

        float leftSpeed = isFront ? m_wheelAngularVelocity_LeftFront : m_wheelAngularVelocity_LeftRear;
        float rightSpeed = isFront ? m_wheelAngularVelocity_RightFront : m_wheelAngularVelocity_RightRear;

        switch (m_differentialType)
        {
            case DifferentialType.None: m_lockRatio = 0f; break;
            case DifferentialType.Open: m_lockRatio = 0f; break;
            case DifferentialType.Lock: m_lockRatio = 1f; break;
            default: m_lockRatio = m_customLockRatio_LSD; break;
        }

        //Torsen LSDらしく速く空転する側から遅い側へ移す。総トルク量は変えない。
        float speedDifference = leftSpeed - rightSpeed;
        float requestedTransfer = speedDifference * m_wheelInertia / Mathf.Max(Time.fixedDeltaTime, 0.001f) * m_lockRatio;
        float transferLimit = Mathf.Abs(baseWheelTorque) * 0.45f;
        float transferTorque = Mathf.Clamp(requestedTransfer, -transferLimit, transferLimit);

        return isRight ? baseWheelTorque + transferTorque : baseWheelTorque - transferTorque;
    }

    public float GetShaftVelocity(float wheelAngularVelocity, float wheelInertia, bool isFront, bool isRight)
    {
        if (isFront)
        {
            if (isRight) m_wheelAngularVelocity_RightFront = wheelAngularVelocity;
            else m_wheelAngularVelocity_LeftFront = wheelAngularVelocity;
        }
        else
        {
            if (isRight) m_wheelAngularVelocity_RightRear = wheelAngularVelocity;
            else m_wheelAngularVelocity_LeftRear = wheelAngularVelocity;
        }

        m_wheelInertia = wheelInertia;
        float averageWheelSpeed = (m_wheelAngularVelocity_LeftFront + m_wheelAngularVelocity_RightFront +
            m_wheelAngularVelocity_LeftRear + m_wheelAngularVelocity_RightRear) * 0.25f;
        return averageWheelSpeed * m_differentialGearRatio;
    }

}
