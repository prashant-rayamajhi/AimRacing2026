using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

// エンジンから4輪までの駆動処理と車体安定制御を統合するクラス
public partial class VehicleController : MonoBehaviour
{
    [Space]
    [SerializeField]
    WheelController2026 m_wheelController;
    [SerializeField, ShowInInspector]
    float m_KPH;
    [Space]
    [SerializeField]
    Car_Engine m_engine;
    [Space]
    [SerializeField]
    Clutch m_clutch;
    [Space]
    [SerializeField]
    Transmission m_mission;
    [Space]
    [SerializeField]
    Differential m_differential;
    [Space]
    [SerializeField]
    Brake m_brake;
    [Space]
    [SerializeField]
    Steering m_steering;
    // 車体速度と姿勢の物理計算に使用するRigidbody
    Rigidbody m_rigidbody;
    // 入力
    float m_steerInput;
    float m_accelInput = 0f;
    float m_brakeInput = 0f;
    // 踏み込み量は未操作で零とし接続率と混同しない
    float m_clutchInput = 0f;
    float m_previousBrakeInput = 0f;
    bool m_driveAwayPrepared;
    bool m_waitingForRecoveryRestart;
    float m_recoveryRequestedAt;
    [SerializeField, ShowInInspector]
    float m_lastRecoveryResponseSeconds;
    bool m_IsPullUp = false;
    [SerializeField, Range(1, 10)]
    int m_introGroundingFixedFrames = 4;
    int m_introGroundingFramesRemaining;
    public float m_HornInput = 0f;
    [SerializeField]
    MeterUIManager m_meterUIManager;
    // ATの低速クリープ制御
    [Header("AT Creep")]
    [SerializeField, Min(0f), InspectorName("Creep Torque (Nm)"), Tooltip("ATのアクセルOFF時に1速ギアへ入力するクリープトルクです。")]
    float m_atCreepTorque = 180f;
    [SerializeField, Min(0.1f), InspectorName("Creep Target Speed (km/h)"), Tooltip("ATクリープが目指す最高速度です。速度が近づくとトルクを徐々に弱めます。")]
    float m_atCreepTargetSpeed = 15f;
    [SerializeField, Range(600f, 2000f), InspectorName("Creep Engine RPM"), Tooltip("ATクリープ中に維持する最低エンジン回転数です。")]
    float m_atCreepEngineRPM = 1200f;
    [SerializeField, ShowInInspector]
    bool m_atCreepActive;
    [SerializeField, ShowInInspector]
    float m_atCreepDriveTorque;
    // 停止状態からすぐに発進させるための補助トルク
    [Header("Launch Response")]
    [SerializeField, Range(0f, 500f)]
    float m_launchAssistTorque = 180f;
    [SerializeField, Range(1f, 20f)]
    float m_launchAssistMaximumSpeedKph = 8f;
    [SerializeField, Range(1f, 8f)]
    float m_arcadeAccelerationTorqueMultiplier = 2.75f;
    [SerializeField, Range(80f, 160f)]
    float m_arcadeAccelerationFadeEndKph = 120f;
    [SerializeField, Range(1f, 20f)]
    float m_driveInputRiseRate = 5f;
    [SerializeField, Range(1f, 30f)]
    float m_driveInputFallRate = 12f;
    float m_smoothedDriveInput;
    // アクセルOFF時の減速を4WDの駆動輪へ伝える設定
    [Header("Engine Braking")]
    // エンジン抵抗をギア比で伝え、惰性旋回で余分な減速倍率を掛けないための調整値
    [SerializeField, Range(0f, 3f)]
    float m_engineBrakingStrength = 1f;
    // 前進旋回時だけエンジンブレーキを少し緩めてアクセルオフの失速感を抑える倍率
    [SerializeField, Range(0.5f, 1f)]
    float m_cornerCoastTorqueScale = 0.85f;
    // Trackで旋回中のエンジンブレーキだけを弱め減速の強さを調整する
    [SerializeField, Range(0.5f, 1f)]
    float m_trackCornerCoastScale = 0.7f;
    // Trackでは小さな横滑りを許し大きなスピンに至る前のESC保護は残す
    [SerializeField, Range(1f, 2f)]
    float m_trackESCSlipThresholdScale = 1.6f;
    // Trackで通常旋回を妨げないようヨー誤差に対する介入開始を遅らせる
    [SerializeField, Range(1f, 2f)]
    float m_trackESCYawThresholdScale = 1.3f;
    [SerializeField, Range(0f, 10f)]
    float m_engineBrakingMinimumSpeedKph = 2f;
    // アクセルオフ直後の減速を急に立ち上げない応答時間
    [SerializeField, Range(0.1f, 1f)]
    float m_coastBrakeResponseTime = 0.35f;
    // タイヤの横グリップを残すためエンジンブレーキだけで許可する減速度
    [SerializeField, Range(0.5f, 4f)]
    float m_coastMaximumDeceleration = 2.2f;
    // 駆動軸へ段差なく渡すエンジンブレーキトルク
    float m_appliedCoastTorque;
    // GR Yarisの最高速制限
    [Header("Speed Limiter")]
    [SerializeField, Min(1f)]
    float m_atMaximumSpeedKph = 230f;
    // 理論後退速度に対して実走行で使用する最高速度割合
    [Header("Reverse Speed")]
    [SerializeField, Range(0.8f, 1f)]
    float m_reverseMaximumSpeedRatio = 0.95f;
    // 展示走行で安全に後退できる速度へ抑える上限
    [SerializeField, Range(10f, 80f)]
    float m_reverseSpeedLimitKph = 45f;
    // 後退速度が上限付近で細かく制御を繰り返さないための解除速度差
    [SerializeField, Range(0.1f, 5f)]
    float m_reverseLimiterReleaseKph = 1.5f;
    // 現在のトランスミッションから計算した後退最高速度
    [SerializeField, ShowInInspector]
    float m_reverseMaximumSpeedKph;
    // 後退最高速度を超えたため駆動トルクを停止している状態
    [SerializeField, ShowInInspector]
    bool m_reverseLimiterActive;
    // ボディピッチフィール
    [Header("Body Pitch Feel")]
    [SerializeField, Range(0f, 0.5f)]
    float m_pitchResponse = 0.08f;
    [SerializeField, Range(1f, 4f)]
    float m_accelerationPitchBoost = 1.25f;
    [SerializeField, Range(0f, 6f)]
    float m_pitchDamping = 4.5f;
    [SerializeField, Range(1f, 15f)]
    float m_maxLongitudinalAcceleration = 10f;
    [SerializeField, Range(1f, 20f)]
    float m_accelerationFilterSpeed = 8f;
    [SerializeField, Range(0.1f, 8f)]
    float m_maxPitchAngularAcceleration = 1.8f;
    [SerializeField, Range(0.1f, 6f)]
    float m_maxNoseUpAngularAcceleration = 1.1f;
    // 車体の向きの変化を加減速と誤認しないよう前回の世界座標速度を保持する
    Vector3 m_previousPitchVelocity;
    float m_filteredLongitudinalAcceleration;
    bool m_pitchStateInitialized;
    // 旋回中の横滑りを抑えるESC設定
    [Header("ESC")]
    [SerializeField]
    bool m_escEnabled = true;
    [SerializeField, Range(5f, 40f)]
    float m_escMinimumSpeedKph = 10f;
    [SerializeField, Range(2f, 15f)]
    float m_escSlipAngleThreshold = 4.5f;
    [SerializeField, Range(0.05f, 1f)]
    float m_escYawErrorThreshold = 0.25f;
    // スリップ角が介入開始値の何倍で最大制御になるかを決める倍率
    [SerializeField, Range(1.2f, 3f)]
    float m_escFullSlipMultiplier = 1.8f;
    // ヨー誤差が介入開始値の何倍で最大制御になるかを決める倍率
    [SerializeField, Range(1.2f, 3f)]
    float m_escFullYawMultiplier = 2f;
    [SerializeField, Range(0f, 0.8f)]
    float m_escMaximumTorqueReduction = 0.3f;
    [SerializeField, Range(0f, 3000f)]
    float m_escMaximumWheelBrakeTorque = 700f;
    [SerializeField, Range(2f, 3f)]
    float m_escWheelBase = 2.56f;
    [SerializeField, Range(6f, 15f)]
    float m_escMaximumLateralAcceleration = 11.5f;
    [SerializeField, Range(0.5f, 10f)]
    float m_escYawResponseRate = 3f;
    [SerializeField, Range(0.5f, 20f)]
    float m_escYawRecenteringRate = 8f;
    // ESC制御を滑らかに強める速さ
    [SerializeField, Range(1f, 30f)]
    float m_escInterventionRiseRate = 8f;
    // グリップ回復後にESC制御を滑らかに戻す速さ
    [SerializeField, Range(1f, 30f)]
    float m_escInterventionReleaseRate = 7f;
    // ESCが中間域から急に強くならないよう介入率へ掛ける指数
    [SerializeField, Range(1f, 2f)]
    float m_escInterventionExponent = 1.35f;
    // アクセル中の通常旋回でESCが駆動力を奪いすぎないようにする介入倍率
    [SerializeField, Range(0.4f, 1f)]
    float m_escPoweredInterventionScale = 0.65f;
    // アクセルを離しただけで旋回補助の制動が増えないようにする通常惰性時の介入倍率
    [SerializeField, Range(0.4f, 1f)]
    float m_escCoastInterventionScale = 0.65f;
    [SerializeField, ShowInInspector]
    bool m_escActive;
    [SerializeField, ShowInInspector]
    float m_escSlipAngle;
    [SerializeField, ShowInInspector]
    float m_escDesiredYawRate;
    [SerializeField, ShowInInspector]
    float m_escAppliedIntervention;
    float m_escIntervention;
    int m_escBrakeWheelIndex = -1;
    // 高速コーナーでアクセル全開のままでも急な横滑りへ入りにくくする設定
    [Header("Corner Assist")]
    [SerializeField]
    bool m_cornerAssistEnabled = true;
    [SerializeField, Range(30f, 120f)]
    float m_cornerAssistStartSpeedKph = 85f;
    [SerializeField, Range(80f, 220f)]
    float m_cornerAssistFullSpeedKph = 170f;
    [SerializeField, Range(1f, 20f)]
    float m_cornerAssistStartSteerAngle = 8f;
    [SerializeField, Range(5f, 35f)]
    float m_cornerAssistFullSteerAngle = 22f;
    [SerializeField, Range(0.4f, 1f)]
    float m_cornerAssistMinimumTorqueRatio = 0.96f;
    // 横滑りが小さい通常旋回で残すコーナー補助の介入割合
    [SerializeField, Range(0f, 1f)]
    float m_cornerAssistLowSlipDemand = 0.25f;
    [SerializeField, Range(0.5f, 10f)]
    float m_cornerAssistRiseRate = 3f;
    [SerializeField, Range(0.5f, 10f)]
    float m_cornerAssistReleaseRate = 5f;
    float m_cornerAssistIntervention;
    // 道路下への落下を検出するための設定
    [Header("Road Fall Protection")]
    [SerializeField, Range(0.05f, 1f)]
    float m_fallProtectionDelay = 0.08f;
    [SerializeField, Range(0.1f, 2f)]
    float m_fallProtectionHeight = 0.25f;
    [SerializeField, Range(30f, 80f)]
    float m_rolloverRecoveryAngle = 50f;
    Vector3 m_lastSafePosition;
    Quaternion m_lastSafeRotation;
    bool m_hasSafePose;
    public Rigidbody Rigidbody => m_rigidbody;
    public Transmission Transmission => m_mission;
    public Car_Engine Engine => m_engine;
    public WheelController2026 WheelComtroller => m_wheelController;
    public float KPH => Mathf.Clamp(m_KPH, 0f, float.PositiveInfinity);
    public float EngineRPM => m_engine.RPM;
    public bool ESCActive => m_escActive;
    // 検証画面へESCの介入率を渡すプロパティ
    public float ESCIntervention => m_escIntervention;
    // 検証画面へ車体のスリップ角を渡すプロパティ
    public float ESCSlipAngle => m_escSlipAngle;
    // 検証画面へコーナー補助の介入率を渡すプロパティ
    public float CornerAssistIntervention => m_cornerAssistIntervention;
    public bool ABSActive => m_wheelController != null && m_wheelController.AnyABSActive;
    public bool IsAirborne => m_wheelController != null && m_wheelController.IsAirborne;
    public int GroundedWheelCount => m_wheelController != null ? m_wheelController.GroundedWheelCount : 0;
    public int ActiveGear => m_mission.ActiveGear;
    // 現在のATまたはMTから計算した後退最高速度
    public float ReverseMaximumSpeedKph => m_reverseMaximumSpeedKph;
    // 現在リバース速度リミッターが作動しているか示す状態
    public bool ReverseLimiterActive => m_reverseLimiterActive;
    // 既存デバッグ画面へ旧WheelController2024の一覧を渡すための互換プロパティ
    public WheelController2024[] WheelComtrollers => GetComponentsInChildren<WheelController2024>(true);

