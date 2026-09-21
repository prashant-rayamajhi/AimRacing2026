using UnityEngine;

// 接地した車体の追従感と制御可能な滑りをモード別に補うクラス
public partial class VehicleController
{
    // 物理タイヤの横力を残したまま追加のヨー補助を有効にする設定
    [Header("Mode Handling")]
    [SerializeField]
    bool m_modeYawAssistEnabled = true;
    // Sportで前側についてくる後輪に許す小さな車体スリップ角
    [SerializeField, Range(0f, 8f)]
    float m_sportTargetSlipDegrees = 3f;
    // Trackで旋回中に許す車体スリップ角で路面摩擦による限界は別に残す
    [SerializeField, Range(0f, 12f)]
    float m_trackTargetSlipDegrees = 6f;
    // 車体の横向きと速度方向のずれを回復させる時定数
    [SerializeField, Range(0.2f, 1.5f)]
    float m_slipFollowSeconds = 0.65f;
    // 目標ヨー速度へ滑らかに近付く時定数
    [SerializeField, Range(0.15f, 1f)]
    float m_modeYawFollowSeconds = 0.35f;
    // Sportの前側を先導する補助を弱く残し旋回出口の回り過ぎを防ぐ割合
    [SerializeField, Range(0f, 0.5f)]
    float m_sportYawFollowStrength = 0.2f;
    // 車体を強制回転させ過ぎないための補助角加速度上限
    [SerializeField, Range(0f, 2f)]
    float m_modeMaximumYawAcceleration = 0.8f;
    // 微速と発進で滑り演出を始めないための最低速度
    [SerializeField, Min(0f)]
    float m_modeHandlingStartKph = 25f;
    // 旋回中の滑り補助を全量使う速度
    [SerializeField, Min(1f)]
    float m_modeHandlingFullKph = 65f;
    // Sportの自然な追従をESCがすぐ止めないための滑り検出倍率
    [SerializeField, Range(1f, 2f)]
    float m_sportESCThresholdScale = 1.25f;
    // 検証時に補助が要求した車体スリップ角を表示する値
    [SerializeField, ShowInInspector]
    float m_handlingTargetSlipDegrees;
    // 検証時に実際に加えたヨー角加速度を表示する値
    [SerializeField, ShowInInspector]
    float m_handlingYawAcceleration;
    // 停車や空中や壁接触中に旋回演出を重ねないための関数
    bool CanApplyModeHandling()
    {
        if (!m_modeYawAssistEnabled || !m_escEnabled || m_IsPullUp || m_rigidbody == null || m_mission == null)
        {
            return false;
        }

        if (m_differential == null || m_mission.ActiveGear <= 0 || GroundedWheelCount < 3)
        {
            return false;
        }

        if (m_sideImpactStabilityTime > 0f || Time.fixedTime - m_wallSpinContactTime < m_wallSpinRecoverySeconds)
        {
            return false;
        }

        return Vector3.Dot(m_rigidbody.linearVelocity, transform.forward) * 3.6f > m_modeHandlingStartKph;
    }

    // 旋回中に許す滑り幅を求め限界内ではタイヤの自然な追従を残す関数
    void UpdateModeSlipAllowance(float baseYawRate, Vector3 localVelocity, float gripLimit)
    {
        m_handlingTargetSlipDegrees = 0f;
        // Trackの復帰判定をESCとヨー補助の両方で同じ物理更新から使う
        UpdateTrackDriftState();
        if (!CanApplyModeHandling())
        {
            return;
        }

        // 直進では滑りを作らず旋回時だけモードの後輪追従量を使う
        float sport = m_differential.SportHandlingBlend;
        float track = m_differential.TrackHandlingBlend;
        float speedBlend = Mathf.InverseLerp(m_modeHandlingStartKph, m_modeHandlingFullKph, localVelocity.z * 3.6f);
        float turnDemand = Mathf.Clamp(baseYawRate / Mathf.Max(0.01f, gripLimit), -1f, 1f);
        m_handlingTargetSlipDegrees = -turnDemand * speedBlend * (sport * m_sportTargetSlipDegrees + track * m_trackTargetSlipDegrees);
    }

