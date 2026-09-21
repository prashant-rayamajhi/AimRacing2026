using UnityEngine;

// エンジンと変速機の回転差からクラッチの伝達トルクを計算するクラス
public class Clutch : MonoBehaviour
{
    // ATでクラッチ接続を自動制御するための設定
    [SerializeField]
    bool m_clutchAuto = true;
    // 現在のクラッチが伝達できる最大トルク
    [SerializeField, ShowInInspector]
    float m_Calclate_ClutchMaxTorque;
    // クラッチ板を押し付ける圧着力
    [SerializeField, ShowInInspector]
    float m_CrimpingForce;
    // AT発進時に半クラッチから完全接続へ移る回転数範囲
    [SerializeField]
    Vector2 m_lockRange = new Vector2(900f, 1400f);
    // ATクラッチを接続する速さ
    [SerializeField, Range(0.5f, 20f)]
    float m_autoEngagementRate = 5f;
    // 変速時にATクラッチを切り離す速さ
    [SerializeField, Range(0.5f, 30f)]
    float m_autoReleaseRate = 15f;
    // AT変速中も速度を途切れさせないために残すクラッチ接続率
    [SerializeField, Range(0.2f, 0.9f)]
    float m_automaticShiftMinimumEngagement = 0.55f;
    // 最大エンジントルクに耐えるクラッチ設計トルク
    [SerializeField]
    float m_DesignTorque = 700f;
    // クラッチ板の摩擦力を求める摩擦係数
    [SerializeField]
    float m_frictionCoef = 0.55f;
    // クラッチ板の有効半径を求める外径
    [SerializeField]
    float m_ClutchOD = 0.35f;
    // クラッチ板の有効半径を求める内径
    [SerializeField]
    float m_ClutchID = 0.25f;
    // クラッチの伝達容量を求める摩擦面数
    [SerializeField]
    float m_ClutchSurface = 4f;
    // 変速中にクラッチを切るための状態
    [SerializeField, ShowInInspector]
    bool m_isGearChanging;
    // イントロ中にクラッチを切るための車体固定状態
    [SerializeField, ShowInInspector]
    bool m_isPullUp;
    // 変速機へ渡す現在のクラッチトルク
    [SerializeField, ShowInInspector]
    float m_OutputTorque;
    // 半クラッチの伝達トルクを求める入出力回転差
    [SerializeField, ShowInInspector]
    float m_clutchSlip;
    // 車輪側から戻ってきたクラッチ角速度
    [SerializeField, ShowInInspector]
    float m_clutchAngularVelocity;
    // エンジン側のクラッチ角速度
    [SerializeField, ShowInInspector]
    float m_engineAngularVelocity;
    // エンジンからクラッチへ入力されるトルク
    [SerializeField, ShowInInspector]
    float m_engineTorque;
    // クラッチの現在接続率
    [SerializeField, ShowInInspector]
    float m_clutchInput;
    // 旧デバッグ表示との互換性を保つクラッチロック値
    [SerializeField, ShowInInspector]
    float m_clutchLock;
    // クラッチ接続変化による振動量
    [SerializeField, ShowInInspector]
    float m_clutchInput_diff;
    // クラッチ接続変化を求める前回値
    float m_clutchInput_prev;
    // 旧振動処理の振幅割合
    [SerializeField]
    float m_AmplitudeRatio;
    // 旧振動処理を収束させる減衰割合
    [SerializeField]
    float m_DampingRatio;
    // 旧振動処理の振動速度
    [SerializeField]
    float m_AmplitudeSpeed;
    // 現在ギアのクラッチ接続条件を判断するギア比
    float m_gearRatio;
    // 旧振動処理の開始時刻
    float m_zeroPoint;
    // Trackレバーだけ回転差から双方向にトルクを伝える設定
    public bool PhysicalManualCoupling { get; set; }