    // ステアリング入力のプロパティ
    public float Steering
    {
        get => m_steerInput;
        set
        {
            m_steerInput = Mathf.Clamp(value, -1f, 1f);
            if (m_steering != null)
            {
                m_steering.SmoothInput = false;
            }
        }
    }

    // 入力機器に応じて操舵補間を切り替える関数
    public void SetSteeringInput(float value, bool smoothInput)
    {
        m_steerInput = Mathf.Clamp(value, -1f, 1f);
        if (m_steering != null)
        {
            m_steering.SmoothInput = smoothInput;
        }
    }

    // アクセル入力のプロパティ
    public float Accel { get => m_accelInput; set => m_accelInput = value; }
    // ブレーキ入力のプロパティ
    public float Brake { get => m_brakeInput; set => m_brakeInput = value; }
    // クラッチ入力のプロパティ
    public float Clutch { get => m_clutchInput; // 直接入力するデバッグ処理からもMTのクラッチペダルを無効にする
        set => m_clutchInput = value; }
    // 車両の固定状態のプロパティ
    public bool IsPullUp { get => m_IsPullUp; }
    // ホーン入力のプロパティ
    public float IsHorn { get => m_HornInput; set => m_HornInput = value; }
    // 駆動系の参照を外部から取得するためのプロパティ
    public Car_Engine passEngine => m_engine;
    public Clutch passClutch => m_clutch;
    public Transmission passTransmission => m_mission;
    public Differential passDifferential => m_differential;

