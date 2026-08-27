using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

//エンジンから4輪までの駆動処理と車体安定制御を統合するクラス
public class VehicleController : MonoBehaviour
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

    //車体速度と姿勢の物理計算に使用するRigidbody
    Rigidbody m_rigidbody;

    //入力
    float m_steerInput;
    float m_accelInput = 0f;
    float m_brakeInput = 0f;
    float m_clutchInput = 1f;
    float m_previousBrakeInput = 0f;
    bool m_driveAwayPrepared;
    bool m_waitingForRecoveryRestart;
    float m_recoveryRequestedAt;
    [SerializeField, ShowInInspector]
    float m_lastRecoveryResponseSeconds;
    bool m_IsPullUp = false;
    [SerializeField, Range(1, 10)] int m_introGroundingFixedFrames = 4;
    int m_introGroundingFramesRemaining;
    public float m_HornInput = 0f;
    [SerializeField] MeterUIManager m_meterUIManager;

    //ATの低速クリープ制御
    [Header("AT Creep / ATクリープ")]
    [SerializeField, Min(0f), InspectorName("Creep Torque (Nm)"), Tooltip("ATのアクセルOFF時に車輪へ追加するクリープトルクです。")]
    float m_atCreepTorque = 180f;
    [SerializeField, Min(0.1f), InspectorName("Creep Target Speed (km/h)"), Tooltip("ATクリープが目指す最高速度です。速度が近づくとトルクを徐々に弱めます。")]
    float m_atCreepTargetSpeed = 15f;

    //停止状態からすぐに発進させるための補助トルク
    [Header("Launch Response")]
    [SerializeField, Range(0f, 500f)] float m_launchAssistTorque = 180f;
    [SerializeField, Range(1f, 20f)] float m_launchAssistMaximumSpeedKph = 8f;
    [SerializeField, Range(1f, 8f)] float m_arcadeAccelerationTorqueMultiplier = 2.75f;
    [SerializeField, Range(80f, 160f)] float m_arcadeAccelerationFadeEndKph = 120f;
    [SerializeField, Range(0f, 10f)] float m_arcadeAccelerationAssist = 0.7f;
    [SerializeField, Range(1f, 20f)] float m_driveInputRiseRate = 5f;
    [SerializeField, Range(1f, 30f)] float m_driveInputFallRate = 12f;
    float m_smoothedDriveInput;

    //GR Yarisの最高速制限
    [Header("GR Yaris Speed Limiter")]
    [SerializeField, Min(1f)] float m_atMaximumSpeedKph = 230f;

    //ボディピッチフィール
    [Header("Body Pitch Feel")]
    [SerializeField, Range(0f, 0.5f)] float m_pitchResponse = 0.08f;
    [SerializeField, Range(1f, 4f)] float m_accelerationPitchBoost = 1.25f;
    [SerializeField, Range(0f, 6f)] float m_pitchDamping = 4.5f;
    [SerializeField, Range(1f, 15f)] float m_maxLongitudinalAcceleration = 10f;
    [SerializeField, Range(1f, 20f)] float m_accelerationFilterSpeed = 8f;
    [SerializeField, Range(0.1f, 8f)] float m_maxPitchAngularAcceleration = 1.8f;
    [SerializeField, Range(0.1f, 6f)] float m_maxNoseUpAngularAcceleration = 1.1f;
    float m_previousForwardSpeed;
    float m_filteredLongitudinalAcceleration;
    bool m_pitchStateInitialized;

    //旋回中の横滑りを抑えるESC設定
    [Header("ESC")]
    [SerializeField] bool m_escEnabled = true;
    [SerializeField, Range(5f, 40f)] float m_escMinimumSpeedKph = 15f;
    [SerializeField, Range(2f, 15f)] float m_escSlipAngleThreshold = 4f;
    [SerializeField, Range(0.05f, 1f)] float m_escYawErrorThreshold = 0.25f;
    [SerializeField, Range(0f, 0.8f)] float m_escMaximumTorqueReduction = 0.60f;
    [SerializeField, Range(0f, 3000f)] float m_escMaximumWheelBrakeTorque = 1050f;
    [SerializeField, Range(2f, 3f)] float m_escWheelBase = 2.56f;
    [SerializeField, Range(6f, 15f)] float m_escMaximumLateralAcceleration = 11f;
    [SerializeField, Range(0.5f, 10f)] float m_escYawResponseRate = 3f;
    [SerializeField, Range(0.5f, 20f)] float m_escYawRecenteringRate = 8f;
    [SerializeField, ShowInInspector] bool m_escActive;
    [SerializeField, ShowInInspector] float m_escSlipAngle;
    [SerializeField, ShowInInspector] float m_escDesiredYawRate;
    float m_escIntervention;
    int m_escBrakeWheelIndex = -1;

    //道路下への落下を検出するための設定
    [Header("Road Fall Protection")]
    [SerializeField, Range(0.05f, 1f)] float m_fallProtectionDelay = 0.08f;
    [SerializeField, Range(0.1f, 2f)] float m_fallProtectionHeight = 0.25f;
    [SerializeField, Range(30f, 80f)] float m_rolloverRecoveryAngle = 50f;
    Vector3 m_lastSafePosition;
    Quaternion m_lastSafeRotation;
    bool m_hasSafePose;

    //壁の角へ衝突した際に過剰な横転を抑えるための設定
    [Header("Collision Stability")]
    [SerializeField] bool m_collisionStabilityEnabled = true;
    [SerializeField, Range(1f, 15f)] float m_sideImpactMinimumSpeed = 3f;
    [SerializeField, Range(0.1f, 4f)] float m_sideImpactStabilityDuration = 0.45f;
    [SerializeField, Range(0.05f, 1f)] float m_sideImpactMaximumRollSpeed = 0.09f;
    //衝突時に許可する車体前後方向の最大回転速度
    [SerializeField, Range(0.05f, 1f)] float m_sideImpactMaximumPitchSpeed = 0.09f;
    //衝突時に許可する車体旋回方向の最大回転速度
    [SerializeField, Range(0.2f, 3f)] float m_sideImpactMaximumYawSpeed = 0.9f;
    [SerializeField, Range(0f, 20f)] float m_sideImpactUprightStrength = 4f;
    [SerializeField, Range(0f, 20f)] float m_sideImpactRollDamping = 12f;
    //衝突後の前後揺れを収める減衰速度
    [SerializeField, Range(0f, 20f)] float m_sideImpactPitchDamping = 12f;
    [SerializeField, Range(10f, 80f)] float m_sideImpactMaximumCorrectionAngle = 30f;
    [SerializeField, Range(0f, 0.2f)] float m_sideImpactMaximumReboundSpeed = 0.03f;
    [SerializeField, Range(0f, 1f)] float m_sideImpactMaximumUpwardSpeed = 0.05f;
    [SerializeField, Range(0.1f, 1f)] float m_sideImpactDriveTorqueRatio = 0.5f;
    [SerializeField, Range(0.2f, 3f)] float m_maxDepenetrationVelocity = 0.8f;
    [SerializeField, Range(0.05f, 0.8f)] float m_sideImpactMinimumIncidence = 0.1f;
    [SerializeField, Range(0.1f, 0.9f)] float m_sideImpactDriveReductionIncidence = 0.5f;
    [SerializeField, Range(1f, 30f)] float m_sideImpactNormalResponse = 12f;
    [SerializeField, Range(0.05f, 1f)] float m_sideImpactReleaseDuration = 0.18f;

    //浅く壁を擦った時に維持する接線方向の速度割合
    [SerializeField, Range(0.5f, 1f)] float m_glancingImpactSpeedRetention = 0.995f;

    //正面に近い壁衝突で残す接線方向の速度割合
    [SerializeField, Range(0f, 1f)] float m_directImpactSpeedRetention = 0.95f;

    //浅い衝突で壁方向の速度を壁沿いへ振り替える割合
    [SerializeField, Range(0f, 0.5f)] float m_glancingImpactTangentialRedirect = 0.3f;

    //正面に近い衝突で壁方向の速度を壁沿いへ振り替える割合
    [SerializeField, Range(0f, 0.3f)] float m_directImpactTangentialRedirect = 0.18f;

    //壁接触の速度補正を始める法線方向速度
    [SerializeField, Range(0.1f, 3f)] float m_impactResponseMinimumSpeed = 0.5f;

    //浅い接触として扱う衝突速度の法線成分割合
    [SerializeField, Range(0f, 0.5f)] float m_glancingImpactIncidence = 0.12f;

    //正面衝突として扱う衝突速度の法線成分割合
    [SerializeField, Range(0.5f, 1f)] float m_directImpactIncidence = 0.85f;
    //車体が壁を擦った時にタイヤ摩擦とは別の強い減速が発生しないための摩擦値
    [SerializeField, Range(0f, 0.2f)] float m_bodyCollisionFriction = 0.02f;
    //壁接触後の姿勢安定化を継続する残り時間
    float m_sideImpactStabilityTime;
    //壁から車体側へ向く衝突面の水平法線
    Vector3 m_sideImpactNormal;
    //衝突前速度に占める壁方向速度の割合
    float m_sideImpactIncidence;
    //衝突後の速度補正に使う物理更新直前の車体速度
    Vector3 m_velocityBeforePhysicsStep;
    //物理更新直前の車体速度を取得済みか示す状態
    bool m_hasVelocityBeforePhysicsStep;
    //車体と壁の摩擦と反発を抑える実行時物理マテリアル
    PhysicsMaterial m_bodyCollisionMaterial;


    public Rigidbody Rigidbody => m_rigidbody;
    public Transmission Transmission => m_mission;
    public Car_Engine Engine => m_engine;
    public WheelController2026 WheelComtroller => m_wheelController;
    public float KPH => Mathf.Clamp(m_KPH, 0f, float.PositiveInfinity);
    public float EngineRPM => m_engine.RPM;
    public bool ESCActive => m_escActive;
    public bool ABSActive => m_wheelController != null && m_wheelController.AnyABSActive;
    public bool IsAirborne => m_wheelController != null && m_wheelController.IsAirborne;
    public int GroundedWheelCount => m_wheelController != null ? m_wheelController.GroundedWheelCount : 0;
    public int ActiveGear => m_mission.ActiveGear;
    //既存デバッグ画面へ旧WheelController2024の一覧を渡すための互換プロパティ
    public WheelController2024[] WheelComtrollers => GetComponentsInChildren<WheelController2024>(true);

    //ステアリング入力のプロパティ
    public float Steering
    {
        get => m_steerInput;
        set
        {
            m_steerInput = Mathf.Clamp(value, -1f, 1f);
            if (m_steering != null) { m_steering.SmoothInput = false; }
        }
    }

    //入力機器に応じて操舵補間を切り替える関数
    public void SetSteeringInput(float value, bool smoothInput)
    {
        m_steerInput = Mathf.Clamp(value, -1f, 1f);
        if (m_steering != null) { m_steering.SmoothInput = smoothInput; }
    }

    //アクセル入力のプロパティ
    public float Accel
    {
        get => m_accelInput;
        set => m_accelInput = value;
    }

    //ブレーキ入力のプロパティ
    public float Brake
    {
        get => m_brakeInput;
        set => m_brakeInput = value;
    }

    //クラッチ入力のプロパティ
    public float Clutch
    {
        get => m_clutchInput;
        set => m_clutchInput = value;
    }

    //車両の固定状態のプロパティ
    public bool IsPullUp
    {
        get => m_IsPullUp;
    }

    //ホーン入力のプロパティ
    public float IsHorn
    {
        get => m_HornInput;
        set => m_HornInput = value;
    }

    //駆動系の参照を外部から取得するためのプロパティ
    public Car_Engine passEngine => m_engine;
    public Clutch passClutch => m_clutch;
    public Transmission passTransmission => m_mission;
    public Differential passDifferential => m_differential;

    //ゲームの開始時に呼ばれる初期化処理
    void Awake()
    {
        //未設定の駆動系コンポーネントを車両階層から取得し、存在しない場合だけ車両本体へ追加する
        ResolveVehicleParts();

        //ゲーム状態の初期化がStartより先でも、車体を安全に固定できるようAwakeで取得する
        TryGetComponent(out m_rigidbody);

        //WheelControllerへ自動取得したデフ、ブレーキ、ステアリングを渡す
        m_wheelController.ConfigureVehicleParts(m_differential, m_brake, m_steering);

        //旧タイヤ物理との二重サスペンション計算を止めてWheelCollider側へ一本化する
        DisableLegacyWheelSimulation();

        //衝突検出モードをContinuousDynamicにすることで、低速での衝突時に物理演算が貫通しないようにする
        if (m_rigidbody != null)
        {
            m_rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_rigidbody.maxDepenetrationVelocity = m_maxDepenetrationVelocity;
            m_rigidbody.solverIterations = Mathf.Max(m_rigidbody.solverIterations, 12);
            m_rigidbody.solverVelocityIterations = Mathf.Max(m_rigidbody.solverVelocityIterations, 4);
            ConfigureBodyCollisionMaterial();
        }
    }

    //車体コライダーの摩擦と反発を抑えて壁を浅く擦った時の急停止を防ぐ関数
    void ConfigureBodyCollisionMaterial()
    {
        m_bodyCollisionMaterial = new PhysicsMaterial("VehicleBodyCollision")
        {
            dynamicFriction = m_bodyCollisionFriction,
            staticFriction = m_bodyCollisionFriction,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        Collider[] vehicleColliders = GetComponentsInChildren<Collider>(true);
        for (int index = 0; index < vehicleColliders.Length; index++)
        {
            Collider vehicleCollider = vehicleColliders[index];
            if (vehicleCollider is WheelCollider || vehicleCollider.attachedRigidbody != m_rigidbody || vehicleCollider.isTrigger) { continue; }
            vehicleCollider.sharedMaterial = m_bodyCollisionMaterial;
        }
    }

    //旧WheelController2024の物理更新を停止して二重計算を防ぐ関数
    void DisableLegacyWheelSimulation()
    {
        WheelController2024[] legacyWheels = GetComponentsInChildren<WheelController2024>(true);
        for (int index = 0; index < legacyWheels.Length; index++) { legacyWheels[index].enabled = false; }
    }

    //ゲームの開始時に呼ばれる初期化処理
    void Start()
    {
        m_mission.Initialize();
        m_engine.Initialize();
    }

    //車両階層内の駆動系を自動取得し、見つからないコンポーネントだけ車両本体へ追加する関数
    void ResolveVehicleParts()
    {
        if (m_engine == null) { m_engine = GetComponentInChildren<Car_Engine>(true); }
        if (m_clutch == null) { m_clutch = GetComponentInChildren<Clutch>(true); }
        if (m_mission == null) { m_mission = GetComponentInChildren<Transmission>(true); }
        if (m_differential == null) { m_differential = GetComponentInChildren<Differential>(true); }
        if (m_brake == null) { m_brake = GetComponentInChildren<Brake>(true); }
        if (m_steering == null) { m_steering = GetComponentInChildren<Steering>(true); }
        if (m_wheelController == null) { m_wheelController = GetComponentInChildren<WheelController2026>(true); }

        if (m_engine == null) { m_engine = gameObject.AddComponent<Car_Engine>(); }
        if (m_clutch == null) { m_clutch = gameObject.AddComponent<Clutch>(); }
        if (m_mission == null) { m_mission = gameObject.AddComponent<Transmission>(); }
        if (m_differential == null) { m_differential = gameObject.AddComponent<Differential>(); }
        if (m_brake == null) { m_brake = gameObject.AddComponent<Brake>(); }
        if (m_steering == null) { m_steering = gameObject.AddComponent<Steering>(); }
        if (m_wheelController == null) { m_wheelController = gameObject.AddComponent<WheelController2026>(); }
    }


    //物理演算の更新ごとに呼ばれる処理
    void FixedUpdate()
    {
        //衝突後の速度補正で相対速度を誤用しないよう物理更新直前の車体速度を保存する
        m_velocityBeforePhysicsStep = m_rigidbody.linearVelocity;
        m_hasVelocityBeforePhysicsStep = true;

        float driveInputRate = m_accelInput > m_smoothedDriveInput ? m_driveInputRiseRate : m_driveInputFallRate;
        m_smoothedDriveInput = Mathf.MoveTowards(m_smoothedDriveInput, m_accelInput, driveInputRate * Time.fixedDeltaTime);

        //復帰後の最初のアクセル入力で、車体を1 km/h相当まで動かす。
        ApplyRecoveryRestartAssist();
        PrepareDriveAwayFromAnyStop();

        //完全停止してブレーキを離した瞬間に、制動中の負スリップを次の発進へ持ち越さない。
        if (m_previousBrakeInput > 0.01f && m_brakeInput <= 0.01f && m_KPH < 0.5f) { PrepareStationaryRestart(); }

        //ギア切り替え
        m_mission.TransmissionUpdate(m_engine.RPM, m_KPH, m_accelInput);
        m_differential.SetFinalDriveRatio(m_mission.FinalDriveRatio);

        //駆動トルク(トランスミッション) = エンジントルク * 現在のギア比
        float driveTorque = m_clutch.ClutchTorque * m_mission.CurrentGearRatio;

        //低速発進時にクラッチ計算の立ち上がりを待たず駆動力を伝える
        if (!m_IsPullUp && m_brakeInput <= 0.01f && m_accelInput > 0.03f && m_mission.CurrentGearRatio != 0f &&
            m_KPH < m_launchAssistMaximumSpeedKph)
        {
            float launchRatio = 1f - Mathf.Clamp01(m_KPH / m_launchAssistMaximumSpeedKph);
            float launchTorque = m_launchAssistTorque * m_smoothedDriveInput * launchRatio;
            driveTorque = Mathf.Sign(m_mission.CurrentGearRatio) * Mathf.Max(Mathf.Abs(driveTorque), launchTorque);
        }

        //0km/hから100km/hまでの加速をゲーム向けに強化する
        if (!m_IsPullUp && m_brakeInput <= 0.01f && m_accelInput > 0.03f && m_mission.CurrentGearRatio > 0f)
        {
            float accelerationFade = Mathf.InverseLerp(80f, m_arcadeAccelerationFadeEndKph, m_KPH);
            float accelerationMultiplier = Mathf.Lerp(1f, m_arcadeAccelerationTorqueMultiplier, m_smoothedDriveInput);
            driveTorque *= Mathf.Lerp(accelerationMultiplier, 1f, accelerationFade);
        }

        //ATの1速でクリープトルクをアクセル駆動へ滑らかに受け渡す処理
        if (m_mission.Type == Transmission.TransmissionType.Automatic && m_mission.ActiveGear == 1 && !m_mission.IsGearChanging &&
            !m_IsPullUp && m_brakeInput <= 0.01f)
        {
            float signedKPH = Vector3.Dot(m_rigidbody.linearVelocity, transform.forward) * 3.6f;
            float creepAmount = Mathf.Clamp01((m_atCreepTargetSpeed - signedKPH) / m_atCreepTargetSpeed);
            float creepTorque = m_atCreepTorque * creepAmount;
            driveTorque = Mathf.Max(driveTorque, creepTorque);
        }

        //衝突角が正面に近いほど壁へ向かう駆動力を強く弱める
        driveTorque *= GetSideImpactDriveTorqueRatio();

        //ESCより先に現在の入力と速度から実際に使う舵角を更新する
        m_steering.InputAngle = m_steerInput;
        m_steering.VehicleSpeedKph = m_rigidbody.linearVelocity.magnitude * 3.6f;
        m_steering.UpdateSteering(Time.fixedDeltaTime);

        //ESCの介入を計算し、必要に応じてトルクを制限する
        UpdateESC();

        //プロペラシャフトの速度を計算
        float shaftVelocity = 0f;

        //ギアの入力側の値を計算する
        float ClutchInputSide = shaftVelocity * m_mission.CurrentGearRatio;

        //ディファレンシャルにトルクを保存
        m_differential.InputTorque = driveTorque * (1f - m_escIntervention * m_escMaximumTorqueReduction);

        //ブレーキに入力を保存
        m_brake.BrakeInput = m_brakeInput;

        //ESCの介入をタイヤ側へ渡す
        m_wheelController.SetStabilityControl(1f, m_escBrakeWheelIndex, m_escMaximumWheelBrakeTorque * m_escIntervention);

        //各ホイールの処理
        m_wheelController.WheelUpdate();
        UpdateIntroGrounding();
        ApplySideImpactStability();

        //接地中の車体へ補助加速度を加えて3秒以内の加速感を作る
        if (!m_IsPullUp && m_brakeInput <= 0.01f && m_accelInput > 0.03f && Mathf.Abs(m_steerInput) < 0.1f && !IsDrivingIntoSideImpact() &&
            m_mission.CurrentGearRatio > 0f && m_wheelController.GroundedWheelCount >= 2)
        {
            float assistFade = 1f - Mathf.InverseLerp(100f, m_arcadeAccelerationFadeEndKph, m_KPH);
            m_rigidbody.AddForce(transform.forward * (m_arcadeAccelerationAssist * m_smoothedDriveInput * assistFade), ForceMode.Acceleration);
        }

        PreventRoadFallThrough();
        shaftVelocity = m_wheelController.ShaftAngularVelocity;
        ClutchInputSide = shaftVelocity * m_mission.CurrentGearRatio;

        //ニュートラルだったとき
        if (m_mission.CurrentGearRatio == 0f)
        {
            //クラッチの出力
            ClutchInputSide = m_engine.RPM * CarPhysics.RPM2Rad;
        }

        //MTだけ手動クラッチ入力を渡し、ATクラッチの自動接続率を毎フレーム上書きしない
        if (!m_clutch.AutoClutch) { m_clutch.ClutchInput = m_clutchInput; }
        m_clutch.GearChanging = m_mission.IsGearChanging;

        //クラッチトルクの更新
        m_clutch.DrivetrainUpdate(ClutchInputSide, m_engine.AngularVelocity, m_engine.EngineTorque, m_mission.CurrentGearRatio, m_engine.Inertia);

        //エンジンの回転数の更新
        bool atSpeedLimiter = m_mission.Type == Transmission.TransmissionType.Automatic && m_KPH >= m_atMaximumSpeedKph;
        m_engine.InjectionCut = m_mission.IsGearChanging || atSpeedLimiter;
        m_engine.EngineUpdate(m_accelInput, m_clutch.ClutchTorque);
        //クラッチ接続率に合わせて駆動軸回転をエンジンへ段差なく同期する処理
        float drivetrainCoupling = m_mission.CurrentGearRatio != 0f && !m_mission.IsGearChanging && !m_IsPullUp ? m_clutch.Engagement : 0f;
        m_engine.SynchronizeToDrivetrain(ClutchInputSide, drivetrainCoupling);

        //車速の計算
        m_KPH = m_rigidbody.linearVelocity.magnitude * 3600f / 1000f;

        //車体のピッチフィールを計算
        ApplyBodyPitchFeel();

        //坂道などで実際に後退している場合へ影響しないよう、1 km/h未満だけを補正する。
        PreventLowSpeedWrongDirection();
        m_previousBrakeInput = m_brakeInput;

    }

    //ESCの介入を計算する
    void UpdateESC()
    {
        //車体のローカル座標系での速度を計算
        Vector3 localVelocity = transform.InverseTransformDirection(m_rigidbody.linearVelocity);
        float forwardSpeed = localVelocity.z;
        m_escSlipAngle = Mathf.Atan2(localVelocity.x, Mathf.Max(1f, Mathf.Abs(forwardSpeed))) * Mathf.Rad2Deg;

        //実際の中央舵角とホイールベースから単純車両モデルの定常ヨーレートを計算する
        float referenceSteerAngle = m_steering.CurrentCenterAngle;
        float kinematicYawRate = forwardSpeed * Mathf.Tan(referenceSteerAngle * Mathf.Deg2Rad) / m_escWheelBase;

        //タイヤが発生できる横加速度を超えない範囲に目標ヨーレートを制限する
        float gripLimitedYawRate = m_escMaximumLateralAcceleration / Mathf.Max(1f, Mathf.Abs(forwardSpeed));
        float targetYawRate = Mathf.Clamp(kinematicYawRate, -gripLimitedYawRate, gripLimitedYawRate);

        //舵を切った瞬間に定常旋回へならないように目標ヨーレートへ過渡応答させる
        //操舵を戻した後に以前の旋回目標を残さず、ESCが車を曲げ続けることを防ぐ
        float yawResponseRate = Mathf.Abs(referenceSteerAngle) < 0.05f ? m_escYawRecenteringRate : m_escYawResponseRate;
        m_escDesiredYawRate = Mathf.MoveTowards(m_escDesiredYawRate, targetYawRate, yawResponseRate * Time.fixedDeltaTime);
        float actualYawRate = Vector3.Dot(m_rigidbody.angularVelocity, transform.up);
        float yawError = actualYawRate - m_escDesiredYawRate;

        //ESCの介入条件を判定する
        if (!m_escEnabled || IsAirborne || m_KPH < m_escMinimumSpeedKph || Mathf.Abs(forwardSpeed) < 1f)
        {
            m_escIntervention = 0f;
            m_escBrakeWheelIndex = -1;
            m_escActive = false;
            m_escDesiredYawRate = Mathf.MoveTowards(m_escDesiredYawRate, 0f, m_escYawResponseRate * Time.fixedDeltaTime);
            return;
        }

        //スリップ角とヨー誤差の両方を考慮して、ESCの介入度合いを計算する
        float slipIntervention = Mathf.InverseLerp(m_escSlipAngleThreshold, m_escSlipAngleThreshold * 2.5f, Mathf.Abs(m_escSlipAngle));
        float yawIntervention = Mathf.InverseLerp(m_escYawErrorThreshold, m_escYawErrorThreshold * 3f, Mathf.Abs(yawError));
        m_escIntervention = Mathf.Clamp01(Mathf.Max(slipIntervention, yawIntervention));
        m_escActive = m_escIntervention > 0.01f;

        //操舵方向が小さい場合は実際のヨー方向から車両の回転方向を判定する
        bool turningRight = Mathf.Abs(referenceSteerAngle) > 0.1f ? referenceSteerAngle > 0f : actualYawRate > 0f;
        bool oppositeYaw = Mathf.Abs(m_escDesiredYawRate) > 0.05f && Mathf.Sign(actualYawRate) != Mathf.Sign(m_escDesiredYawRate);
        bool oversteer = oppositeYaw || Mathf.Abs(actualYawRate) > Mathf.Abs(m_escDesiredYawRate) + m_escYawErrorThreshold;

        m_escBrakeWheelIndex = oversteer ? (turningRight ? 1 : 0) : (turningRight ? 2 : 3);
    }

    //道路を貫通して落下し続ける場合に最後の接地点へ戻す関数
    void PreventRoadFallThrough()
    {
        if (m_rigidbody == null || m_wheelController == null) { return; }

        //衝突で車体が大きく傾いて接地を失った場合だけ直前の正常姿勢へ戻す
        float tiltAngle = Vector3.Angle(transform.up, Vector3.up);
        if (m_hasSafePose && tiltAngle >= m_rolloverRecoveryAngle && m_wheelController.GroundedWheelCount < 3)
        {
            m_rigidbody.position = m_lastSafePosition + Vector3.up * 0.15f;
            m_rigidbody.rotation = m_lastSafeRotation;
            ResetAfterCourseRecovery();
            return;
        }

        //3輪以上が接地している安定状態を復帰位置として保存する
        if (m_wheelController.GroundedWheelCount >= 3 && Vector3.Dot(m_rigidbody.linearVelocity, Physics.gravity.normalized) < 2f)
        {
            m_lastSafePosition = m_rigidbody.position;
            m_lastSafeRotation = m_rigidbody.rotation;
            m_hasSafePose = true;
            return;
        }

        //短いジャンプや縁石通過では復帰させない
        if (!m_hasSafePose || !m_wheelController.IsAirborne || m_wheelController.AirborneTime < m_fallProtectionDelay) { return; }
        if (m_rigidbody.position.y >= m_lastSafePosition.y - m_fallProtectionHeight) { return; }

        //路面直下へ抜けた車体だけを直前の接地高さへ戻す
        Vector3 correctedPosition = m_rigidbody.position;
        correctedPosition.y = m_lastSafePosition.y + 0.08f;
        m_rigidbody.position = correctedPosition;

        //前進速度を維持して上下方向の落下速度だけを止める
        Vector3 correctedVelocity = m_rigidbody.linearVelocity;
        correctedVelocity.y = Mathf.Max(0f, correctedVelocity.y);
        m_rigidbody.linearVelocity = correctedVelocity;
        m_rigidbody.angularVelocity = Vector3.Project(m_rigidbody.angularVelocity, transform.up);
        ResetBodyPitchState(Vector3.Dot(correctedVelocity, transform.forward));
    }

    //車体のピッチフィールを計算する
    void ApplyBodyPitchFeel()
    {
        //車体の前後方向の速度を計算
        float forwardSpeed = Vector3.Dot(m_rigidbody.linearVelocity, transform.forward);
        if (!m_pitchStateInitialized)
        {
            ResetBodyPitchState(forwardSpeed);
            return;
        }

        //前後方向の加速度を計算し、フィルタリングする
        float rawAcceleration = (forwardSpeed - m_previousForwardSpeed) / Mathf.Max(Time.fixedDeltaTime, 0.001f);
        m_previousForwardSpeed = forwardSpeed;
        if (Mathf.Abs(rawAcceleration) > m_maxLongitudinalAcceleration * 2f)
        {
            m_filteredLongitudinalAcceleration = 0f;
            return;
        }

        //加速度の値を制限して、フィルタリングする
        rawAcceleration = Mathf.Clamp(rawAcceleration, -m_maxLongitudinalAcceleration, m_maxLongitudinalAcceleration);
        float filterAmount = 1f - Mathf.Exp(-m_accelerationFilterSpeed * Time.fixedDeltaTime);
        m_filteredLongitudinalAcceleration = Mathf.Lerp(m_filteredLongitudinalAcceleration, rawAcceleration, filterAmount);

        //加減速に応じた小さなピッチトルクを加えて自然な荷重移動を補助する
        float pitchAcceleration = -m_filteredLongitudinalAcceleration * m_pitchResponse;
        if (pitchAcceleration < 0f) { pitchAcceleration *= m_accelerationPitchBoost; }
        float pitchLimit = pitchAcceleration < 0f ? m_maxNoseUpAngularAcceleration : m_maxPitchAngularAcceleration;
        m_rigidbody.AddTorque(transform.right * Mathf.Clamp(pitchAcceleration, -pitchLimit, pitchLimit), ForceMode.Acceleration);

        //サスペンションの自然な荷重移動を残しながら異常なピッチ回転だけを減衰する
        float localPitchVelocity = Vector3.Dot(m_rigidbody.angularVelocity, transform.right);
        const float maximumNaturalPitchSpeed = 0.18f;
        if (Mathf.Abs(localPitchVelocity) <= maximumNaturalPitchSpeed) { return; }
        float targetPitchVelocity = Mathf.Sign(localPitchVelocity) * maximumNaturalPitchSpeed;
        float correctedPitchVelocity = Mathf.MoveTowards(localPitchVelocity, targetPitchVelocity, m_pitchDamping * Time.fixedDeltaTime);
        m_rigidbody.angularVelocity += transform.right * (correctedPitchVelocity - localPitchVelocity);
    }

    //車体のピッチフィールの状態をリセットする
    void ResetBodyPitchState(float forwardSpeed = 0f)
    {
        m_previousForwardSpeed = forwardSpeed;
        m_filteredLongitudinalAcceleration = 0f;
        m_pitchStateInitialized = true;
    }

    //壁面へ衝突した瞬間から跳ね返りと姿勢変化を抑える関数
    void OnCollisionEnter(Collision collision)
    {
        ApplyImpactAngleSpeedResponse(collision, true);
        BeginSideImpactStability(collision);
    }

    //壁面へ接触している間も跳ね返りと姿勢変化を抑える関数
    void OnCollisionStay(Collision collision)
    {
        ApplyImpactAngleSpeedResponse(collision, false);
        BeginSideImpactStability(collision);
    }

    //衝突面の法線と速度から壁面衝突を判定して安定化を開始する関数
    void BeginSideImpactStability(Collision collision)
    {
        if (!m_collisionStabilityEnabled || collision.contactCount == 0) { return; }

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            bool isSideSurface = Mathf.Abs(Vector3.Dot(contact.normal, Vector3.up)) < 0.55f;
            if (!isSideSurface) { continue; }
            Vector3 horizontalNormal = Vector3.ProjectOnPlane(contact.normal, Vector3.up);
            if (horizontalNormal.sqrMagnitude <= 0.0001f) { continue; }
            horizontalNormal.Normalize();
            Vector3 incomingVelocity = GetVelocityBeforeImpact();
            Vector3 horizontalIncomingVelocity = Vector3.ProjectOnPlane(incomingVelocity, Vector3.up);
            if (Vector3.Dot(horizontalIncomingVelocity, horizontalNormal) > 0f) { horizontalNormal = -horizontalNormal; }
            float sideImpactSpeed = Mathf.Max(0f, -Vector3.Dot(horizontalIncomingVelocity, horizontalNormal));
            float incidence = sideImpactSpeed / Mathf.Max(0.1f, horizontalIncomingVelocity.magnitude);
            if (sideImpactSpeed < m_sideImpactMinimumSpeed || incidence < m_sideImpactMinimumIncidence) { continue; }
            float normalBlend = 1f - Mathf.Exp(-m_sideImpactNormalResponse * Time.fixedDeltaTime);
            m_sideImpactNormal = m_sideImpactNormal.sqrMagnitude > 0.0001f ?
                Vector3.Slerp(m_sideImpactNormal, horizontalNormal, normalBlend).normalized : horizontalNormal;
            m_sideImpactIncidence = incidence;
            m_sideImpactStabilityTime = Mathf.Min(m_sideImpactStabilityDuration, m_sideImpactReleaseDuration);
            LimitSideImpactRebound();
            LimitSideImpactAngularVelocity();
            return;
        }
    }

    //衝突角度とギア方向から壁接触中の駆動トルク倍率を返す関数
    float GetSideImpactDriveTorqueRatio()
    {
        if (m_sideImpactStabilityTime <= 0f || m_sideImpactNormal.sqrMagnitude <= 0.0001f || m_mission.CurrentGearRatio == 0f ||
            m_sideImpactIncidence < m_sideImpactDriveReductionIncidence) { return 1f; }
        Vector3 driveDirection = transform.forward * Mathf.Sign(m_mission.CurrentGearRatio);
        if (Vector3.Dot(driveDirection, m_sideImpactNormal) >= -0.35f) { return 1f; }

        //浅い接触では駆動力を残し、正面に近い衝突だけ壁登り防止を強くする
        float impactSeverity = Mathf.InverseLerp(m_sideImpactDriveReductionIncidence, 1f, m_sideImpactIncidence);
        return Mathf.Lerp(1f, m_sideImpactDriveTorqueRatio, impactSeverity);
    }

    //壁へ向かう駆動中で衝突角度に応じた駆動制限が必要か返す関数
    bool IsDrivingIntoSideImpact()
    {
        return GetSideImpactDriveTorqueRatio() < 0.999f;
    }

    //壁へ入る速度成分だけを除去し衝突前より速くならない接線方向速度へ補正する関数
    void ApplyImpactAngleSpeedResponse(Collision collision, bool applyInitialImpactLoss)
    {
        if (!m_collisionStabilityEnabled || m_rigidbody == null || collision.contactCount == 0) { return; }

        Vector3 incomingVelocity = GetVelocityBeforeImpact();
        Vector3 horizontalIncomingVelocity = Vector3.ProjectOnPlane(incomingVelocity, Vector3.up);
        float horizontalSpeed = horizontalIncomingVelocity.magnitude;
        if (horizontalSpeed < m_impactResponseMinimumSpeed) { return; }

        Vector3 selectedNormal = Vector3.zero;
        float selectedInwardSpeed = 0f;
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (Mathf.Abs(Vector3.Dot(contact.normal, Vector3.up)) >= 0.55f) { continue; }

            Vector3 horizontalNormal = Vector3.ProjectOnPlane(contact.normal, Vector3.up);
            if (horizontalNormal.sqrMagnitude <= 0.0001f) { continue; }
            horizontalNormal.Normalize();
            if (Vector3.Dot(horizontalIncomingVelocity, horizontalNormal) > 0f) { horizontalNormal = -horizontalNormal; }
            float inwardSpeed = Mathf.Max(0f, -Vector3.Dot(horizontalIncomingVelocity, horizontalNormal));
            if (inwardSpeed <= selectedInwardSpeed) { continue; }
            selectedNormal = horizontalNormal;
            selectedInwardSpeed = inwardSpeed;
        }

        if (selectedInwardSpeed < m_impactResponseMinimumSpeed || selectedNormal.sqrMagnitude <= 0.0001f) { return; }

        //衝突角度を法線速度と全速度の比で求め、浅い接触ほど進行方向の速度を残す
        float incidence = selectedInwardSpeed / Mathf.Max(0.1f, horizontalSpeed);
        float impactSeverity = Mathf.InverseLerp(m_glancingImpactIncidence, m_directImpactIncidence, incidence);
        float speedRetention = applyInitialImpactLoss ? Mathf.Lerp(m_glancingImpactSpeedRetention, m_directImpactSpeedRetention, impactSeverity) : 1f;
        Vector3 tangentialVelocity = horizontalIncomingVelocity + selectedNormal * selectedInwardSpeed;
        float redirectRatio = applyInitialImpactLoss ?
            Mathf.Lerp(m_glancingImpactTangentialRedirect, m_directImpactTangentialRedirect, impactSeverity) : 0f;
        float correctedTangentialSpeed = tangentialVelocity.magnitude * speedRetention + selectedInwardSpeed * redirectRatio;
        Vector3 correctedHorizontalVelocity = tangentialVelocity.sqrMagnitude > 0.0001f ?
            tangentialVelocity.normalized * Mathf.Min(horizontalSpeed, correctedTangentialSpeed) : Vector3.zero;
        if (Vector3.Dot(correctedHorizontalVelocity, horizontalIncomingVelocity) < 0f) { correctedHorizontalVelocity = Vector3.zero; }
        if (correctedHorizontalVelocity.magnitude > horizontalSpeed)
        {
            correctedHorizontalVelocity = correctedHorizontalVelocity.normalized * horizontalSpeed;
        }
        float verticalSpeed = Mathf.Min(Vector3.Dot(m_rigidbody.linearVelocity, Vector3.up), m_sideImpactMaximumUpwardSpeed);
        m_rigidbody.linearVelocity = correctedHorizontalVelocity + Vector3.up * verticalSpeed;
    }

    //衝突判定で使う物理更新直前の車体速度を返す関数
    Vector3 GetVelocityBeforeImpact()
    {
        return m_hasVelocityBeforePhysicsStep ? m_velocityBeforePhysicsStep : m_rigidbody.linearVelocity;
    }

    //壁から離れる方向と上方向の速度を制限して車体が跳ね上がることを防ぐ関数
    void LimitSideImpactRebound()
    {
        if (m_rigidbody == null || m_sideImpactNormal.sqrMagnitude <= 0.0001f) { return; }

        Vector3 limitedVelocity = m_rigidbody.linearVelocity;
        float reboundSpeed = Vector3.Dot(limitedVelocity, m_sideImpactNormal);
        if (reboundSpeed > m_sideImpactMaximumReboundSpeed)
        {
            limitedVelocity -= m_sideImpactNormal * (reboundSpeed - m_sideImpactMaximumReboundSpeed);
        }
        float upwardSpeed = Vector3.Dot(limitedVelocity, Vector3.up);
        if (upwardSpeed > m_sideImpactMaximumUpwardSpeed)
        {
            limitedVelocity -= Vector3.up * (upwardSpeed - m_sideImpactMaximumUpwardSpeed);
        }
        m_rigidbody.linearVelocity = limitedVelocity;
    }

    //衝突直後のピッチとロールを抑えて車体とカメラの大揺れを防ぐ関数
    void LimitSideImpactAngularVelocity()
    {
        if (m_rigidbody == null) { return; }

        Vector3 localAngularVelocity = transform.InverseTransformDirection(m_rigidbody.angularVelocity);
        localAngularVelocity.x = Mathf.Clamp(localAngularVelocity.x, -m_sideImpactMaximumPitchSpeed, m_sideImpactMaximumPitchSpeed);
        localAngularVelocity.y = Mathf.Clamp(localAngularVelocity.y, -m_sideImpactMaximumYawSpeed, m_sideImpactMaximumYawSpeed);
        localAngularVelocity.z = Mathf.Clamp(localAngularVelocity.z, -m_sideImpactMaximumRollSpeed, m_sideImpactMaximumRollSpeed);
        localAngularVelocity.x = Mathf.MoveTowards(localAngularVelocity.x, 0f, m_sideImpactPitchDamping * Time.fixedDeltaTime);
        localAngularVelocity.z = Mathf.MoveTowards(localAngularVelocity.z, 0f, m_sideImpactRollDamping * Time.fixedDeltaTime);
        m_rigidbody.angularVelocity = transform.TransformDirection(localAngularVelocity);
    }

    //実行中に生成した車体用物理マテリアルを破棄する関数
    void OnDestroy()
    {
        if (m_bodyCollisionMaterial != null) { Destroy(m_bodyCollisionMaterial); }
    }

    //接地を残したまま壁へ衝突した車体の過剰なロール回転を減衰する関数
    void ApplySideImpactStability()
    {
        if (m_sideImpactStabilityTime <= 0f || m_rigidbody == null) { return; }

        m_sideImpactStabilityTime = Mathf.Max(0f, m_sideImpactStabilityTime - Time.fixedDeltaTime);
        LimitSideImpactRebound();
        LimitSideImpactAngularVelocity();

        float tiltAngle = Vector3.Angle(transform.up, Vector3.up);
        if (tiltAngle <= 1f) { return; }

        //ピッチとヨーを変えず車体のロールだけを緩やかに水平へ戻す
        float rollError = Vector3.SignedAngle(transform.up, Vector3.up, transform.forward);
        float limitedRollError = Mathf.Clamp(rollError, -m_sideImpactMaximumCorrectionAngle, m_sideImpactMaximumCorrectionAngle);
        float uprightAcceleration = limitedRollError * Mathf.Deg2Rad * m_sideImpactUprightStrength;
        m_rigidbody.AddTorque(transform.forward * uprightAcceleration, ForceMode.Acceleration);
    }

    //停止状態からの発進準備を行う
    void PrepareDriveAwayFromAnyStop()
    {
        //停止状態からの発進準備を行う条件を判定する
        bool canDrive = !m_IsPullUp && m_mission.ActiveGear != 0 && m_accelInput > 0.03f && m_brakeInput <= 0.01f;

        //一度走り出した後は再び監視を有効にする。
        if (!canDrive || m_KPH > 1f)
        {
            m_driveAwayPrepared = false;
            return;
        }

        //発進準備が完了している場合は、再度準備を行わない。
        if (m_driveAwayPrepared || m_KPH > 0.5f) { return; }

        float forwardSpeed = Vector3.Dot(m_rigidbody.linearVelocity, transform.forward);
        float gearDirection = Mathf.Sign(m_mission.ActiveGear);

        m_wheelController.PrepareDriveAway(forwardSpeed * gearDirection);

        //停止原因に関係なく、古いクラッチ回転差とエンジン負荷を次の発進へ持ち越さない。
        m_clutch.ResetDynamics();
        m_engine.ResetToIdle(0f);
        m_rigidbody.WakeUp();
        m_driveAwayPrepared = true;
    }

    void ApplyRecoveryRestartAssist()
    {
        //復帰後の最初のアクセル入力で、車体を1 km/h相当まで動かす。
        if (!m_waitingForRecoveryRestart || m_accelInput <= 0.01f || m_mission.ActiveGear == 0) { return; }

        //ギアの方向に応じて、前後方向の速度を計算する
        float gearDirection = Mathf.Sign(m_mission.ActiveGear);
        float forwardSpeed = Vector3.Dot(m_rigidbody.linearVelocity, transform.forward);
        float speedInGearDirection = forwardSpeed * gearDirection;

        //タイヤの低速スリップが0付近で振動しても、プレイヤーを待たせず1物理フレームで再始動する。
        const float restartSpeed = 1f / 3.6f;
        if (speedInGearDirection < restartSpeed)
        {
            float targetForwardSpeed = restartSpeed * gearDirection;
            m_rigidbody.linearVelocity += transform.forward * (targetForwardSpeed - forwardSpeed);
        }

        m_lastRecoveryResponseSeconds = Time.realtimeSinceStartup - m_recoveryRequestedAt;
        m_waitingForRecoveryRestart = false;
    }

    //完全停止してブレーキを離した瞬間に、制動中の負スリップを次の発進へ持ち越さない。
    void PrepareStationaryRestart()
    {
        m_wheelController.ResetDynamics();

        m_clutch.ResetDynamics();
        m_engine.ResetToIdle(0f);
        m_smoothedDriveInput = 0f;
        m_driveAwayPrepared = false;
    }

    //停止直前のタイヤ計算誤差だけで、選択中のギアと逆方向へ転がるのを防ぐ。
    void PreventLowSpeedWrongDirection()
    {
        //ギアがニュートラルか、車速が1 km/h以上の場合は処理しない
        if (m_mission.ActiveGear == 0 || m_KPH >= 1f) { return; }

        //ギアと逆方向に微小な前後速度がある場合のみ処理する
        float forwardSpeed = Vector3.Dot(m_rigidbody.linearVelocity, transform.forward);
        bool movingAgainstGear = (m_mission.ActiveGear > 0 && forwardSpeed < 0f) || (m_mission.ActiveGear < 0 && forwardSpeed > 0f);

        //ギアと逆方向に動いていない場合は処理しない
        if (!movingAgainstGear) { return; }

        //横方向と上下方向の動きは残し、ギアと反対向きの微小な前後速度だけを除去する。
        m_rigidbody.linearVelocity -= transform.forward * forwardSpeed;
        m_KPH = m_rigidbody.linearVelocity.magnitude * 3.6f;
    }

    //コース復帰時に車体と駆動系の運動状態をまとめて初期化する。
    public void ResetAfterCourseRecovery()
    {
        //Rigidbodyを取得する
        if (m_rigidbody == null) { TryGetComponent(out m_rigidbody); }

        //車体の運動状態を初期化する
        m_rigidbody.linearVelocity = Vector3.zero;
        m_rigidbody.angularVelocity = Vector3.zero;
        ResetBodyPitchState();
        m_KPH = 0f;

        m_previousBrakeInput = m_brakeInput;
        m_wheelController.ResetDynamics();

        //駆動系の状態を初期化する
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

    //トランスミッションの種類を切り替える
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

    //車両の固定状態を変更する
    public void PullUp(bool _active)
    {
        //Rigidbodyを取得する
        if (m_rigidbody == null) TryGetComponent(out m_rigidbody);
        if (m_rigidbody == null)
        {
            Debug.LogError("VehicleController: Rigidbodyがないため車両の固定状態を変更できません。", this);
            return;
        }

        //車両の固定状態を変更する
        RigidbodyConstraints constraints;
        if (_active)
        {
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
            if (m_meterUIManager != null) { m_meterUIManager.StartTimer(); }
        }

        //車両の固定状態を更新する
        m_IsPullUp = _active;
        m_mission.IsPullUp = _active;
        m_clutch.IsPullUp = _active;
        m_rigidbody.constraints = constraints;
        Physics.SyncTransforms();
        m_clutch.Oscillation = 1.0f;
    }

    //イントロ中に車両の前後左右と姿勢を固定しながらサスペンションを路面へ馴染ませる関数
    public void PrepareIntroGrounding()
    {
        PullUp(true);
        if (m_rigidbody == null) { return; }
        //接地情報が安定するまで車体を完全固定し、開始直後の落下とタイヤの跳ねを防ぐ
        m_introGroundingFramesRemaining = m_introGroundingFixedFrames;
        m_rigidbody.constraints = RigidbodyConstraints.FreezePosition | RigidbodyConstraints.FreezeRotation;
        m_rigidbody.WakeUp();
    }

    //イントロ開始直後の数フレームだけ路面食い込みを補正してから上下サスペンションを解放する関数
    void UpdateIntroGrounding()
    {
        if (m_introGroundingFramesRemaining <= 0 || m_rigidbody == null) { return; }
        m_wheelController.CorrectGroundPenetrationImmediately();
        m_introGroundingFramesRemaining--;
        if (m_introGroundingFramesRemaining > 0) { return; }
        m_rigidbody.linearVelocity = Vector3.zero;
        m_rigidbody.angularVelocity = Vector3.zero;
        m_rigidbody.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
        Physics.SyncTransforms();
    }
}
