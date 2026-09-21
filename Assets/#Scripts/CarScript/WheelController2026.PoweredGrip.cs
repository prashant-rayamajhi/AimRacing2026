using UnityEngine;

// 駆動中の後輪グリップを配分してTrackの滑りを急に消さないクラス
public partial class WheelController2026
{
    // 比較検証と調整で加速ドリフトの横グリップ配分を切り替える設定
    [Header("Track Powered Grip")]
    [SerializeField]
    bool m_trackPoweredGripEnabled = true;
    // 横滑りを残しても操縦に必要な後輪の横グリップを確保する下限
    [SerializeField, Range(0.75f, 1f)]
    float m_trackPoweredGripMinimum = 0.82f;
    // 通常のコーナーではグリップを減らさないための滑り開始角
    [SerializeField, Min(0f)]
    float m_trackPoweredGripStartDegrees = 3f;
    // 十分に滑った状態でグリップ配分を全量適用する角度
    [SerializeField, Min(1f)]
    float m_trackPoweredGripFullDegrees = 8f;
    // スピンに近付いたときに通常グリップへ戻し始める角度
    [SerializeField, Min(1f)]
    float m_trackPoweredGripRecoveryDegrees = 12f;
    // 大きく姿勢が崩れた場合にグリップ緩和を終了する角度
    [SerializeField, Min(1f)]
    float m_trackPoweredGripStopDegrees = 18f;
    // 踏み直しで後輪の横力を急変させない応答時間
    [SerializeField, Range(0.05f, 0.5f)]
    float m_trackPoweredGripResponseSeconds = 0.18f;
    // 空転が収まる途中で急に横グリップを戻さずカウンターの修正回数を減らす時間
    [SerializeField, Range(0.05f, 0.6f)]
    float m_trackPoweredGripReturnSeconds = 0.32f;
    // 発進と微速ではグリップ配分を変更しない最低速度
    [SerializeField, Min(0f)]
    float m_trackPoweredGripMinimumKph = 25f;
    // 各輪の配分を独立して平滑化する横グリップ倍率
    readonly float[] m_poweredGripScale =
    {
        1f,
        1f,
        1f,
        1f
    };
    // 走行ログで後輪の横グリップ緩和量を確認する関数
    public float GetPoweredGripScale(int index)
    {
        if (index < 0 || index >= m_poweredGripScale.Length)
        {
            return 1f;
        }

        return m_poweredGripScale[index];
    }

    // 縦方向の駆動負荷から残す横グリップを摩擦円の形で求める関数
    static float CalculatePoweredGripScale(float usage, float blend, float minimum)
    {
        // 駆動需要が増えても横グリップを零へ落とさず制御余地を残す
        float longitudinal = Mathf.Clamp01(usage);
        float remaining = Mathf.Sqrt(Mathf.Max(0f, 1f - longitudinal * longitudinal));
        return Mathf.Lerp(1f, Mathf.Max(Mathf.Clamp01(minimum), remaining), Mathf.Clamp01(blend));
    }

    // 空転抑制後のトルクだけでグリップ回復を判断せず実際の加速空転も含める関数
    static float CalculatePoweredGripUsage(float torqueUsage, float accelerationSlip, float peakSlip)
    {
        // 摩擦曲線のピークまでの空転を正規化し制動滑りは加速空転に含めない
        float spinUsage = Mathf.Clamp01(Mathf.Max(0f, accelerationSlip) / Mathf.Max(0.01f, peakSlip));
        return Mathf.Clamp01(Mathf.Max(torqueUsage, spinUsage));
    }

    // 変速中も残る加速空転を維持し制動中には追加のグリップ緩和を始めない関数
    static bool HasPoweredGripDemand(float netDrive, float accelerationSlip, float brakeTorque)
    {
        return netDrive > 0f || (accelerationSlip > 0f && brakeTorque <= 0f);
    }

    // 横グリップの復帰だけゆっくり行い危険な姿勢では元の応答速度を使う関数
    static float SmoothPoweredGrip(float current, float target, float attack, float release, bool recovery, float deltaTime)
    {
        float seconds = target > current && !recovery ? Mathf.Max(attack, release) : attack;
        float response = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / Mathf.Max(0.01f, seconds));
        return Mathf.Lerp(current, target, response);
    }

    // 速度を直接書き換えず後輪の摩擦だけで加速中の滑りを残す関数
    void ApplyTrackPoweredGrip(int index, WheelCollider wheel, bool grounded, WheelHit hit)
    {
        // NormalとSportと前輪の接地特性は変更しない
        float track = m_differential.TrackHandlingBlend;
        if (!m_trackPoweredGripEnabled || m_IsFront[index] || !grounded || track <= 0f)
        {
            m_poweredGripScale[index] = 1f;
            return;
        }

        // 停止中や制動中や後退中には加速ドリフトの緩和を持ち込まない
        Vector3 velocity = m_vehicleRigidbody.transform.InverseTransformDirection(m_vehicleRigidbody.linearVelocity);
        float target = 1f;
        float slip = Mathf.Abs(Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg);
        float netDrive = Mathf.Max(0f, wheel.motorTorque - wheel.brakeTorque);
        // 変速や駆動制限でトルクが一瞬消えても実際の空転が残る間は滑りを急に消さない
        float accelerationSlip = GetAccelerationSlip(hit.forwardSlip);
        bool driveDemand = HasPoweredGripDemand(netDrive, accelerationSlip, wheel.brakeTorque);
        if (velocity.z * 3.6f > m_trackPoweredGripMinimumKph && driveDemand)
        {
            // 横速度が既にあるときだけ作用し直進から勝手に滑りを作らない
            float drift = Mathf.InverseLerp(m_trackPoweredGripStartDegrees, m_trackPoweredGripFullDegrees, slip);
            float recovery = Mathf.InverseLerp(m_trackPoweredGripRecoveryDegrees, m_trackPoweredGripStopDegrees, slip);
            // Nmをタイヤ半径と接地荷重で割り縦方向のグリップ使用率へ換算する
            float peak = wheel.forwardFriction.extremumValue * wheel.forwardFriction.stiffness;
            float capacity = Mathf.Max(1f, hit.force * wheel.radius * peak);
            // ESCの小さな制動で緩和を突然解除せず実際の駆動と空転に合わせる
            float usage = CalculatePoweredGripUsage(netDrive / capacity, accelerationSlip, wheel.forwardFriction.extremumSlip);
            target = CalculatePoweredGripScale(usage, track * drift * (1f - recovery), m_trackPoweredGripMinimum);
        }

        // 物理更新周期に依存しない補間で摩擦の変更を滑らかにする
        bool recovering = slip >= m_trackPoweredGripRecoveryDegrees || (wheel.brakeTorque > 0f && wheel.brakeTorque >= wheel.motorTorque);
        m_poweredGripScale[index] = SmoothPoweredGrip(m_poweredGripScale[index], target, m_trackPoweredGripResponseSeconds, m_trackPoweredGripReturnSeconds, recovering, Time.fixedDeltaTime);
        WheelFrictionCurve lateral = wheel.sidewaysFriction;
        lateral.stiffness *= m_poweredGripScale[index];
        wheel.sidewaysFriction = lateral;
    }
}