    // 回転差を解消するトルクの応答時間で接続ショックを調整する値
    [SerializeField, Range(0.02f, 0.2f)]
    float m_manualCouplingSeconds = 0.04f;
    // 回転差トルクを次元の合った値へ換算するエンジン慣性
    float m_engineInertia;
    // 展示用MTの停止発進でエンジンがアイドルへ押し戻されることを防ぐ設定
    [SerializeField]
    bool m_manualLaunchAssist = true;
    // 全開で発進する時にクラッチを滑らせて立ち上げる目標回転数
    [SerializeField, Range(1400f, 3000f)]
    float m_manualLaunchRPM = 2000f;
    // 発進回転へ近づけるためにクラッチ負荷を調整する応答時間
    [SerializeField, Range(0.1f, 0.5f)]
    float m_manualLaunchResponseSeconds = 0.2f;
    // 車両側が許可したMT1速の発進目標角速度
    float m_manualLaunchTargetAngularVelocity;
    // 発進時の滑りをRPM直接同期で打ち消さないための状態
    public bool ManualLaunchSlipActive { get; private set; }

    // MT1速でブレーキを離して加速する時だけ発進補助を準備する関数
    public void ConfigureManualLaunch(bool allowed, float throttle, float idleAngularVelocity)
    {
        float target = Mathf.Max(idleAngularVelocity, m_manualLaunchRPM * CarPhysics.RPM2Rad);
        m_manualLaunchTargetAngularVelocity = allowed && m_manualLaunchAssist && throttle > 0f ? Mathf.Lerp(idleAngularVelocity, target, Mathf.Clamp01(throttle)) : 0f;
    }

    // 現在のクラッチ伝達トルク
    public float ClutchTorque => m_OutputTorque;
    // 現在のクラッチ接続率
    public float Engagement => m_clutchInput;
    // イントロ中の車体固定状態
    public bool IsPullUp { get => m_isPullUp; set => m_isPullUp = value; }
    // 現在の変速状態
    public bool GearChanging { get => m_isGearChanging; set => m_isGearChanging = value; }
    // ATクラッチ制御の有効状態
    public bool AutoClutch { get => m_clutchAuto; set => m_clutchAuto = value; }

    // MTから受け取るクラッチ接続率
    public float ClutchInput
    {
        set
        {
            m_clutchInput = Mathf.Clamp01(value);
            // 切断時に前回のトルクが次のギアへ一物理更新だけ漏れることを防ぐ
            if (m_clutchInput <= 0f)
            {
                m_OutputTorque = 0f;
            }
        }
    }

    // 旧振動演出へ渡す振動量
    public float Oscillation { set => m_clutchInput_diff = value; }

    // エンジン側と車輪側の状態からクラッチトルクを更新する関数
    public void DrivetrainUpdate(in float clutchOutputSide, in float engineAngularVelocity, in float engineOutputTorque, in float gearRatio, in float engineInertia)
    {
        m_clutchAngularVelocity = clutchOutputSide;
        m_engineAngularVelocity = engineAngularVelocity;
        m_engineTorque = engineOutputTorque;
        m_gearRatio = gearRatio;
        m_engineInertia = Mathf.Max(0.001f, engineInertia);
        m_clutchSlip = m_engineAngularVelocity - m_clutchAngularVelocity;
        CalcClutchTorque();
    }

    // 復帰前の回転差や伝達トルクを消去する関数
    public void ResetDynamics()
    {
        m_manualLaunchTargetAngularVelocity = 0f;
        ManualLaunchSlipActive = false;
        m_OutputTorque = 0f;
        m_clutchSlip = 0f;
        m_clutchAngularVelocity = 0f;
        m_engineAngularVelocity = 0f;
        m_engineTorque = 0f;
        m_clutchInput = m_clutchAuto ? 0f : m_clutchInput;
        m_clutchLock = m_clutchInput;
        m_clutchInput_diff = 0f;
        m_clutchInput_prev = m_clutchInput;
    }