    // ゲームの開始時に呼ばれる初期化処理
    void Awake()
    {
        // 旧シーンに保存された強すぎる衝突減速値をTGS向けの安全範囲へ補正する
        ApplyCollisionTuningLimits();
        // 未設定の駆動系コンポーネントを車両階層から取得し、存在しない場合だけ車両本体へ追加する
        ResolveVehicleParts();
        // ゲーム状態の初期化がStartより先でも、車体を安全に固定できるようAwakeで取得する
        TryGetComponent(out m_rigidbody);
        // WheelControllerへ自動取得したデフ、ブレーキ、ステアリングを渡す
        m_wheelController.ConfigureVehicleParts(m_differential, m_brake, m_steering);
        // 旧タイヤ物理との二重サスペンション計算を止めてWheelCollider側へ一本化する
        DisableLegacyWheelSimulation();
        // 衝突検出モードをContinuousDynamicにすることで、低速での衝突時に物理演算が貫通しないようにする
        if (m_rigidbody != null)
        {
            m_rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_rigidbody.maxDepenetrationVelocity = m_maxDepenetrationVelocity;
            m_rigidbody.solverIterations = Mathf.Max(m_rigidbody.solverIterations, 12);
            m_rigidbody.solverVelocityIterations = Mathf.Max(m_rigidbody.solverVelocityIterations, 4);
            ConfigureBodyCollisionMaterial();
        }
    }

    // 旧WheelController2024の物理更新を停止して二重計算を防ぐ関数
    void DisableLegacyWheelSimulation()
    {
        WheelController2024[] legacyWheels = GetComponentsInChildren<WheelController2024>(true);
        for (int index = 0; index < legacyWheels.Length; index++)
        {
            legacyWheels[index].enabled = false;
        }
    }

    // ゲームの開始時に呼ばれる初期化処理
    void Start()
    {
        m_mission.Initialize();
        m_engine.Initialize();
        // 初回の入力より先に車速から変速先RPMを計算できるようにする
        m_mission.ConfigureShiftSafety(m_wheelController.WheelRadius, m_engine.OverRevRPM);
    }

    // 車両階層内の駆動系を自動取得し、見つからないコンポーネントだけ車両本体へ追加する関数
    void ResolveVehicleParts()
    {
        if (m_engine == null)
        {
            m_engine = GetComponentInChildren<Car_Engine>(true);
        }

        if (m_clutch == null)
        {
            m_clutch = GetComponentInChildren<Clutch>(true);
        }

        if (m_mission == null)
        {
            m_mission = GetComponentInChildren<Transmission>(true);
        }

        if (m_differential == null)
        {
            m_differential = GetComponentInChildren<Differential>(true);
        }

        if (m_brake == null)
        {
            m_brake = GetComponentInChildren<Brake>(true);
        }

        if (m_steering == null)
        {
            m_steering = GetComponentInChildren<Steering>(true);
        }

        if (m_wheelController == null)
        {
            m_wheelController = GetComponentInChildren<WheelController2026>(true);
        }

        if (m_engine == null)
        {
            m_engine = gameObject.AddComponent<Car_Engine>();
        }

        if (m_clutch == null)
        {
            m_clutch = gameObject.AddComponent<Clutch>();
        }

        if (m_mission == null)
        {
            m_mission = gameObject.AddComponent<Transmission>();
        }

        if (m_differential == null)
        {
            m_differential = gameObject.AddComponent<Differential>();
        }

        if (m_brake == null)
        {
            m_brake = gameObject.AddComponent<Brake>();
        }

        if (m_steering == null)
        {
            m_steering = gameObject.AddComponent<Steering>();
        }

        if (m_wheelController == null)
        {
            m_wheelController = gameObject.AddComponent<WheelController2026>();
        }
    }

