// enum DifferentialType
// Open,           //オープンデフ(高回転している方に多く分配)
// Lock,           //デフロック(左右の回転を同じにする)
// LimitedSlip,    //LSD(左右の回転差を一定の割合で止める)
// 最終減速比
// 駆動輪に渡す駆動トルクを取得
// プロペラシャフトのトルクと最終減速比を掛け合わせて2で割ったものを分配トルクとする
// 左右の角速度の差を2で割ったもの
// 左駆動輪の角速度からこれを引いて、右駆動輪の角速度にこれを足せば左右の角速度は同じになる
// 角速度からトルクへの変換を行う
// 計算式
// これをそのまま駆動トルクとして渡すと左右の差が無くなる
// デフの種類によってロック率を変える
// switch (m_differentialType)
// case DifferentialType.None:
// case DifferentialType.Open:
// case DifferentialType.Lock:
// case DifferentialType.LimitedSlip:
// 左右の回転差を考慮して分配トルクを計算
// 現在の駆動輪のホイールの速度からシャフトの回転数を求める
// 慣性は左右等しいとする
// シャフトの速度は左右駆動輪の角速度の平均 * 最終減速比
using UnityEngine;

[System.Serializable]
public partial class Differential : MonoBehaviour
{
    enum DifferentialType
    {
        None = 0,
        Open,
        Lock,
        LimitedSlip
    }

    [SerializeField]
    float m_differentialGearRatio = 3.941f;
    [SerializeField]
    DifferentialType m_differentialType = DifferentialType.LimitedSlip;
    [SerializeField, ShowInInspector]
    float m_lockRatio;
    [UnityEngine.Serialization.FormerlySerializedAs("m_customLockRatio_LSD")]
    [SerializeField, Range(0f, 1f)]
    float m_customLockRatioLSD = 0.35f;
    // ノーマルで前輪に渡す基本配分を保存し、既存シーンの設定を引き継ぐ
    [Header("駆動モード Normal")]
    [SerializeField, Range(0f, 1f)]
    float m_frontTorqueRatio = 0.60f;
    // 旧発進配分の保存値を残すが、選択モードの配分には上書きしない
    [HideInInspector]
#pragma warning disable CS0414 // 意図的に保存だけしておく旧設定値(選択モードの配分には使わない)

    [SerializeField, Range(0f, 1f)]
    float m_launchFrontTorqueRatio = 0.50f;
#pragma warning restore CS0414
    [SerializeField, Range(0f, 1f)]
    float m_centerLockRatio = 0.35f;
    public void SetFinalDriveRatio(float _ratio)
    {
        m_differentialGearRatio = Mathf.Max(0.01f, _ratio);
    }

    float m_wheelAngularVelocityLeftFront;
    float m_wheelAngularVelocityLeftRear;
    float m_wheelAngularVelocityRightFront;
    float m_wheelAngularVelocityRightRear;
    float m_wheelInertia;
    // 入力トルク
    float m_inputTorque;
    public float InputTorque
    {
        get
        {
            return m_inputTorque;
        }

        set
        {
            m_inputTorque = value;
        }
    }

    public float GetDriveTorque(bool _isFront, bool _isRight)
    {
        // 選択モードの配分を四輪で共有し、発進中もスポーツやトラックの指定を保つ
        float dynamicFrontRatio = AppliedFrontTorqueRatio;
        float axleRatio = _isFront ? dynamicFrontRatio : 1f - dynamicFrontRatio;
        float baseWheelTorque = m_inputTorque * m_differentialGearRatio * axleRatio * 0.5f;
        // 回転方向を残した前後差を使い、後退でも空転している車軸の回転を抑える
        float frontSpeed = (m_wheelAngularVelocityLeftFront + m_wheelAngularVelocityRightFront) * 0.5f;
        float rearSpeed = (m_wheelAngularVelocityLeftRear + m_wheelAngularVelocityRightRear) * 0.5f;
        float centerRequest = (frontSpeed - rearSpeed) * m_wheelInertia / Mathf.Max(Time.fixedDeltaTime, 0.001f) * m_centerLockRatio * 0.5f;
        float centerLimit = Mathf.Abs(m_inputTorque * m_differentialGearRatio) * 0.12f;
        // 配分をエディタで極端に変えても、移動元のトルクを超えて逆駆動させない
        float availablePerWheel = Mathf.Abs(m_inputTorque * m_differentialGearRatio) * Mathf.Min(dynamicFrontRatio, 1f - dynamicFrontRatio) * 0.5f;
        centerLimit = Mathf.Min(centerLimit, availablePerWheel);
        float centerTransfer = Mathf.Clamp(centerRequest, -centerLimit, centerLimit);
        baseWheelTorque += _isFront ? -centerTransfer : centerTransfer;
        float leftSpeed = _isFront ? m_wheelAngularVelocityLeftFront : m_wheelAngularVelocityLeftRear;
        float rightSpeed = _isFront ? m_wheelAngularVelocityRightFront : m_wheelAngularVelocityRightRear;
        switch (m_differentialType)
        {
            case DifferentialType.None:
                m_lockRatio = 0f;
                break;
            case DifferentialType.Open:
                m_lockRatio = 0f;
                break;
            case DifferentialType.Lock:
                m_lockRatio = 1f;
                break;
            default:
                m_lockRatio = m_customLockRatioLSD;
                break;
        }

        // Torsen LSDらしく速く空転する側から遅い側へ移す。総トルク量は変えない。
        float speedDifference = leftSpeed - rightSpeed;
        float requestedTransfer = speedDifference * m_wheelInertia / Mathf.Max(Time.fixedDeltaTime, 0.001f) * m_lockRatio;
        float transferLimit = Mathf.Abs(baseWheelTorque) * 0.45f;
        float transferTorque = Mathf.Clamp(requestedTransfer, -transferLimit, transferLimit);
        return _isRight ? baseWheelTorque + transferTorque : baseWheelTorque - transferTorque;
    }

    public float GetShaftVelocity(float _wheelAngularVelocity, float _wheelInertia, bool _isFront, bool _isRight)
    {
        if (_isFront)
        {
            if (_isRight)
                m_wheelAngularVelocityRightFront = _wheelAngularVelocity;
            else
                m_wheelAngularVelocityLeftFront = _wheelAngularVelocity;
        }
        else
        {
            if (_isRight)
                m_wheelAngularVelocityRightRear = _wheelAngularVelocity;
            else
                m_wheelAngularVelocityLeftRear = _wheelAngularVelocity;
        }

        m_wheelInertia = _wheelInertia;
        float averageWheelSpeed = (m_wheelAngularVelocityLeftFront + m_wheelAngularVelocityRightFront + m_wheelAngularVelocityLeftRear + m_wheelAngularVelocityRightRear) * 0.25f;
        return averageWheelSpeed * m_differentialGearRatio;
    }
#region Legacy fixed four-way split (kept for comparison)
#endregion
}