    // クラッチの摩擦容量と回転差から伝達トルクを計算する関数
    void CalcClutchTorque()
    {
        ManualLaunchSlipActive = false;
        // MTでも開始待機中は伝達を切り、1速の停止車輪が空ぶかしを妨げないようにする
        if (m_isPullUp)
        {
            // ATの待機中は接続率も戻し、解除時に一度で完全接続しない
            if (m_clutchAuto)
            {
                m_clutchInput = 0f;
            }

            m_OutputTorque = 0f;
            return;
        }

        if (m_clutchAuto)
        {
            UpdateAutomaticClutch();
        }

        float effectiveRadius = (m_ClutchOD + m_ClutchID) / 4f;
        float diskForceRatio = m_frictionCoef * effectiveRadius * m_ClutchSurface;
        m_CrimpingForce = m_DesignTorque / Mathf.Max(0.001f, diskForceRatio) * m_clutchInput;
        m_Calclate_ClutchMaxTorque = diskForceRatio * m_CrimpingForce;
        if (PhysicalManualCoupling)
        {
            // Nと全踏みでは駆動を切り回転合わせにアクセルだけを使えるようにする
            if (m_clutchInput <= 0f || m_gearRatio == 0f)
            {
                m_OutputTorque = 0f;
                return;
            }

            float responseSeconds = Mathf.Max(Time.fixedDeltaTime, m_manualCouplingSeconds);
            float requested = (m_engineTorque + m_clutchSlip * m_engineInertia / responseSeconds) * m_clutchInput;
            m_OutputTorque = Mathf.Clamp(requested, -m_Calclate_ClutchMaxTorque, m_Calclate_ClutchMaxTorque);
            ApplyManualLaunchLoadLimit();
            return;
        }

        if (m_clutchInput <= 0f || m_engineTorque <= 0f)
        {
            m_OutputTorque = 0f;
            return;
        }

        float couplingTorque = m_clutchSlip * 0.8f * m_clutchInput;
        float requestedTorque = m_engineTorque * m_clutchInput + couplingTorque;
        m_OutputTorque = Mathf.Clamp(requestedTorque, -m_Calclate_ClutchMaxTorque, m_Calclate_ClutchMaxTorque);
        ApplyManualLaunchLoadLimit();
    }

    // 車輪が発進回転に追いつくまでエンジンを加速させる余力を残す関数
    void ApplyManualLaunchLoadLimit()
    {
        if (m_manualLaunchTargetAngularVelocity <= 0f || m_clutchInput <= 0f || m_gearRatio <= 0f)
        {
            return;
        }

        if (Mathf.Abs(m_clutchAngularVelocity) >= m_manualLaunchTargetAngularVelocity)
        {
            return;
        }

        ManualLaunchSlipActive = true;
        // 必要なエンジン加速トルクを差し引き回転数を直接書き換えず発進時だけ負荷を減らす
        float response = Mathf.Max(Time.fixedDeltaTime, m_manualLaunchResponseSeconds);
        float reserveTorque = m_engineInertia * (m_manualLaunchTargetAngularVelocity - m_engineAngularVelocity) / response;
        float allowedTorque = Mathf.Max(0f, m_engineTorque - reserveTorque);
        if (m_OutputTorque > 0f)
        {
            m_OutputTorque = Mathf.Min(m_OutputTorque, allowedTorque);
        }
    }

    // AT発進時の回転数に応じてクラッチを滑らかに接続する関数
    void UpdateAutomaticClutch()
    {
        if (m_gearRatio == 0f || m_isPullUp)
        {
            // イントロとニュートラルではクラッチを切ってエンジン回転を駆動輪から解放する
            m_clutchInput = 0f;
            m_clutchLock = 0f;
            return;
        }

        float engineRPM = Mathf.Abs(m_engineAngularVelocity) * CarPhysics.Rad2RPM;
        float drivetrainRPM = Mathf.Abs(m_clutchAngularVelocity) * CarPhysics.Rad2RPM;
        float couplingRPM = Mathf.Max(engineRPM, drivetrainRPM);
        float runningEngagement = Mathf.InverseLerp(m_lockRange.x, Mathf.Max(m_lockRange.x + 1f, m_lockRange.y), couplingRPM);
        float targetEngagement = m_isGearChanging ? Mathf.Min(runningEngagement, m_automaticShiftMinimumEngagement) : runningEngagement;
        float responseRate = targetEngagement > m_clutchInput ? m_autoEngagementRate : m_autoReleaseRate;
        m_clutchInput = Mathf.MoveTowards(m_clutchInput, targetEngagement, responseRate * Time.fixedDeltaTime);
        m_clutchLock = m_clutchInput;
    }
}