    // 物理演算の更新ごとに呼ばれる処理
    void FixedUpdate()
    {
        // Trackの実クラッチを自動復帰や駆動計算より先に反映する
        UpdateManualLeverClutch();
        // サイドブレーキ入力を先に読み取り、発進補助との競合を防ぐ
        m_brake.UpdateHandbrake(!m_IsPullUp);
        // 衝突後の速度補正で相対速度を誤用しないよう物理更新直前の車体速度を保存する
        m_velocityBeforePhysicsStep = m_rigidbody.linearVelocity;
        m_hasVelocityBeforePhysicsStep = true;
        float driveInputRate = m_accelInput > m_smoothedDriveInput ? m_driveInputRiseRate : m_driveInputFallRate;
        m_smoothedDriveInput = Mathf.MoveTowards(m_smoothedDriveInput, m_accelInput, driveInputRate * Time.fixedDeltaTime);
        // 復帰後の実際の発進を計測し、駆動系の初期化で再発進を準備する
        if (!m_brake.HandbrakeActive && !ManualLeverActive)
        {
            ApplyRecoveryRestartAssist();
            PrepareDriveAwayFromAnyStop();
        }

        // 完全停止してブレーキを離した瞬間に、制動中の負スリップを次の発進へ持ち越さない。
        if (!ManualLeverActive && m_previousBrakeInput > 0.01f && m_brakeInput <= 0.01f && m_KPH < 0.5f)
        {
            PrepareStationaryRestart();
        }

        // ギア切り替え
        // 実タイヤ半径と現在のレブ上限を渡し設定変更後も安全な変速判定を行う
        m_mission.ConfigureShiftSafety(m_wheelController.WheelRadius, m_engine.OverRevRPM);
        m_mission.TransmissionUpdate(m_engine.RPM, m_KPH, m_accelInput);
        // MTの高回転ダウンシフト後に選択段の最高速度まで車体を減速する
        ApplyManualDownshiftSpeedLimit();
        m_differential.SetFinalDriveRatio(m_mission.FinalDriveRatio);
        UpdateReverseSpeedLimiter();
        // 駆動トルク(トランスミッション) = エンジントルク * 現在のギア比
        float driveTorque = m_clutch.ClutchTorque * m_mission.CurrentGearRatio * m_mission.DriveTorqueRatio;
        if (m_reverseLimiterActive)
        {
            driveTorque = 0f;
        }

        // 実クラッチの接続量に合わせ通常MTと同じ発進補助を戻す
        float clutchAssistRatio = ManualDriveAssistRatio;
        if (clutchAssistRatio > 0f && !m_IsPullUp && m_brakeInput <= 0.01f && m_accelInput > 0.03f && m_mission.CurrentGearRatio != 0f && m_KPH < m_launchAssistMaximumSpeedKph)
        {
            float launchRatio = 1f - Mathf.Clamp01(m_KPH / m_launchAssistMaximumSpeedKph);
            float launchTorque = m_launchAssistTorque * m_smoothedDriveInput * launchRatio * clutchAssistRatio;
            driveTorque = Mathf.Sign(m_mission.CurrentGearRatio) * Mathf.Max(Mathf.Abs(driveTorque), launchTorque);
        }

        // 0km/hから100km/hまでの加速をゲーム向けに強化する
        if (clutchAssistRatio > 0f && !m_IsPullUp && m_brakeInput <= 0.01f && m_accelInput > 0.03f && m_mission.CurrentGearRatio > 0f)
        {
            float accelerationFade = Mathf.InverseLerp(80f, m_arcadeAccelerationFadeEndKph, m_KPH);
            float accelerationMultiplier = Mathf.Lerp(1f, m_arcadeAccelerationTorqueMultiplier, m_smoothedDriveInput);
            // 踏み戻した時だけ通常MTと同じ倍率へ戻し半クラッチ中は補助を弱める
            driveTorque *= Mathf.Lerp(1f, Mathf.Lerp(accelerationMultiplier, 1f, accelerationFade), clutchAssistRatio);
        }

        // MTの高い段では低回転の駆動不足を発進補助や加速倍率で打ち消さない
        driveTorque *= m_mission.LowRPMDriveRatio;
        // ATの1速でクリープトルクをアクセル駆動へ滑らかに受け渡す処理
        bool canApplyCreep = m_mission.Type == Transmission.TransmissionType.Automatic && m_mission.ActiveGear == 1 && !m_IsPullUp && m_brakeInput <= 0.01f && !m_brake.HandbrakeActive;
        m_atCreepActive = false;
        m_atCreepDriveTorque = 0f;
        if (canApplyCreep)
        {
            float signedKPH = Vector3.Dot(m_rigidbody.linearVelocity, transform.forward) * 3.6f;
            float creepAmount = Mathf.Clamp01((m_atCreepTargetSpeed - signedKPH) / m_atCreepTargetSpeed);
            m_atCreepActive = creepAmount > 0.001f;
            // 180Nmを1速ギア比へ通し、デフへ渡す入力トルクを求める
            m_atCreepDriveTorque = m_atCreepTorque * Mathf.Abs(m_mission.CurrentGearRatio) * creepAmount;
            driveTorque = Mathf.Max(driveTorque, m_atCreepDriveTorque);
            // クラッチ同期で回転数が下がってもクリープ中は1200RPMを維持する
            m_engine.MaintainMinimumRPM(m_atCreepEngineRPM);
        }

        // アクセルを離した時だけギアとクラッチを通したエンジンブレーキを加える
        driveTorque += CalculateEngineBrakingTorque();
        // 衝突角が正面に近いほど壁へ向かう駆動力を強く弱める
        driveTorque *= GetSideImpactDriveTorqueRatio();
        // ESCより先に現在の入力と速度から実際に使う舵角を更新する
        // 駆動配分と操舵とタイヤへ同じ時刻のモード補間値を渡す
        m_differential.UpdateDriveDistribution(Time.fixedDeltaTime);
        m_steering.InputAngle = m_steerInput;
        m_steering.VehicleSpeedKph = m_rigidbody.linearVelocity.magnitude * 3.6f;
        // デフと同じ切替割合を使いTrackだけ操舵の切り込みを穏やかにする
        m_steering.TrackHandlingBlend = m_differential != null ? m_differential.TrackHandlingBlend : 0f;
        m_steering.SportHandlingBlend = m_differential != null ? m_differential.SportHandlingBlend : 0f;
        m_steering.UpdateSteering(Time.fixedDeltaTime);
        // ESCの介入を計算し、必要に応じてトルクを制限する
        UpdateESC();
        // アクセル中の通常旋回では介入を弱め、大きく姿勢を崩した時は最大介入へ戻す
        m_escAppliedIntervention = GetAppliedESCIntervention() * Mathf.Clamp01(m_drivingAssistStrength);
        m_escActive = m_escAppliedIntervention > 0.01f;
        // 同じ時刻の滑りを使い、コーナー補助とESCの強い方だけで駆動力を制限する
        float cornerTorqueRatio = GetCornerAssistTorqueRatio();
        float controlTorqueRatio = CombineDriveLimits(cornerTorqueRatio, m_escAppliedIntervention, m_escMaximumTorqueReduction);
        // 展示用の加速上限を車輪トルクへ換算し、タイヤの摩擦を通さない加速をなくす
        driveTorque = LimitAssistedDriveTorque(driveTorque);
        // プロペラシャフトの速度を計算
        float shaftVelocity = 0f;
        // ギアの入力側の値を計算する
        float ClutchInputSide = shaftVelocity * m_mission.CurrentGearRatio;
        // 後輪制動中はエンジン側から駆動輪を押さず、解除後に通常駆動へ戻す
        if (m_brake.HandbrakeActive)
        {
            driveTorque = 0f;
        }

        m_differential.InputTorque = driveTorque * controlTorqueRatio;
        // ブレーキに入力を保存
        m_brake.BrakeInput = m_brakeInput;
        // ESCの介入をタイヤ側へ渡す
        m_wheelController.SetStabilityControl(1f, m_escBrakeWheelIndex, m_escMaximumWheelBrakeTorque * m_escAppliedIntervention);
        // 各ホイールの処理
        m_wheelController.WheelUpdate();
        // 実際に接地している時だけ後輪の追従と滑り回復を補助する
        ApplyModeYawAssist();
        LimitReverseSpeed();
        UpdateIntroGrounding();
        ApplySideImpactStability();
        // 壁接触後の逆ハンドルを妨げる回転だけを短時間減衰する
        ApplyWallSpinRecovery();
        // サイドブレーキで止めた車を壁離脱補助が勝手に動かさないようにする
        if (!m_brake.HandbrakeActive && !ManualLeverActive)
        {
            // 停止復帰と壁沿い補助が同じ物理更新で二重に加速しないよう上限を共有する
            m_wallAssistRemainingDeltaSpeed = Mathf.Max(0f, m_wallAssistAcceleration) * Time.fixedDeltaTime;
            ApplyWallStuckRecovery();
            ApplyWallFollowVelocitySupport();
        }

        PreventRoadFallThrough();
        shaftVelocity = m_wheelController.ShaftAngularVelocity;
        ClutchInputSide = shaftVelocity * m_mission.CurrentGearRatio;
        // ニュートラルだったとき
        if (m_mission.CurrentGearRatio == 0f)
        {
            // クラッチの出力
            ClutchInputSide = m_engine.RPM * CarPhysics.RPM2Rad;
        }

        // MTだけ手動クラッチ入力を渡し、ATクラッチの自動接続率を毎フレーム上書きしない
        // 手動経路でもペダルを踏むほど接続を切り逆転を防ぐ
        if (!m_clutch.AutoClutch && !ManualLeverActive)
        {
            m_clutch.ClutchInput = 1f - Mathf.Clamp01(m_clutchInput);
        }

        m_clutch.GearChanging = m_mission.IsGearChanging;
        // クラッチトルクの更新
        // 待機中や制動中には発進補助を使わずMT1速の踏み出しだけを助ける
        bool manualLaunchAllowed = m_mission.Type == Transmission.TransmissionType.Manual && m_mission.ActiveGear == 1 && !m_IsPullUp && m_brakeInput <= 0.01f && !m_brake.HandbrakeActive && !m_mission.IsGearChanging;
        m_clutch.ConfigureManualLaunch(manualLaunchAllowed, m_accelInput, m_engine.IdleAngularVelocity);
        m_clutch.DrivetrainUpdate(ClutchInputSide, m_engine.AngularVelocity, m_engine.EngineTorque, m_mission.CurrentGearRatio, m_engine.Inertia);
        // エンジンの回転数の更新
        bool atForwardSpeedLimiter = m_mission.Type == Transmission.TransmissionType.Automatic && m_mission.ActiveGear > 0 && m_KPH >= m_atMaximumSpeedKph;
        // MTのアップ時だけ駆動を抜きダウン時の回転合わせを燃料カットで妨げない
        bool manualShiftInjectionCut = m_mission.Type == Transmission.TransmissionType.Manual && m_mission.IsGearChanging && m_mission.IsShiftUp && !ManualLeverActive;
        m_engine.InjectionCut = manualShiftInjectionCut || atForwardSpeedLimiter || m_reverseLimiterActive;
        // 待機中も実際の燃料カットで回転制限し発進後は通常上限へ戻す
        m_engine.TemporaryRevLimitRPM = m_IsPullUp ? CountdownMaximumRPM : 0f;
        // ATダウンシフト時だけ新しいギアの駆動軸回転へアクセルを補って合わせる
        // 自動クラッチ付きMTも前進段のダウン時は車輪側に回転を合わせる
        bool manualBlip = m_mission.Type == Transmission.TransmissionType.Manual && m_clutch.AutoClutch && m_mission.IsGearChanging && m_mission.IsShiftDown && m_mission.ActiveGear > 0;
        bool automaticBlip = (m_mission.AutomaticBlipActive || manualBlip) && !m_IsPullUp;
        float blipRPM = automaticBlip ? Mathf.Abs(ClutchInputSide) * CarPhysics.Rad2RPM : 0f;
        m_engine.EngineUpdate(m_accelInput, m_clutch.ClutchTorque, blipRPM);
        // クラッチ接続率に合わせて駆動軸回転をエンジンへ段差なく同期する処理
        // 変速中も残っているクラッチ接続分だけ回転を合わせ空ぶかしや急落を抑える
        // 実クラッチでは回転差トルクで合わせるため回転数の直接同期を重ねない
        bool canSynchronizeShift = !ManualLeverActive && !m_clutch.ManualLaunchSlipActive && (!m_mission.IsGearChanging || m_clutch.AutoClutch);
        float drivetrainCoupling = m_mission.CurrentGearRatio != 0f && canSynchronizeShift && !m_IsPullUp ? m_clutch.Engagement : 0f;
        m_engine.SynchronizeToDrivetrain(ClutchInputSide, drivetrainCoupling);
        // カウント中と発進直後だけ専用の回転範囲を適用する
        // 実クラッチ操作中は発進演出で回転合わせを上書きしない
        if (ManualLeverActive && !m_IsPullUp)
        {
            m_launchRPMBlendRemaining = 0f;
        }
        else
        {
            UpdateLaunchRPM();
        }

        // 車速の計算
        m_KPH = m_rigidbody.linearVelocity.magnitude * 3600f / 1000f;
        // 車体のピッチフィールを計算
        ApplyBodyPitchFeel();
        // 坂道などで実際に後退している場合へ影響しないよう、1 km/h未満だけを補正する。
        PreventLowSpeedWrongDirection();
        m_previousBrakeInput = m_brakeInput;
    }