    // ヨー軸だけに上限付き補助を加えサスペンションや車速を直接書き換えない関数
    void ApplyModeYawAssist()
    {
        m_handlingYawAcceleration = 0f;
        if (!CanApplyModeHandling())
        {
            return;
        }

        // 駆動配分と同じ割合で補助を切り替えNormalでは既存ESCだけを使う
        float modeAmount = Mathf.Clamp01(m_differential.SportHandlingBlend + m_differential.TrackHandlingBlend);
        float speed = Vector3.Dot(m_rigidbody.linearVelocity, transform.forward) * 3.6f;
        float speedBlend = Mathf.InverseLerp(m_modeHandlingStartKph, m_modeHandlingFullKph, speed);
        float yaw = Vector3.Dot(m_rigidbody.angularVelocity, transform.up);
        // Trackは滑りを強制せず許容角を超えた分だけ速度方向へ車体を戻す
        // ESCが許している滑りを別の補助が先に打ち消さないよう共通のしきい値を使う
        float allowance = Mathf.Max(GetModeSlipThreshold(), Mathf.Abs(m_handlingTargetSlipDegrees));
        float excessSlip = Mathf.Sign(m_escSlipAngle) * Mathf.Max(0f, Mathf.Abs(m_escSlipAngle) - allowance) * Mathf.Deg2Rad;
        float followTime = Mathf.Max(0.2f, m_slipFollowSeconds);
        float acceleration = excessSlip / (followTime * followTime);
        // Trackでは回復中にも押し戻し続けないよう滑りの変化速度を使う
        float track = m_differential.TrackHandlingBlend;
        if (track > 0f)
        {
            float recovery = CalculateTrackRecovery(m_escSlipAngle, m_trackSlipRateDegrees, allowance, followTime, m_trackRecoveryDamping);
            float maximum = Mathf.Max(0f, m_modeMaximumYawAcceleration);
            recovery = Mathf.Clamp(recovery, -maximum, maximum);
            // 滑りが収まった後や逆方向への切り返しに以前の復帰力を持ち越さない
            m_trackRecoveryAcceleration = LimitTrackRecovery(m_trackRecoveryAcceleration, recovery, m_trackYawAccelerationRate, Time.fixedDeltaTime);
            acceleration = Mathf.Lerp(acceleration, m_trackRecoveryAcceleration, track);
        }

        // Sportだけ弱いヨー追従を加え切り返しと戻しの操作を遅らせない
        float sportFollow = m_differential.SportHandlingBlend * m_sportYawFollowStrength;
        acceleration += (m_escDesiredYawRate - yaw) / Mathf.Max(0.15f, m_modeYawFollowSeconds) * sportFollow;
        float limit = Mathf.Max(0f, m_modeMaximumYawAcceleration);
        m_handlingYawAcceleration = Mathf.Clamp(acceleration, -limit, limit) * modeAmount * speedBlend * Mathf.Clamp01(m_drivingAssistStrength);
        m_rigidbody.AddTorque(transform.up * m_handlingYawAcceleration, ForceMode.Acceleration);
    }

    // モード切替中もESCと車体復帰で同じ滑り開始角度を使う関数
    float GetModeSlipThreshold()
    {
        // 駆動モードの補間率を使い切替直後の補助の急変を防ぐ
        float track = m_differential != null ? Mathf.Clamp01(m_differential.TrackHandlingBlend) : 0f;
        float sport = m_differential != null ? Mathf.Clamp01(m_differential.SportHandlingBlend) : 0f;
        // 通常の安定制御より狭い許容幅にならないよう入力値を制限する
        float scale = 1f + (Mathf.Max(1f, m_trackESCSlipThresholdScale) - 1f) * track;
        scale += (Mathf.Max(1f, m_sportESCThresholdScale) - 1f) * sport;
        float threshold = Mathf.Max(0.01f, m_escSlipAngleThreshold) * scale;
        // NormalとSportの設定は維持しTrackの滑り幅だけペダル操作で調整する
        if (!m_trackDriftHistoryValid)
        {
            return threshold;
        }

        float oldTrackThreshold = Mathf.Max(0.01f, m_escSlipAngleThreshold) * Mathf.Max(1f, m_trackESCSlipThresholdScale);
        return Mathf.Max(0.01f, threshold + (m_trackSlipAllowanceDegrees - oldTrackThreshold) * track);
    }
}
