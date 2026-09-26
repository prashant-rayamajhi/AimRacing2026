using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

// エンジンから4輪までの駆動処理と車体安定制御を統合するクラス
public partial class VehicleController : MonoBehaviour
{
    [Space]
    [SerializeField]
    WheelController2026 m_wheelController;
    [UnityEngine.Serialization.FormerlySerializedAs("m_KPH")]
    [SerializeField, ShowInInspector]
    float m_kph;
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
    bool m_isPullUp = false;
    [SerializeField, Range(1, 10)]
    int m_introGroundingFixedFrames = 4;
    int m_introGroundingFramesRemaining;
    [UnityEngine.Serialization.FormerlySerializedAs("m_HornInput")]
    public float m_hornInput = 0f;
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
    [UnityEngine.Serialization.FormerlySerializedAs("m_arcadeAccelerationTorqueMultiplier")]
    [SerializeField, Range(1f, 8f)]
    float m_accelTorqueBoost = 2.75f;
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
    // 車両の最高速制限
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
    public float KPH => Mathf.Clamp(m_kph, 0f, float.PositiveInfinity);
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
    public void SetSteeringInput(float _value, bool _smoothInput)
    {
        m_steerInput = Mathf.Clamp(_value, -1f, 1f);
        if (m_steering != null)
        {
            m_steering.SmoothInput = _smoothInput;
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
    public bool IsPullUp { get => m_isPullUp; }
    // ホーン入力のプロパティ
    public float IsHorn { get => m_hornInput; set => m_hornInput = value; }
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
}