    // 進行方向と逆向きへ作用するエンジンブレーキトルクを計算する関数
    float CalculateEngineBrakingTorque()
    {
        // 実クラッチの負トルクでエンジン抵抗を伝えるため別の制動を二重に足さない
        if (ManualLeverActive)
        {
            return 0f;
        }

        if (m_engine == null || m_clutch == null || m_mission == null || m_rigidbody == null)
        {
            return 0f;
        }

        if (m_IsPullUp || m_brakeInput > 0.01f || m_mission.CurrentGearRatio == 0f || m_clutch.Engagement <= 0f)
        {
            m_appliedCoastTorque = 0f;
            return 0f;
        }

        // 車体前方を基準にした進行速度
        float signedSpeedKph = Vector3.Dot(m_rigidbody.linearVelocity, transform.forward) * 3.6f;
        if (Mathf.Abs(signedSpeedKph) < m_engineBrakingMinimumSpeedKph)
        {
            m_appliedCoastTorque = 0f;
            return 0f;
        }

        // ATの低速域はトルクコンバーターのクリープを優先して停止直前の引っ掛かりを防ぐ
        bool atCreepRange = m_mission.Type == Transmission.TransmissionType.Automatic && m_mission.ActiveGear == 1 && Mathf.Abs(signedSpeedKph) <= m_atCreepTargetSpeed;
        if (atCreepRange)
        {
            m_appliedCoastTorque = 0f;
            return 0f;
        }

        // アクセル開度に応じてエンジンブレーキを滑らかに解除する割合
        float coastAmount = 1f - Mathf.Clamp01(m_smoothedDriveInput);
        float transmissionTorque = m_engine.EngineBrakingTorque * Mathf.Abs(m_mission.CurrentGearRatio) * m_clutch.Engagement;
        float maximumTorque = m_rigidbody.mass * m_coastMaximumDeceleration * m_wheelController.WheelRadius / Mathf.Max(0.01f, m_mission.FinalDriveRatio);
        float targetTorque = Mathf.Min(transmissionTorque * m_engineBrakingStrength * coastAmount, maximumTorque);
        // 舵角に応じて少しずつ緩め、直進と後退のエンジンブレーキは元の強さを保つ
        float cornerAmount = m_steering != null && signedSpeedKph > 0f && m_mission.ActiveGear > 0 ? Mathf.InverseLerp(0f, m_cornerAssistStartSteerAngle, Mathf.Abs(m_steering.CurrentCenterAngle)) : 0f;
        float modeBlend = m_differential != null ? m_differential.TrackHandlingBlend : 0f;
        float coastScale = Mathf.Lerp(m_cornerCoastTorqueScale, m_trackCornerCoastScale, modeBlend);
        targetTorque *= Mathf.Lerp(1f, Mathf.Clamp01(coastScale), cornerAmount);
        // MTのTrackで踏み直した時は惰性制動を駆動トルクへ重ね続けない
        if (m_mission.Type == Transmission.TransmissionType.Manual && modeBlend > 0f && m_smoothedDriveInput > 0f && targetTorque < m_appliedCoastTorque)
        {
            // 通常モードとの切替中も解除時間を連続的に変える
            float releaseSeconds = Mathf.Lerp(m_coastBrakeResponseTime, m_trackManualCoastReleaseSeconds, modeBlend);
            float released = ReleaseManualCoastTorque(m_appliedCoastTorque, targetTorque, m_smoothedDriveInput, releaseSeconds, Time.fixedDeltaTime);
            float normalResponse = 1f - Mathf.Exp(-Time.fixedDeltaTime / Mathf.Max(0.01f, m_coastBrakeResponseTime));
            float normalTorque = Mathf.Lerp(m_appliedCoastTorque, targetTorque, normalResponse);
            m_appliedCoastTorque = Mathf.Lerp(normalTorque, released, modeBlend);
            return -Mathf.Sign(signedSpeedKph) * m_appliedCoastTorque;
        }

        float response = 1f - Mathf.Exp(-Time.fixedDeltaTime / Mathf.Max(0.01f, m_coastBrakeResponseTime));
        m_appliedCoastTorque = Mathf.Lerp(m_appliedCoastTorque, targetTorque, response);
        return -Mathf.Sign(signedSpeedKph) * m_appliedCoastTorque;
    }

    // 道路を貫通して落下し続ける場合に最後の接地点へ戻す関数
    void PreventRoadFallThrough()
    {
        if (m_rigidbody == null || m_wheelController == null)
        {
            return;
        }

        // 衝突で車体が大きく傾いて接地を失った場合だけ直前の正常姿勢へ戻す
        float tiltAngle = Vector3.Angle(transform.up, Vector3.up);
        if (m_hasSafePose && tiltAngle >= m_rolloverRecoveryAngle && m_wheelController.GroundedWheelCount < 3)
        {
            m_rigidbody.position = m_lastSafePosition + Vector3.up * 0.15f;
            m_rigidbody.rotation = m_lastSafeRotation;
            ResetAfterCourseRecovery();
            return;
        }

        // 3輪以上が接地している安定状態を復帰位置として保存する
        if (m_wheelController.GroundedWheelCount >= 3 && Vector3.Dot(m_rigidbody.linearVelocity, Physics.gravity.normalized) < 2f)
        {
            m_lastSafePosition = m_rigidbody.position;
            m_lastSafeRotation = m_rigidbody.rotation;
            m_hasSafePose = true;
            return;
        }

        // 短いジャンプや縁石通過では復帰させない
        if (!m_hasSafePose || !m_wheelController.IsAirborne || m_wheelController.AirborneTime < m_fallProtectionDelay)
        {
            return;
        }

        if (m_rigidbody.position.y >= m_lastSafePosition.y - m_fallProtectionHeight)
        {
            return;
        }

        // 路面直下へ抜けた車体だけを直前の接地高さへ戻す
        // 前進速度を維持して上下方向の落下速度だけを止める
        Vector3 correctedVelocity = m_rigidbody.linearVelocity;
        correctedVelocity.y = Mathf.Max(0f, correctedVelocity.y);
        m_rigidbody.linearVelocity = correctedVelocity;
        m_rigidbody.angularVelocity = Vector3.Project(m_rigidbody.angularVelocity, transform.up);
        ResetBodyPitchState();
    }

    // 実際の加減速に応じたノーズの上下動を補助する関数
    void ApplyBodyPitchFeel()
    {
        // 空中と開始待機中は演出用トルクを加えず、着地時へ古い加速度を持ち越さない
        if (m_IsPullUp || GroundedWheelCount < 2)
        {
            ResetBodyPitchState();
            return;
        }

        if (!m_pitchStateInitialized)
        {
            ResetBodyPitchState();
            return;
        }

        // 速度ベクトルの変化から前後加速度を求め旋回だけでノーズを押し下げない
        float rawAcceleration = CalculatePitchAcceleration(m_rigidbody.linearVelocity, m_previousPitchVelocity, transform.forward, Time.fixedDeltaTime);
        m_previousPitchVelocity = m_rigidbody.linearVelocity;
        if (Mathf.Abs(rawAcceleration) > m_maxLongitudinalAcceleration * 2f)
        {
            m_filteredLongitudinalAcceleration = 0f;
            return;
        }

        // 加速度の値を制限して、フィルタリングする
        rawAcceleration = Mathf.Clamp(rawAcceleration, -m_maxLongitudinalAcceleration, m_maxLongitudinalAcceleration);
        float filterAmount = 1f - Mathf.Exp(-m_accelerationFilterSpeed * Time.fixedDeltaTime);
        m_filteredLongitudinalAcceleration = Mathf.Lerp(m_filteredLongitudinalAcceleration, rawAcceleration, filterAmount);
        // 加減速に応じた小さなピッチトルクを加えて自然な荷重移動を補助する
        float pitchAcceleration = -m_filteredLongitudinalAcceleration * m_pitchResponse * Mathf.Clamp01(m_additionalPitchScale);
        if (pitchAcceleration < 0f)
        {
            pitchAcceleration *= m_accelerationPitchBoost;
        }

        float pitchLimit = pitchAcceleration < 0f ? m_maxNoseUpAngularAcceleration : m_maxPitchAngularAcceleration;
        m_rigidbody.AddTorque(transform.right * Mathf.Clamp(pitchAcceleration, -pitchLimit, pitchLimit), ForceMode.Acceleration);
        // サスペンションの自然な荷重移動を残しながら異常なピッチ回転だけを減衰する
        float localPitchVelocity = Vector3.Dot(m_rigidbody.angularVelocity, transform.right);
        const float maximumNaturalPitchSpeed = 0.18f;
        if (Mathf.Abs(localPitchVelocity) <= maximumNaturalPitchSpeed)
        {
            return;
        }

        float targetPitchVelocity = Mathf.Sign(localPitchVelocity) * maximumNaturalPitchSpeed;
        float correctedPitchVelocity = Mathf.MoveTowards(localPitchVelocity, targetPitchVelocity, m_pitchDamping * Time.fixedDeltaTime);
        m_rigidbody.angularVelocity += transform.right * (correctedPitchVelocity - localPitchVelocity);
    }

    // 世界座標で求めた加速度のうち車体の前後方向だけを取り出す関数
    static float CalculatePitchAcceleration(Vector3 velocity, Vector3 previousVelocity, Vector3 forward, float deltaTime)
    {
        return Vector3.Dot(velocity - previousVelocity, forward) / Mathf.Max(deltaTime, 0.001f);
    }

    // 発進や復帰直前の速度を基準にして古い加速度を持ち越さない関数
    void ResetBodyPitchState()
    {
        m_previousPitchVelocity = m_rigidbody != null ? m_rigidbody.linearVelocity : Vector3.zero;
        m_filteredLongitudinalAcceleration = 0f;
        m_pitchStateInitialized = true;
    }

    // 停止状態からの発進準備を行う
    void PrepareDriveAwayFromAnyStop()
    {
        // カウントダウンから引き継いだ回転数を通常の停止復帰でアイドルへ戻さない
        if (m_launchRPMBlendRemaining > 0f)
        {
            return;
        }

        // ATが停止中にNへ残った場合はアクセル入力で1速へ戻して発進を可能にする
        if (!m_IsPullUp && m_mission.Type == Transmission.TransmissionType.Automatic && m_mission.ActiveGear == 0 && !m_mission.AutomaticNeutralSelected && m_accelInput > 0.03f && m_brakeInput <= 0.01f)
        {
            m_mission.PrepareForwardStart();
        }

        // 停止状態からの発進準備を行う条件を判定する
        bool canDrive = !m_IsPullUp && m_mission.ActiveGear != 0 && m_accelInput > 0.03f && m_brakeInput <= 0.01f;
        // 一度走り出した後は再び監視を有効にする。
        if (!canDrive || m_KPH > 1f)
        {
            m_driveAwayPrepared = false;
            return;
        }

        // 発進準備が完了している場合は、再度準備を行わない。
        if (m_driveAwayPrepared || m_KPH > 0.5f)
        {
            return;
        }

        float forwardSpeed = Vector3.Dot(m_rigidbody.linearVelocity, transform.forward);
        float gearDirection = Mathf.Sign(m_mission.ActiveGear);
        m_wheelController.PrepareDriveAway(forwardSpeed * gearDirection);
        // 停止原因に関係なく、古いクラッチ回転差とエンジン負荷を次の発進へ持ち越さない。
        m_clutch.ResetDynamics();
        m_engine.ResetToIdle(m_accelInput);
        // 最初の物理フレームから発進補助トルクを使える入力値へ引き上げる
        m_smoothedDriveInput = Mathf.Max(m_smoothedDriveInput, Mathf.Min(m_accelInput, 0.35f));
        // 速度の直接書き換えはせず、同じ物理更新の発進トルクでタイヤを駆動する
        m_rigidbody.WakeUp();
        m_driveAwayPrepared = true;
    }

    // 復帰後に実際の駆動で発進できた時刻を記録する関数
    void ApplyRecoveryRestartAssist()
    {
        // 入力がない間やニュートラルでは発進成功として記録しない
        if (!m_waitingForRecoveryRestart || m_accelInput <= 0.01f || m_mission.ActiveGear == 0)
        {
            return;
        }

        // ギアの方向に応じて、前後方向の速度を計算する
        float gearDirection = Mathf.Sign(m_mission.ActiveGear);
        float forwardSpeed = Vector3.Dot(m_rigidbody.linearVelocity, transform.forward);
        float speedInGearDirection = forwardSpeed * gearDirection;
        // 速度を作り出さず、選択ギア方向へ実際に動いた場合だけ発進済みにする
        const float restartSpeed = 1f / 3.6f;
        if (speedInGearDirection < restartSpeed)
        {
            return;
        }

        m_lastRecoveryResponseSeconds = Time.realtimeSinceStartup - m_recoveryRequestedAt;
        m_waitingForRecoveryRestart = false;
    }

    // 完全停止してブレーキを離した瞬間に、制動中の負スリップを次の発進へ持ち越さない。
    void PrepareStationaryRestart()
    {
        m_wheelController.ResetDynamics();
        m_clutch.ResetDynamics();
        m_engine.ResetToIdle(0f);
        m_smoothedDriveInput = 0f;
        m_driveAwayPrepared = false;
    }

    // 停止直前のタイヤ計算誤差だけで、選択中のギアと逆方向へ転がるのを防ぐ。
    void PreventLowSpeedWrongDirection()
    {
        // ギアがニュートラルか、車速が1 km/h以上の場合は処理しない
        if (m_mission.ActiveGear == 0 || m_KPH >= 1f)
        {
            return;
        }

        // ギアと逆方向に微小な前後速度がある場合のみ処理する
        float forwardSpeed = Vector3.Dot(m_rigidbody.linearVelocity, transform.forward);
        bool movingAgainstGear = (m_mission.ActiveGear > 0 && forwardSpeed < 0f) || (m_mission.ActiveGear < 0 && forwardSpeed > 0f);
        // ギアと逆方向に動いていない場合は処理しない
        if (!movingAgainstGear)
        {
            return;
        }

        // 横方向と上下方向の動きは残し、ギアと反対向きの微小な前後速度だけを除去する。
        m_rigidbody.linearVelocity -= transform.forward * forwardSpeed;
        m_KPH = m_rigidbody.linearVelocity.magnitude * 3.6f;
    }

    // コース復帰時に車体と駆動系の運動状態をまとめて初期化する。
    public void ResetAfterCourseRecovery()
    {
        // コース復帰後にスタート時の回転保持を再開しない
        m_countdownLaunchActive = false;
        m_launchRPMBlendRemaining = 0f;
        // 復帰前に押していたサイドブレーキの補間値を持ち越さない
        if (m_brake != null)
        {
            m_brake.ResetHandbrake();
        }

        // Rigidbodyを取得する
        if (m_rigidbody == null)
        {
            TryGetComponent(out m_rigidbody);
        }

        // 車体の運動状態を初期化する
        m_rigidbody.linearVelocity = Vector3.zero;
        m_rigidbody.angularVelocity = Vector3.zero;
        ResetBodyPitchState();
        m_KPH = 0f;
        m_previousBrakeInput = m_brakeInput;
        m_wheelController.ResetDynamics();
        // 駆動系の状態を初期化する
        m_clutch.ResetDynamics();
        m_mission.ResetDynamics();
        m_engine.ResetToIdle(0f);
        m_smoothedDriveInput = 0f;
        m_waitingForRecoveryRestart = true;
        m_recoveryRequestedAt = Time.realtimeSinceStartup;
        m_lastRecoveryResponseSeconds = 0f;
        m_driveAwayPrepared = false;
        m_wheelController.CorrectGroundPenetrationImmediately();
        m_rigidbody.WakeUp();
    }

    // 道路下の復帰Triggerから最後に保存した安全な路面位置へ車体を戻す関数
    public bool RecoverToLastSafePose()
    {
        if (!m_hasSafePose || m_rigidbody == null)
        {
            return false;
        }

        // 最後に4輪が安定していた位置へ少し持ち上げて戻す
        m_rigidbody.position = m_lastSafePosition + Vector3.up * 0.15f;
        m_rigidbody.rotation = m_lastSafeRotation;
        ResetAfterCourseRecovery();
        return true;
    }

    // トランスミッションの種類を切り替える
    public void ChangeMissionType()
    {
        if (m_mission.Type == Transmission.TransmissionType.Manual)
        {
            m_mission.Type = Transmission.TransmissionType.Automatic;
        }
        else if (m_mission.Type == Transmission.TransmissionType.Automatic)
        {
            m_mission.Type = Transmission.TransmissionType.Manual;
        }
    }

    // パドル操作でATとクラッチ付き自動MTモードを切り替える関数
    public void SetPaddleTransmissionType(Transmission.TransmissionType transmissionType)
    {
        SelectPaddleClutch();
        m_mission.Type = transmissionType;
        m_clutch.AutoClutch = true;
        if (transmissionType == Transmission.TransmissionType.Automatic && !m_IsPullUp && m_mission.ActiveGear == 0)
        {
            m_mission.PrepareForwardStart();
        }
    }

    // 車両の固定状態を変更する
    public void PullUp(bool _active)
    {
        // Rigidbodyを取得する
        if (m_rigidbody == null)
            TryGetComponent(out m_rigidbody);
        if (m_rigidbody == null)
        {
            AppLog.LogError("VehicleController: Rigidbodyがないため車両の固定状態を変更できません。", this);
            return;
        }

        // 車両の固定状態を変更する
        RigidbodyConstraints constraints;
        if (_active)
        {
            // イントロからレースへ同じ固定状態を再指定しても開始済みカウントの回転引き継ぎを消さない
            if (!m_IsPullUp)
            {
                m_countdownLaunchActive = false;
                m_launchRPMBlendRemaining = 0f;
            }

            constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezePositionZ;
            m_rigidbody.linearVelocity = Vector3.zero;
            m_rigidbody.angularVelocity = Vector3.zero;
            ResetBodyPitchState();
            m_wheelController.ResetDynamics();
            m_smoothedDriveInput = 0f;
        }
        else
        {
            constraints = RigidbodyConstraints.None;
            // カウント中の実回転数を発進へ引き継いでから車体固定を解除する
            ReleaseCountdownLaunch();
            // ATのカウントダウン終了時にNから1速へ切り替えてクリープと発進を有効にする
            if (m_mission.Type == Transmission.TransmissionType.Automatic && m_mission.ActiveGear == 0)
            {
                m_mission.PrepareForwardStart();
            }

            if (m_meterUIManager != null)
            {
                m_meterUIManager.StartTimer();
            }
        }

        // 車両の固定状態を更新する
        m_IsPullUp = _active;
        m_mission.IsPullUp = _active;
        m_clutch.IsPullUp = _active;
        m_rigidbody.constraints = constraints;
        Physics.SyncTransforms();
        m_clutch.Oscillation = 1.0f;
    }

    // イントロ中に車両の前後左右と姿勢を固定しながらサスペンションを路面へ馴染ませる関数
    public void PrepareIntroGrounding()
    {
        PullUp(true);
        if (m_rigidbody == null)
        {
            return;
        }

        // 接地情報が安定するまで車体を完全固定し、開始直後の落下とタイヤの跳ねを防ぐ
        m_introGroundingFramesRemaining = m_introGroundingFixedFrames;
        m_rigidbody.constraints = RigidbodyConstraints.FreezePosition | RigidbodyConstraints.FreezeRotation;
        m_rigidbody.WakeUp();
    }

    // イントロ開始直後の数フレームだけ路面食い込みを補正してから上下サスペンションを解放する関数
    void UpdateIntroGrounding()
    {
        if (m_introGroundingFramesRemaining <= 0 || m_rigidbody == null)
        {
            return;
        }

        m_wheelController.CorrectGroundPenetrationImmediately();
        m_introGroundingFramesRemaining--;
        if (m_introGroundingFramesRemaining > 0)
        {
            return;
        }

        m_rigidbody.linearVelocity = Vector3.zero;
        m_rigidbody.angularVelocity = Vector3.zero;
        m_rigidbody.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
        Physics.SyncTransforms();
    }
}
