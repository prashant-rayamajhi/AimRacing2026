// FFBの制御(G923対応)
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using UnityEngine.InputSystem.Controls;

public class G923_FFBController : MonoBehaviour
{
    public static G923_FFBController Instance { get; private set; }

    // 外部通知
    public static event Action<bool> OnG923ConnectionChanged;
    public enum EG923ResumeState
    {
        Connected,
        Disconnected,
        WaitingForFunction5,
    }

    public static event Action<EG923ResumeState> OnG923ResumeStateChanged;
    // マスター設定
    [Header("マスター設定")]
    [SerializeField, Tooltip("FFB全体の有効/無効スイッチ")]
    private bool m_enableMasterFFB = true;
    [SerializeField, Range(0f, 1f), Tooltip("FFB全体の出力倍率。1が最大")]
    private float m_masterGain = 1.0f;
    // 車体参照
    [Header("車体参照")]
    [SerializeField, Tooltip("Mapシーン内のVehicle。Manager配置時は未設定でも可")]
    private GameObject m_vehicle;
    [SerializeField, Tooltip("Mapシーン内のVehicleController。Manager配置時は自動取得可")]
    private VehicleController m_vehicleController;
    [SerializeField, Tooltip("VehicleControllerを自動取得する")]
    private bool m_autoFindVehicleController = true;
    [SerializeField, Tooltip("VehicleControllerを自動取得する間隔 秒")]
    private float m_vehicleSearchInterval = 1.0f;
    private float m_nextVehicleSearchTime = 0.0f;
    // Logitech SDK設定
    [Header("Logitech SDK設定")]
    [SerializeField, Tooltip("通常は0番のデバイスを使用する")]
    private int m_deviceIndex = 0;
    [SerializeField, Range(90, 900), Tooltip("G923の左右を合わせた操作範囲。360で片側180度")]
    private int m_steeringOperatingRange = 540;
    private bool m_steeringOperatingRangeApplied = false;
    [SerializeField, Tooltip("Editor上でもFFBを実行するか")]
    private bool m_enableFFBInEditor = true;
    // USB切断時の設定
    [Header("USB切断時の設定")]
    [SerializeField, Tooltip("USB切断時にゲームを一時停止するか")]
    private bool m_pauseGameOnDisconnect = true;
    [SerializeField, Tooltip("再接続時に自動でゲームを再開するか。Function5復帰を使う場合はOFF推奨")]
    private bool m_resumeGameOnReconnect = false;
    private bool m_pausedByG923Disconnect = false;
    private float m_timeScaleBeforeG923Pause = 1.0f;
    // 復帰操作
    [Header("復帰操作")]
    [SerializeField, Tooltip("再接続後、Function5を押すまで復帰させない")]
    private bool m_requireFunction5ToResume = true;
    [SerializeField, Tooltip("Function5に割り当てたInputActionを指定する")]
    private InputActionReference m_resumeAction;
    [SerializeField, Tooltip("復帰時にPlayerInputを再有効化する")]
    private bool m_reactivatePlayerInputOnResume = true;
    [SerializeField, Tooltip("PlayerInput参照。未設定なら自動検索する")]
    private PlayerInput m_playerInput;
    [SerializeField, Tooltip("復帰後に切り替える操作用ActionMap名")]
    private string m_gameplayActionMapName = "InGame";
    [SerializeField, Tooltip("復帰時に必ず走行用ActionMapへ切り替える")]
    private bool m_switchToGameplayActionMapOnResume = true;
    [SerializeField, Tooltip("Project-wide Actionsで使っているInputActionAsset。Aim2024Inputを入れる")]
    private InputActionAsset m_inputActionsAsset;
    [SerializeField, Tooltip("復帰後も有効化しておくシステム用ActionMap名")]
    private string m_systemActionMapName = "MainSystem";
    [SerializeField, Tooltip("復帰時にLogitech SDKを再初期化する")]
    private bool m_reinitializeLogitechSDKOnResume = true;
    [SerializeField, Tooltip("復帰時にInput System側のG923/Joystickデバイスを再同期する")]
    private bool m_refreshInputDevicesOnResume = true;
    [SerializeField, Tooltip("復帰時にInput System側のJoystick/G923デバイスをリセットする")]
    private bool m_resetInputSystemDevicesOnResume = true;
    [SerializeField, Tooltip("再接続直後、入力デバイス再同期まで待つ時間 秒")]
    private float m_inputRefreshDelayAfterReconnect = 0.5f;
    [SerializeField, Tooltip("開発用。キーボードのF5でも復帰できるようにする")]
    private bool m_allowKeyboardF5Resume = true;
    private bool m_waitingForFunction5Resume = false;
    private Coroutine m_resumeInputCoroutine;
    private bool m_hasLoggedWaitingForFunction5 = false;
    // FFBモード設定
    public enum EG923FFBMode
    {
        Standard,
        StrongShake,
        Child,
    }

    [Serializable]
    public class FFBModeSettings
    {
        [Header("モード全体")]
        [Range(0.0f, 1.0f), Tooltip("このモード全体のFFB倍率。Master Gainと掛け合わせて使う")]
        public float ffbGain = 1.0f;
        [Tooltip("停止中に残す揺れ倍率。0にすると停止中の揺れなし、0.2なら20%だけ残す")]
        [Range(0.0f, 1.0f)]
        public float stationaryShakeMultiplier = 0.25f;
        [Tooltip("この速度から高速安定化として揺れを減らし始める km/h")]
        public float shakeReduceStartSpeed = 40.0f;
        [Tooltip("この速度以上で揺れを最小側へ寄せる km/h")]
        public float shakeReduceEndSpeed = 120.0f;
        [Tooltip("高速安定化の効き方。大きいほど高速側で急に揺れが減る")]
        public float shakeStabilityCurvePower = 1.25f;
        [Range(0.0f, 1.0f), Tooltip("高速時に残す左右揺れの割合")]
        public float highSpeedShakeRemainRate = 0.15f;
        [Range(0.0f, 1.0f), Tooltip("高速時に残す路面凹凸の強さ割合")]
        public float highSpeedBumpRemainRate = 0.20f;
        [Range(0.0f, 1.0f), Tooltip("高速時に残す路面凹凸の頻度割合")]
        public float highSpeedBumpFrequencyRemainRate = 0.35f;
        [Header("メニュー・非走行中FFB設定")]
        [Tooltip("VehicleControllerがないシーンでもFFBをかける")]
        public bool enableMenuFFB = true;
        [Range(0, 200), Tooltip("メニュー中の中央へ戻す力の上限。最終出力は100で制限する")]
        public int menuSpringSaturation = 100;
        [Range(0, 200), Tooltip("メニュー中の中央へ戻す強さ。最終出力は100で制限する")]
        public int menuSpringCoefficient = 100;
        [Range(0, 200), Tooltip("メニュー中のハンドルの重さ。最終出力は100で制限する")]
        public int menuDamperForce = 25;
        [Header("仮の路面凹凸設定")]
        [Tooltip("仮の凹凸振動を有効にするか")]
        public bool enableFakeBump = true;
        [Tooltip("低速・停止付近でも弱い凹凸を残すか")]
        public bool enableFakeBumpAtLowSpeed = true;
        [Range(0, 200), Tooltip("振動の強さ 最小。最終出力は100で制限する")]
        public int fakeBumpMagnitudeMin = 3;
        [Range(0, 200), Tooltip("振動の強さ 最大。最終出力は100で制限する")]
        public int fakeBumpMagnitudeMax = 30;
        [Tooltip("振動が発生するまでの最短間隔 秒。小さいほど頻繁に揺れる")]
        public float bumpIntervalMin = 0.5f;
        [Tooltip("振動が発生するまでの最長間隔 秒。小さいほど頻繁に揺れる")]
        public float bumpIntervalMax = 2.0f;
        [Tooltip("1回の振動が継続する基本時間 秒。長いほど揺れが残る")]
        public float bumpDuration = 0.15f;
        [Tooltip("揺れの速度変化の強調度")]
        public float bumpSpeedCurvePower = 1.35f;
        [Range(0.0f, 3.0f), Tooltip("揺れ全体の内部倍率。最終出力は100で制限する")]
        public float bumpOutputMultiplier = 1.35f;
        [Range(0.0f, 1.0f), Tooltip("低速時でも最低限残す路面凹凸の倍率")]
        public float bumpMinSpeedRate = 0.55f;
        [Tooltip("揺れの発生頻度に対する速度変化の強調度")]
        public float bumpFrequencySpeedCurvePower = 1.25f;
        [Tooltip("低速時の揺れ頻度倍率")]
        public float bumpMinFrequencyMultiplier = 0.7f;
        [Tooltip("中速域までの揺れ頻度倍率。高速ではHigh Speed Bump Frequency Remain Rateで減らす")]
        public float bumpMaxFrequencyMultiplier = 6.0f;
        [Header("衝突時のFFB設定")]
        [Tooltip("衝突時FFBを有効にするか")]
        public bool enableCrashFFB = true;
        [Range(0, 100), Tooltip("衝突時にハンドルを振る力の最小値")]
        public int minCrashForce = 0;
        [Range(0, 100), Tooltip("衝突時にハンドルを振る力の最大値")]
        public int maxCrashForce = 40;
        [Tooltip("衝突によるハンドルの振れが継続する時間 秒")]
        public float crashDuration = 0.18f;
        [Tooltip("この速度未満の衝突では衝突FFBを出さない")]
        public float minCrashImpactSpeed = 1.5f;
        [Tooltip("衝突時のハンドルの振れ方向を反転する")]
        public bool invertCrashForce = true;
        [Header("低速時のFFB設定")]
        public float lowSpeedThreshold = 3.0f;
        [Range(0, 200)]
        public int lowSpeedSpringSaturation = 100;
        [Range(0, 200)]
        public int lowSpeedSpringCoefficient = 100;
        [Range(0, 200)]
        public int lowSpeedDamperForce = 25;
        [Header("走行中のセンタリング設定")]
        [Tooltip("Springが最大側へ近づく基準速度 km/h。低いほど早い速度で最大に近づく")]
        public float springSpeedReference = 80.0f;
        [Range(0, 200), Tooltip("走行中Spring Saturationの低速側")]
        public int minSpringSaturation = 100;
        [Range(0, 200), Tooltip("走行中Spring Saturationの高速側")]
        public int maxSpringSaturation = 100;
        [Range(0, 200), Tooltip("走行中Spring Coefficientの低速側")]
        public int minSpringCoefficient = 100;
        [Range(0, 200), Tooltip("走行中Spring Coefficientの高速側")]
        public int maxSpringCoefficient = 100;
        [Tooltip("速度によるSpring変化の強調度")]
        public float springSpeedCurvePower = 1.25f;
        [Range(0.0f, 2.0f), Tooltip("Spring全体の内部倍率。最終出力は100で制限する")]
        public float springOutputMultiplier = 1.0f;
        [Header("ダンパー設定")]
        [Tooltip("走行中Damperの基本値。大きいほど常に重い")]
        public float baseDamperForce = 25.0f;
        [Tooltip("速度によるDamper増加量。大きいほど高速時に重くなる")]
        public float speedDamperPower = 0.3f;
        [Tooltip("ブレーキ入力によるDamper増加量")]
        public float brakeDamperPower = 40.0f;
        [Range(0, 200), Tooltip("Damper計算の内部上限。最終出力は100で制限する")]
        public int maxDamperForce = 60;
        [Range(0, 200), Tooltip("走行中Damperの最低保証値。シフト時のDamper抜け対策")]
        public int minDrivingDamperForce = 85;
        [Tooltip("速度によるDamper変化の強調度")]
        public float damperSpeedCurvePower = 1.25f;
        [Range(0.0f, 3.0f), Tooltip("Damper全体の内部倍率。最終出力は100で制限する")]
        public float damperOutputMultiplier = 1.0f;
        [Tooltip("FFB用速度をなめらかにする。シフト時の急な軽さ対策")]
        public bool smoothSpeedForFFB = true;
        [Tooltip("FFB用速度が上がるときの追従速度 km/h per sec")]
        public float ffbSpeedRiseRate = 250.0f;
        [Range(0, 100), Tooltip("FFB用速度が下がる時の追従速度 km/h per sec。小さいほどシフト時に軽くなりにくい")]
        public float ffbSpeedDropRate = 12.0f;
        [Header("速度連動ハンドル揺れ設定")]
        [Tooltip("ConstantForceを使った左右揺れを有効にする。クラッシュFFB中は自動で譲る")]
        public bool enableSpeedShakeFFB = true;
        [Range(0, 100), Tooltip("左右揺れの低速側強度")]
        public int speedShakeMinForce = 8;
        [Range(0, 100), Tooltip("左右揺れの中速側最大強度。高速ではHigh Speed Shake Remain Rateで減らす")]
        public int speedShakeMaxForce = 35;
        [Tooltip("左右揺れが最大側へ近づく基準速度 km/h")]
        public float speedShakeReferenceSpeed = 100.0f;
        [Tooltip("低速時の左右揺れ周波数")]
        public float speedShakeMinFrequency = 7.0f;
        [Tooltip("中速時の左右揺れ周波数。高速では安定化で減らす")]
        public float speedShakeMaxFrequency = 18.0f;
        [Tooltip("速度による左右揺れ変化の強調度")]
        public float speedShakeCurvePower = 1.25f;
        [Range(0.0f, 2.0f), Tooltip("左右揺れ全体の内部倍率。最終出力は100で制限する")]
        public float speedShakeOutputMultiplier = 1.0f;
        public static FFBModeSettings CreateStandard()
        {
            return new FFBModeSettings
            {
                ffbGain = 1.0f,
                stationaryShakeMultiplier = 0.22f,
                shakeReduceStartSpeed = 35.0f,
                shakeReduceEndSpeed = 120.0f,
                shakeStabilityCurvePower = 1.3f,
                highSpeedShakeRemainRate = 0.12f,
                highSpeedBumpRemainRate = 0.18f,
                highSpeedBumpFrequencyRemainRate = 0.30f,
                enableMenuFFB = true,
                menuSpringSaturation = 100,
                menuSpringCoefficient = 100,
                menuDamperForce = 35,
                enableFakeBump = true,
                enableFakeBumpAtLowSpeed = true,
                fakeBumpMagnitudeMin = 2,
                fakeBumpMagnitudeMax = 18,
                bumpIntervalMin = 0.8f,
                bumpIntervalMax = 2.5f,
                bumpDuration = 0.10f,
                bumpSpeedCurvePower = 1.25f,
                bumpOutputMultiplier = 0.75f,
                bumpMinSpeedRate = 0.40f,
                bumpFrequencySpeedCurvePower = 1.15f,
                bumpMinFrequencyMultiplier = 0.4f,
                bumpMaxFrequencyMultiplier = 2.5f,
                enableCrashFFB = true,
                minCrashForce = 0,
                maxCrashForce = 30,
                crashDuration = 0.14f,
                minCrashImpactSpeed = 1.5f,
                invertCrashForce = true,
                lowSpeedThreshold = 3.0f,
                lowSpeedSpringSaturation = 100,
                lowSpeedSpringCoefficient = 100,
                lowSpeedDamperForce = 35,
                springSpeedReference = 90.0f,
                minSpringSaturation = 90,
                maxSpringSaturation = 100,
                minSpringCoefficient = 90,
                maxSpringCoefficient = 100,
                springSpeedCurvePower = 1.15f,
                springOutputMultiplier = 1.0f,
                baseDamperForce = 35.0f,
                speedDamperPower = 0.45f,
                brakeDamperPower = 35.0f,
                maxDamperForce = 80,
                minDrivingDamperForce = 55,
                damperSpeedCurvePower = 1.2f,
                damperOutputMultiplier = 1.0f,
                smoothSpeedForFFB = true,
                ffbSpeedRiseRate = 250.0f,
                ffbSpeedDropRate = 12.0f,
                enableSpeedShakeFFB = true,
                speedShakeMinForce = 4,
                speedShakeMaxForce = 18,
                speedShakeReferenceSpeed = 100.0f,
                speedShakeMinFrequency = 5.0f,
                speedShakeMaxFrequency = 12.0f,
                speedShakeCurvePower = 1.2f,
                speedShakeOutputMultiplier = 0.7f,
            };
        }

        public static FFBModeSettings CreateStrongShake()
        {
            return new FFBModeSettings
            {
                // 現在のInspector値に近い、強め演出用の初期値
                ffbGain = 1.0f,
                stationaryShakeMultiplier = 0.45f,
                shakeReduceStartSpeed = 50.0f,
                shakeReduceEndSpeed = 140.0f,
                shakeStabilityCurvePower = 1.2f,
                highSpeedShakeRemainRate = 0.25f,
                highSpeedBumpRemainRate = 0.30f,
                highSpeedBumpFrequencyRemainRate = 0.45f,
                enableMenuFFB = true,
                menuSpringSaturation = 100,
                menuSpringCoefficient = 100,
                menuDamperForce = 25,
                enableFakeBump = true,
                enableFakeBumpAtLowSpeed = true,
                fakeBumpMagnitudeMin = 3,
                fakeBumpMagnitudeMax = 30,
                bumpIntervalMin = 0.5f,
                bumpIntervalMax = 2.0f,
                bumpDuration = 0.15f,
                bumpSpeedCurvePower = 1.35f,
                bumpOutputMultiplier = 1.35f,
                bumpMinSpeedRate = 0.55f,
                bumpFrequencySpeedCurvePower = 1.25f,
                bumpMinFrequencyMultiplier = 0.7f,
                bumpMaxFrequencyMultiplier = 6.0f,
                enableCrashFFB = true,
                minCrashForce = 0,
                maxCrashForce = 40,
                crashDuration = 0.18f,
                minCrashImpactSpeed = 1.5f,
                invertCrashForce = true,
                lowSpeedThreshold = 3.0f,
                lowSpeedSpringSaturation = 100,
                lowSpeedSpringCoefficient = 100,
                lowSpeedDamperForce = 25,
                springSpeedReference = 80.0f,
                minSpringSaturation = 100,
                maxSpringSaturation = 100,
                minSpringCoefficient = 100,
                maxSpringCoefficient = 100,
                springSpeedCurvePower = 1.25f,
                springOutputMultiplier = 1.0f,
                baseDamperForce = 25.0f,
                speedDamperPower = 0.3f,
                brakeDamperPower = 40.0f,
                maxDamperForce = 60,
                minDrivingDamperForce = 85,
                damperSpeedCurvePower = 1.25f,
                damperOutputMultiplier = 1.0f,
                smoothSpeedForFFB = true,
                ffbSpeedRiseRate = 250.0f,
                ffbSpeedDropRate = 12.0f,
                enableSpeedShakeFFB = true,
                speedShakeMinForce = 8,
                speedShakeMaxForce = 35,
                speedShakeReferenceSpeed = 100.0f,
                speedShakeMinFrequency = 7.0f,
                speedShakeMaxFrequency = 18.0f,
                speedShakeCurvePower = 1.25f,
                speedShakeOutputMultiplier = 1.0f,
            };
        }

        public static FFBModeSettings CreateChild()
        {
            return new FFBModeSettings
            {
                ffbGain = 0.35f,
                stationaryShakeMultiplier = 0.10f,
                shakeReduceStartSpeed = 25.0f,
                shakeReduceEndSpeed = 90.0f,
                shakeStabilityCurvePower = 1.1f,
                highSpeedShakeRemainRate = 0.05f,
                highSpeedBumpRemainRate = 0.08f,
                highSpeedBumpFrequencyRemainRate = 0.20f,
                enableMenuFFB = true,
                menuSpringSaturation = 80,
                menuSpringCoefficient = 80,
                menuDamperForce = 20,
                enableFakeBump = true,
                enableFakeBumpAtLowSpeed = true,
                fakeBumpMagnitudeMin = 1,
                fakeBumpMagnitudeMax = 8,
                bumpIntervalMin = 1.0f,
                bumpIntervalMax = 3.0f,
                bumpDuration = 0.06f,
                bumpSpeedCurvePower = 1.0f,
                bumpOutputMultiplier = 0.45f,
                bumpMinSpeedRate = 0.25f,
                bumpFrequencySpeedCurvePower = 1.0f,
                bumpMinFrequencyMultiplier = 0.25f,
                bumpMaxFrequencyMultiplier = 1.2f,
                enableCrashFFB = true,
                minCrashForce = 0,
                maxCrashForce = 12,
                crashDuration = 0.08f,
                minCrashImpactSpeed = 2.0f,
                invertCrashForce = true,
                lowSpeedThreshold = 3.0f,
                lowSpeedSpringSaturation = 80,
                lowSpeedSpringCoefficient = 80,
                lowSpeedDamperForce = 18,
                springSpeedReference = 100.0f,
                minSpringSaturation = 65,
                maxSpringSaturation = 85,
                minSpringCoefficient = 65,
                maxSpringCoefficient = 85,
                springSpeedCurvePower = 1.0f,
                springOutputMultiplier = 0.8f,
                baseDamperForce = 18.0f,
                speedDamperPower = 0.2f,
                brakeDamperPower = 15.0f,
                maxDamperForce = 45,
                minDrivingDamperForce = 20,
                damperSpeedCurvePower = 1.0f,
                damperOutputMultiplier = 0.8f,
                smoothSpeedForFFB = true,
                ffbSpeedRiseRate = 180.0f,
                ffbSpeedDropRate = 20.0f,
                enableSpeedShakeFFB = true,
                speedShakeMinForce = 1,
                speedShakeMaxForce = 6,
                speedShakeReferenceSpeed = 100.0f,
                speedShakeMinFrequency = 3.0f,
                speedShakeMaxFrequency = 7.0f,
                speedShakeCurvePower = 1.0f,
                speedShakeOutputMultiplier = 0.35f,
            };
        }
    }

    [Header("FFBモード設定")]
    [SerializeField, Tooltip("現在使用するFFBモード")]
    private EG923FFBMode m_currentFFBMode = EG923FFBMode.Standard;
    [SerializeField, Tooltip("確認用ホットキーを有効化。初期値はF6:Child(弱) / F7:Standard(中) / F8:StrongShake(強)")]
    private bool m_enableFFBModeHotkeys = true;
    [SerializeField, Tooltip("弱モードへ切り替えるキー。椅子側の弱め設定キーと合わせる")]
    private Key m_childModeKey = Key.F6;
    [SerializeField, Tooltip("中モードへ切り替えるキー。椅子側の通常設定キーと合わせる")]
    private Key m_standardModeKey = Key.F7;
    [SerializeField, Tooltip("強モードへ切り替えるキー。椅子側の強め設定キーと合わせる")]
    private Key m_strongShakeModeKey = Key.F8;
    [SerializeField, Tooltip("通常展示用。重さは残し、停止中と高速時の揺れを抑える")]
    private FFBModeSettings m_standardMode = FFBModeSettings.CreateStandard();
    [SerializeField, Tooltip("演出用。現在の設定に近く、大きめに揺れる")]
    private FFBModeSettings m_strongShakeMode = FFBModeSettings.CreateStrongShake();
    [SerializeField, Tooltip("子供・小柄な体験者用。軽く、揺れも弱め")]
    private FFBModeSettings m_childMode = FFBModeSettings.CreateChild();
    private float m_bumpWaitTimer = 0.0f;
    private float m_bumpActiveTimer = 0.0f;
    private bool m_isBumping = false;
    private float m_crashActiveTimer = 0.0f;
    private int m_currentCrashForce = 0;
    private bool m_hasSmoothedSpeedForFFB = false;
    private float m_smoothedSpeedForFFB = 0.0f;
    private float m_speedShakeTimer = 0.0f;
    // 接続状態
    private bool m_wasConnected = false;
    private bool m_hasCheckedConnection = false;
    [SerializeField, Tooltip("G923の接続・切断・復帰など重要ログを表示する")]
    private bool m_showConnectionLog = true;
    private bool m_isPrimaryInstance = false;
    // 初期化
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[G923 FFB] G923_FFBController が複数存在するため、後から生成されたものを無効化します。", this);
            enabled = false;
            return;
        }

        Instance = this;
        m_isPrimaryInstance = true;
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        ResolveVehicleControllerFromSerializedReference();
        ResetFakeBumpTimer();
    }

    private void OnEnable()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }

        if (Instance == null)
        {
            Instance = this;
            m_isPrimaryInstance = true;
        }

        EnableResumeAction();
        if (m_resumeAction != null && m_resumeAction.action != null)
        {
            m_resumeAction.action.performed += OnResumeActionPerformed;
        }

        InputSystem.onDeviceChange += OnInputDeviceChange;
    }

    private void OnValidate()
    {
        m_vehicleSearchInterval = Mathf.Max(0.1f, m_vehicleSearchInterval);
        ValidateFFBModeSettings(m_standardMode);
        ValidateFFBModeSettings(m_strongShakeMode);
        ValidateFFBModeSettings(m_childMode);
    }

    private void ValidateFFBModeSettings(FFBModeSettings profile)
    {
        if (profile == null)
        {
            return;
        }

        profile.ffbGain = Mathf.Clamp01(profile.ffbGain);
        profile.stationaryShakeMultiplier = Mathf.Clamp01(profile.stationaryShakeMultiplier);
        profile.shakeReduceStartSpeed = Mathf.Max(0.0f, profile.shakeReduceStartSpeed);
        profile.shakeReduceEndSpeed = Mathf.Max(profile.shakeReduceStartSpeed + 1.0f, profile.shakeReduceEndSpeed);
        profile.shakeStabilityCurvePower = Mathf.Max(0.1f, profile.shakeStabilityCurvePower);
        profile.highSpeedShakeRemainRate = Mathf.Clamp01(profile.highSpeedShakeRemainRate);
        profile.highSpeedBumpRemainRate = Mathf.Clamp01(profile.highSpeedBumpRemainRate);
        profile.highSpeedBumpFrequencyRemainRate = Mathf.Clamp01(profile.highSpeedBumpFrequencyRemainRate);
        if (profile.fakeBumpMagnitudeMin > profile.fakeBumpMagnitudeMax)
        {
            profile.fakeBumpMagnitudeMin = profile.fakeBumpMagnitudeMax;
        }

        profile.bumpIntervalMin = Mathf.Max(0.01f, profile.bumpIntervalMin);
        profile.bumpIntervalMax = Mathf.Max(profile.bumpIntervalMin, profile.bumpIntervalMax);
        profile.bumpDuration = Mathf.Max(0.01f, profile.bumpDuration);
        profile.bumpSpeedCurvePower = Mathf.Max(0.1f, profile.bumpSpeedCurvePower);
        profile.bumpOutputMultiplier = Mathf.Max(0.0f, profile.bumpOutputMultiplier);
        profile.bumpMinSpeedRate = Mathf.Clamp01(profile.bumpMinSpeedRate);
        profile.bumpFrequencySpeedCurvePower = Mathf.Max(0.1f, profile.bumpFrequencySpeedCurvePower);
        profile.bumpMinFrequencyMultiplier = Mathf.Max(0.0f, profile.bumpMinFrequencyMultiplier);
        profile.bumpMaxFrequencyMultiplier = Mathf.Max(profile.bumpMinFrequencyMultiplier, profile.bumpMaxFrequencyMultiplier);
        if (profile.minCrashForce > profile.maxCrashForce)
        {
            profile.minCrashForce = profile.maxCrashForce;
        }

        profile.crashDuration = Mathf.Max(0.01f, profile.crashDuration);
        profile.minCrashImpactSpeed = Mathf.Max(0.0f, profile.minCrashImpactSpeed);
        profile.lowSpeedThreshold = Mathf.Max(0.0f, profile.lowSpeedThreshold);
        profile.springSpeedReference = Mathf.Max(1.0f, profile.springSpeedReference);
        profile.springSpeedCurvePower = Mathf.Max(0.1f, profile.springSpeedCurvePower);
        profile.damperSpeedCurvePower = Mathf.Max(0.1f, profile.damperSpeedCurvePower);
        profile.speedShakeReferenceSpeed = Mathf.Max(1.0f, profile.speedShakeReferenceSpeed);
        profile.speedShakeMinFrequency = Mathf.Max(0.0f, profile.speedShakeMinFrequency);
        profile.speedShakeMaxFrequency = Mathf.Max(profile.speedShakeMinFrequency, profile.speedShakeMaxFrequency);
        profile.speedShakeCurvePower = Mathf.Max(0.1f, profile.speedShakeCurvePower);
        if (profile.speedShakeMinForce > profile.speedShakeMaxForce)
        {
            profile.speedShakeMinForce = profile.speedShakeMaxForce;
        }
    }

    // 更新処理
    private void Update()
    {
        if (!m_isPrimaryInstance)
        {
            return;
        }

        if (!CanCallLogitechSDK())
        {
            return;
        }

        // 再接続後、Function5待ち中なら最優先で復帰入力を見る
        if (m_waitingForFunction5Resume)
        {
            LogitechGSDK.LogiUpdate();
            bool isConnected = LogitechGSDK.LogiIsConnected(m_deviceIndex);
            if (!isConnected)
            {
                UpdateConnectionState(false);
                return;
            }

            EnableResumeAction();
            if (!m_hasLoggedWaitingForFunction5)
            {
                m_hasLoggedWaitingForFunction5 = true;
            }

            if (WasResumeActionPressedThisFrame())
            {
                ResumeFromG923Disconnect();
            }

            return;
        }

        if (!m_enableMasterFFB)
        {
            ForceStopAllFFB();
            return;
        }

        ResolveVehicleControllerIfNeeded();
        if (!LogitechGSDK.LogiUpdate())
        {
            UpdateConnectionState(false);
            return;
        }

        bool isConnectedNormal = LogitechGSDK.LogiIsConnected(m_deviceIndex);
        UpdateConnectionState(isConnectedNormal);
        if (!isConnectedNormal)
        {
            return;
        }

        ApplySteeringOperatingRange();
        HandleFFBModeHotkeys();
        FFBModeSettings currentProfile = GetCurrentFFBModeSettings();
        if (m_pausedByG923Disconnect && !m_resumeGameOnReconnect)
        {
            ForceStopAllFFB();
            return;
        }

        // VehicleControllerがないシーンでは、メニュー用FFBをかける
        if (m_vehicleController == null)
        {
            ApplyMenuFFB();
            return;
        }

        float speed = GetStableSpeedForFFB(m_vehicleController.KPH, currentProfile);
        float brakeForce = Mathf.Clamp01(m_vehicleController.Brake);
        if (speed < currentProfile.lowSpeedThreshold)
        {
            ApplyLowSpeedFFB(currentProfile);
            if (currentProfile.enableFakeBump && currentProfile.enableFakeBumpAtLowSpeed)
            {
                UpdateFakeBumps(speed, currentProfile);
            }
            else
            {
                StopFakeBump();
            }

            UpdateSpeedShakeFFB(speed, currentProfile);
            UpdateCrashFFB(currentProfile);
            return;
        }

        ApplyDrivingFFB(speed, brakeForce, currentProfile);
        if (currentProfile.enableFakeBump)
        {
            UpdateFakeBumps(speed, currentProfile);
        }
        else
        {
            StopFakeBump();
        }

        UpdateSpeedShakeFFB(speed, currentProfile);
        UpdateCrashFFB(currentProfile);
    }

    // Vehicle取得
    private void ResolveVehicleControllerFromSerializedReference()
    {
        if (m_vehicleController == null && m_vehicle != null)
        {
            m_vehicleController = m_vehicle.GetComponent<VehicleController>();
        }

        if (m_vehicleController == null)
        {
            m_vehicleController = GetComponent<VehicleController>();
        }

        if (m_vehicle == null && m_vehicleController != null)
        {
            m_vehicle = m_vehicleController.gameObject;
        }
    }

    private void ResolveVehicleControllerIfNeeded()
    {
        if (m_vehicleController != null)
        {
            return;
        }

        if (m_vehicle != null)
        {
            m_vehicleController = m_vehicle.GetComponent<VehicleController>();
            if (m_vehicleController != null)
            {
                return;
            }
        }

        if (!m_autoFindVehicleController)
        {
            return;
        }

        if (Time.unscaledTime < m_nextVehicleSearchTime)
        {
            return;
        }

        m_nextVehicleSearchTime = Time.unscaledTime + m_vehicleSearchInterval;
#if UNITY_2023_1_OR_NEWER
        m_vehicleController = FindFirstObjectByType<VehicleController>();
#else
        m_vehicleController = FindObjectOfType<VehicleController>();
#endif
        if (m_vehicleController != null)
        {
            m_vehicle = m_vehicleController.gameObject;
        }
    }

    // 実行環境・マスター判定
    private bool CanCallLogitechSDK()
    {
#if UNITY_EDITOR
        return m_enableFFBInEditor;
#else
        return true;
#endif
    }

    private bool CanUseFFB()
    {
        if (!m_enableMasterFFB)
        {
            return false;
        }

        return CanCallLogitechSDK();
    }

    private FFBModeSettings GetCurrentFFBModeSettings()
    {
        switch (m_currentFFBMode)
        {
            case EG923FFBMode.StrongShake:
                return m_strongShakeMode ?? m_standardMode;
            case EG923FFBMode.Child:
                return m_childMode ?? m_standardMode;
            case EG923FFBMode.Standard:
            default:
                return m_standardMode;
        }
    }

    private float GetEffectiveMasterGain(FFBModeSettings profile)
    {
        if (profile == null)
        {
            return Mathf.Clamp01(m_masterGain);
        }

        return Mathf.Clamp01(m_masterGain * profile.ffbGain);
    }

    private float GetMovingShakeMultiplier(float speed, FFBModeSettings profile)
    {
        if (profile == null)
        {
            return 1.0f;
        }

        float moveRate = Mathf.Clamp01(speed / Mathf.Max(0.1f, profile.lowSpeedThreshold));
        return Mathf.Lerp(profile.stationaryShakeMultiplier, 1.0f, moveRate);
    }

    private float GetHighSpeedStabilityRate(float speed, FFBModeSettings profile)
    {
        if (profile == null)
        {
            return 0.0f;
        }

        float stabilityRate = Mathf.InverseLerp(profile.shakeReduceStartSpeed, profile.shakeReduceEndSpeed, speed);
        return Mathf.Pow(stabilityRate, Mathf.Max(0.1f, profile.shakeStabilityCurvePower));
    }

    private float GetSpeedShakeRemainMultiplier(float speed, FFBModeSettings profile)
    {
        if (profile == null)
        {
            return 1.0f;
        }

        float stabilityRate = GetHighSpeedStabilityRate(speed, profile);
        return Mathf.Lerp(1.0f, profile.highSpeedShakeRemainRate, stabilityRate);
    }

    private float GetBumpRemainMultiplier(float speed, FFBModeSettings profile)
    {
        if (profile == null)
        {
            return 1.0f;
        }

        float stabilityRate = GetHighSpeedStabilityRate(speed, profile);
        return Mathf.Lerp(1.0f, profile.highSpeedBumpRemainRate, stabilityRate);
    }

    private float GetBumpFrequencyRemainMultiplier(float speed, FFBModeSettings profile)
    {
        if (profile == null)
        {
            return 1.0f;
        }

        float stabilityRate = GetHighSpeedStabilityRate(speed, profile);
        return Mathf.Lerp(1.0f, profile.highSpeedBumpFrequencyRemainRate, stabilityRate);
    }

    private void HandleFFBModeHotkeys()
    {
        if (!m_enableFFBModeHotkeys)
        {
            return;
        }

        if (Keyboard.current == null)
        {
            return;
        }

        if (WasKeyPressedThisFrame(m_childModeKey))
        {
            SetFFBModeChild();
        }

        if (WasKeyPressedThisFrame(m_standardModeKey))
        {
            SetFFBModeStandard();
        }

        if (WasKeyPressedThisFrame(m_strongShakeModeKey))
        {
            SetFFBModeStrongShake();
        }
    }

    private bool WasKeyPressedThisFrame(Key key)
    {
        if (key == Key.None || Keyboard.current == null)
        {
            return false;
        }

        KeyControl keyControl = Keyboard.current[key];
        return keyControl != null && keyControl.wasPressedThisFrame;
    }

    public void SetFFBMode(EG923FFBMode mode)
    {
        if (m_currentFFBMode == mode)
        {
            return;
        }

        m_currentFFBMode = mode;
        ForceStopAllFFB();
        ResetFakeBumpTimer();
    }

    public void SetFFBModeStandard()
    {
        SetFFBMode(EG923FFBMode.Standard);
    }

    public void SetFFBModeStrongShake()
    {
        SetFFBMode(EG923FFBMode.StrongShake);
    }

    public void SetFFBModeChild()
    {
        SetFFBMode(EG923FFBMode.Child);
    }

    public EG923FFBMode GetCurrentFFBMode()
    {
        return m_currentFFBMode;
    }

    private void G923Warning(string message)
    {
        if (!m_showConnectionLog)
        {
            return;
        }

        AppLog.LogWarning(message);
    }

    // 接続状態管理
    private void UpdateConnectionState(bool isConnected)
    {
        if (m_hasCheckedConnection && isConnected == m_wasConnected)
        {
            return;
        }

        m_hasCheckedConnection = true;
        m_wasConnected = isConnected;
        if (isConnected)
        {
            OnG923Connected();
        }
        else
        {
            OnG923Disconnected();
        }
    }

    private void OnG923Connected()
    {
        OnG923ConnectionChanged?.Invoke(true);
        if (m_pausedByG923Disconnect && m_requireFunction5ToResume)
        {
            m_waitingForFunction5Resume = true;
            m_hasLoggedWaitingForFunction5 = false;
            EnableResumeAction();
            ForceStopAllFFB();
            OnG923ResumeStateChanged?.Invoke(EG923ResumeState.WaitingForFunction5);
            G923Warning("[G923 FFB] G923が再接続されました。Function5を押して復帰してください。");
            return;
        }

        if (m_pausedByG923Disconnect && m_resumeGameOnReconnect)
        {
            ResumeFromG923Disconnect();
            return;
        }

        OnG923ResumeStateChanged?.Invoke(EG923ResumeState.Connected);
    }

    private void OnG923Disconnected()
    {
        G923Warning("[G923 FFB] G923の接続が切断されました。FFBを停止します。");
        m_waitingForFunction5Resume = false;
        m_hasLoggedWaitingForFunction5 = false;
        m_steeringOperatingRangeApplied = false;
        ForceStopAllFFB();
        m_isBumping = false;
        ResetFakeBumpTimer();
        OnG923ConnectionChanged?.Invoke(false);
        OnG923ResumeStateChanged?.Invoke(EG923ResumeState.Disconnected);
        if (m_pauseGameOnDisconnect && !m_pausedByG923Disconnect)
        {
            m_timeScaleBeforeG923Pause = Time.timeScale;
            Time.timeScale = 0.0f;
            m_pausedByG923Disconnect = true;
            G923Warning("[G923 FFB] G923切断によりゲームを一時停止しました。");
        }
    }

    // 復帰処理
    private void EnableResumeAction()
    {
        if (m_resumeAction == null)
        {
            return;
        }

        if (m_resumeAction.action == null)
        {
            return;
        }

        if (!m_resumeAction.action.enabled)
        {
            m_resumeAction.action.Enable();
        }
    }

    private void DisableResumeAction()
    {
        if (m_resumeAction == null)
        {
            return;
        }

        if (m_resumeAction.action == null)
        {
            return;
        }

        if (m_resumeAction.action.enabled)
        {
            m_resumeAction.action.Disable();
        }
    }

    private bool IsAllowedResumeControl(InputControl control)
    {
        if (control == null)
        {
            return false;
        }

        InputDevice device = control.device;
        if (IsG923Device(device))
        {
            return true;
        }

        if (m_allowKeyboardF5Resume && IsKeyboardF5Control(control))
        {
            return true;
        }

        return false;
    }

    private bool IsG923Device(InputDevice device)
    {
        if (device == null)
        {
            return false;
        }

        string displayName = device.displayName ?? string.Empty;
        string deviceName = device.name ?? string.Empty;
        return displayName.Contains("G923") || displayName.Contains("Racing Wheel") || deviceName.Contains("G923") || deviceName.Contains("Racing");
    }

    private bool IsKeyboardF5Control(InputControl control)
    {
        if (control == null)
        {
            return false;
        }

        if (!(control.device is Keyboard))
        {
            return false;
        }

        return control.path == "/Keyboard/f5";
    }

    private bool WasResumeActionPressedThisFrame()
    {
        if (m_resumeAction == null || m_resumeAction.action == null)
        {
            G923Warning("[G923 FFB] ResumeAction が未設定です。Inspectorを確認してください。");
            return false;
        }

        if (!m_resumeAction.action.enabled)
        {
            m_resumeAction.action.Enable();
        }

        foreach (InputControl control in m_resumeAction.action.controls)
        {
            if (!IsAllowedResumeControl(control))
            {
                continue;
            }

            if (control is ButtonControl buttonControl && buttonControl.wasPressedThisFrame)
            {
                return true;
            }
        }

        return false;
    }

    private void OnResumeActionPerformed(InputAction.CallbackContext context)
    {
        if (!context.performed)
        {
            return;
        }

        if (!m_waitingForFunction5Resume)
        {
            return;
        }

        if (!IsAllowedResumeControl(context.control))
        {
            return;
        }

        ResumeFromG923Disconnect();
    }

    public void ResumeFromG923Disconnect()
    {
        if (!CanCallLogitechSDK())
        {
            return;
        }

        if (!m_pausedByG923Disconnect && !m_waitingForFunction5Resume)
        {
            return;
        }

        LogitechGSDK.LogiUpdate();
        if (!LogitechGSDK.LogiIsConnected(m_deviceIndex))
        {
            G923Warning("[G923 FFB] G923が未接続のため復帰できません。");
            return;
        }

        if (m_timeScaleBeforeG923Pause <= 0.0f)
        {
            Time.timeScale = 1.0f;
        }
        else
        {
            Time.timeScale = m_timeScaleBeforeG923Pause;
        }

        m_pausedByG923Disconnect = false;
        m_waitingForFunction5Resume = false;
        m_hasLoggedWaitingForFunction5 = false;
        ForceStopAllFFB();
        ResetFakeBumpTimer();
        OnG923ResumeStateChanged?.Invoke(EG923ResumeState.Connected);
        if (m_resumeInputCoroutine != null)
        {
            StopCoroutine(m_resumeInputCoroutine);
        }

        m_resumeInputCoroutine = StartCoroutine(ResumeInputAfterReconnectCoroutine());
    }

    private IEnumerator ResumeInputAfterReconnectCoroutine()
    {
        yield return new WaitForSecondsRealtime(m_inputRefreshDelayAfterReconnect);
        if (m_reinitializeLogitechSDKOnResume)
        {
            ReinitializeLogitechSDKForG923();
        }

        yield return null;
        if (m_refreshInputDevicesOnResume)
        {
            RefreshInputSystemDevicesAfterReconnect();
        }

        yield return null;
        ReactivatePlayerInput();
        m_resumeInputCoroutine = null;
    }

    private void ReinitializeLogitechSDKForG923()
    {
        if (GameManager.Instance == null)
        {
            G923Warning("[G923 FFB] GameManager.Instance が見つからないため、Logitech SDK再初期化を実行できません。");
            return;
        }

        bool result = GameManager.Instance.ForceReinitializeLogitech("G923_FFBController Resume");
        if (result)
        {
            m_steeringOperatingRangeApplied = false;
            ApplySteeringOperatingRange();
        }
    }

    private void ApplySteeringOperatingRange()
    {
        if (m_steeringOperatingRangeApplied)
        {
            return;
        }

        LogitechGSDK.LogiControllerPropertiesData properties = new LogitechGSDK.LogiControllerPropertiesData();
        if (!LogitechGSDK.LogiGetCurrentControllerProperties(m_deviceIndex, ref properties))
        {
            return;
        }

        properties.wheelRange = Mathf.Clamp(m_steeringOperatingRange, 90, 900);
        m_steeringOperatingRangeApplied = LogitechGSDK.LogiSetPreferredControllerProperties(properties);
        if (m_steeringOperatingRangeApplied)
        {
        }
    }

    private void ReactivatePlayerInput()
    {
        if (!m_reactivatePlayerInputOnResume)
        {
            G923Warning("[G923 FFB] Reactivate Player Input On Resume がOFFです。");
            return;
        }

        if (m_playerInput == null)
        {
#if UNITY_2023_1_OR_NEWER
            m_playerInput = FindFirstObjectByType<PlayerInput>();
#else
            m_playerInput = FindObjectOfType<PlayerInput>();
#endif
        }

        if (m_inputActionsAsset == null && m_playerInput != null)
        {
            m_inputActionsAsset = m_playerInput.actions;
        }

        if (m_inputActionsAsset == null)
        {
            G923Warning("[G923 FFB] InputActionAssetが見つかりません。Inspectorの Input Actions Asset に Aim2024Input を入れてください。");
        }
        else
        {
            m_inputActionsAsset.Disable();
            InputSystem.Update();
            m_inputActionsAsset.Enable();
            InputSystem.Update();
            InputActionMap systemMap = m_inputActionsAsset.FindActionMap(m_systemActionMapName, false);
            if (systemMap != null)
            {
                systemMap.Enable();
            }
            else
            {
                G923Warning("[G923 FFB] System ActionMapが見つかりません : " + m_systemActionMapName);
            }

            InputActionMap gameplayMap = m_inputActionsAsset.FindActionMap(m_gameplayActionMapName, false);
            if (gameplayMap != null)
            {
                gameplayMap.Enable();
            }
            else
            {
                G923Warning("[G923 FFB] Gameplay ActionMapが見つかりません : " + m_gameplayActionMapName);
            }
        }

        if (m_playerInput != null)
        {
            m_playerInput.DeactivateInput();
            InputSystem.Update();
            m_playerInput.ActivateInput();
            InputSystem.Update();
            if (m_switchToGameplayActionMapOnResume)
            {
                try
                {
                    m_playerInput.SwitchCurrentActionMap(m_gameplayActionMapName);
                }
                catch
                {
                    G923Warning("[G923 FFB] PlayerInputのActionMap切り替えに失敗しました : " + m_gameplayActionMapName);
                }
            }

            if (m_playerInput.currentActionMap != null)
            {
            }

            RePairCurrentG923ToPlayerInput();
        }
        else
        {
            G923Warning("[G923 FFB] PlayerInputが見つかりませんでした。Project-wide Actionsのみ再有効化しました。");
        }

        EnableResumeAction();
    }

    // Input System デバイス管理
    private void OnInputDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (!IsG923OrJoystickDevice(device))
        {
            return;
        }
    }

    private bool IsG923OrJoystickDevice(InputDevice device)
    {
        if (device == null)
        {
            return false;
        }

        string displayName = device.displayName ?? string.Empty;
        string deviceName = device.name ?? string.Empty;
        string layoutName = device.layout ?? string.Empty;
        string deviceText = (displayName + " " + deviceName + " " + layoutName).ToLowerInvariant();
        return deviceText.Contains("g923") || deviceText.Contains("racing wheel") || deviceText.Contains("logitech") || deviceText.Contains("logicool");
    }

    private void RefreshInputSystemDevicesAfterReconnect()
    {
        if (!m_refreshInputDevicesOnResume)
        {
            return;
        }

        InputSystem.Update();
        bool foundTargetDevice = false;
        foreach (InputDevice device in InputSystem.devices)
        {
            if (!IsG923OrJoystickDevice(device))
            {
                continue;
            }

            foundTargetDevice = true;
            if (!device.enabled)
            {
                InputSystem.EnableDevice(device);
            }

            if (m_resetInputSystemDevicesOnResume)
            {
                InputSystem.ResetDevice(device, true);
            }
        }

        InputSystem.Update();
        if (!foundTargetDevice)
        {
            G923Warning("[G923 FFB] 再同期対象のG923デバイスが見つかりませんでした。");
        }
    }

    private void RePairCurrentG923ToPlayerInput()
    {
        if (m_playerInput == null)
        {
            G923Warning("[G923 FFB] PlayerInputがないため、G923再ペアリングをスキップします。");
            return;
        }

        InputDevice targetDevice = null;
        foreach (InputDevice device in InputSystem.devices)
        {
            if (!IsG923OrJoystickDevice(device))
            {
                continue;
            }

            targetDevice = device;
            break;
        }

        if (targetDevice == null)
        {
            G923Warning("[G923 FFB] PlayerInputへ再ペアリングするG923が見つかりません。");
            return;
        }

        try
        {
            m_playerInput.user.UnpairDevices();
            InputUser.PerformPairingWithDevice(targetDevice, m_playerInput.user);
            if (Keyboard.current != null)
            {
                InputUser.PerformPairingWithDevice(Keyboard.current, m_playerInput.user);
            }

            m_playerInput.ActivateInput();
            if (m_switchToGameplayActionMapOnResume)
            {
                m_playerInput.SwitchCurrentActionMap(m_gameplayActionMapName);
            }
        }
        catch (Exception e)
        {
            AppLog.LogError("[G923 FFB] PlayerInputのG923再ペアリング中に例外 : " + e.Message);
        }
    }

    // メニュー・非走行中FFB
    private void ApplyMenuFFB()
    {
        FFBModeSettings profile = GetCurrentFFBModeSettings();
        if (profile == null || !profile.enableMenuFFB)
        {
            ForceStopAllFFB();
            return;
        }

        if (m_crashActiveTimer <= 0.0f)
        {
            LogitechGSDK.LogiStopConstantForce(m_deviceIndex);
        }

        StopFakeBump();
        float gain = GetEffectiveMasterGain(profile);
        int saturation = Mathf.Clamp(Mathf.RoundToInt(profile.menuSpringSaturation * gain), 0, 100);
        int coefficient = Mathf.Clamp(Mathf.RoundToInt(profile.menuSpringCoefficient * gain), 0, 100);
        int damper = Mathf.Clamp(Mathf.RoundToInt(profile.menuDamperForce * gain), 0, 100);
        LogitechGSDK.LogiPlaySpringForce(m_deviceIndex, 0, saturation, coefficient);
        LogitechGSDK.LogiPlayDamperForce(m_deviceIndex, damper);
        // メニューでは路面凹凸は出さず、必要なら弱い左右揺れだけ残す。
        UpdateSpeedShakeFFB(0.0f, profile);
    }

    // 通常FFB反映
    private void ApplyLowSpeedFFB(FFBModeSettings profile)
    {
        if (profile == null)
        {
            return;
        }

        if (m_crashActiveTimer <= 0.0f)
        {
            LogitechGSDK.LogiStopConstantForce(m_deviceIndex);
        }

        float gain = GetEffectiveMasterGain(profile);
        int saturation = Mathf.Clamp(Mathf.RoundToInt(profile.lowSpeedSpringSaturation * gain), 0, 100);
        int coefficient = Mathf.Clamp(Mathf.RoundToInt(profile.lowSpeedSpringCoefficient * gain), 0, 100);
        int damper = Mathf.Clamp(Mathf.RoundToInt(profile.lowSpeedDamperForce * gain), 0, 100);
        LogitechGSDK.LogiPlaySpringForce(m_deviceIndex, 0, saturation, coefficient);
        LogitechGSDK.LogiPlayDamperForce(m_deviceIndex, damper);
    }

    private float GetStableSpeedForFFB(float rawSpeed, FFBModeSettings profile)
    {
        rawSpeed = Mathf.Max(0.0f, rawSpeed);
        if (profile == null || !profile.smoothSpeedForFFB)
        {
            return rawSpeed;
        }

        if (!m_hasSmoothedSpeedForFFB)
        {
            m_smoothedSpeedForFFB = rawSpeed;
            m_hasSmoothedSpeedForFFB = true;
            return rawSpeed;
        }

        float rate = rawSpeed >= m_smoothedSpeedForFFB ? profile.ffbSpeedRiseRate : profile.ffbSpeedDropRate;
        m_smoothedSpeedForFFB = Mathf.MoveTowards(m_smoothedSpeedForFFB, rawSpeed, rate * Time.unscaledDeltaTime);
        return m_smoothedSpeedForFFB;
    }

    private void ApplyDrivingFFB(float speed, float brakeForce, FFBModeSettings profile)
    {
        if (profile == null)
        {
            return;
        }

        if (m_crashActiveTimer <= 0.0f)
        {
            LogitechGSDK.LogiStopConstantForce(m_deviceIndex);
        }

        float gain = GetEffectiveMasterGain(profile);
        float rawSpeedRate = Mathf.Clamp01(speed / Mathf.Max(1.0f, profile.springSpeedReference));
        float speedRate = Mathf.Pow(rawSpeedRate, Mathf.Max(0.1f, profile.springSpeedCurvePower));
        int springSaturation = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(profile.minSpringSaturation, profile.maxSpringSaturation, speedRate)), 0, 100);
        int springCoefficient = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(profile.minSpringCoefficient, profile.maxSpringCoefficient, speedRate)), 0, 100);
        springSaturation = Mathf.Clamp(Mathf.RoundToInt(springSaturation * profile.springOutputMultiplier * gain), 0, 100);
        springCoefficient = Mathf.Clamp(Mathf.RoundToInt(springCoefficient * profile.springOutputMultiplier * gain), 0, 100);
        LogitechGSDK.LogiPlaySpringForce(m_deviceIndex, 0, springSaturation, springCoefficient);
        float damperSpeedRate = Mathf.Pow(rawSpeedRate, Mathf.Max(0.1f, profile.damperSpeedCurvePower));
        int damperForce = Mathf.Clamp(Mathf.RoundToInt((profile.baseDamperForce + (speed * profile.speedDamperPower * damperSpeedRate) + (brakeForce * profile.brakeDamperPower)) * profile.damperOutputMultiplier), 0, profile.maxDamperForce);
        damperForce = Mathf.Max(damperForce, profile.minDrivingDamperForce);
        damperForce = Mathf.Clamp(Mathf.RoundToInt(damperForce * gain), 0, 100);
        LogitechGSDK.LogiPlayDamperForce(m_deviceIndex, damperForce);
    }

    private void StopSpeedShakeFFB()
    {
        if (!CanCallLogitechSDK())
        {
            return;
        }

        LogitechGSDK.LogiStopConstantForce(m_deviceIndex);
        m_speedShakeTimer = 0.0f;
    }

    private void UpdateSpeedShakeFFB(float speed, FFBModeSettings profile)
    {
        if (m_crashActiveTimer > 0.0f)
        {
            return;
        }

        if (profile == null || !profile.enableSpeedShakeFFB)
        {
            StopSpeedShakeFFB();
            return;
        }

        float gain = GetEffectiveMasterGain(profile);
        float rawSpeedRate = Mathf.Clamp01(speed / Mathf.Max(1.0f, profile.speedShakeReferenceSpeed));
        float shakeRate = Mathf.Pow(rawSpeedRate, Mathf.Max(0.1f, profile.speedShakeCurvePower));
        float movingMultiplier = GetMovingShakeMultiplier(speed, profile);
        float highSpeedRemainMultiplier = GetSpeedShakeRemainMultiplier(speed, profile);
        float rawShakeMagnitude = Mathf.Lerp(profile.speedShakeMinForce, profile.speedShakeMaxForce, shakeRate) * profile.speedShakeOutputMultiplier * movingMultiplier * highSpeedRemainMultiplier * gain;
        int shakeMagnitude = Mathf.Clamp(Mathf.RoundToInt(rawShakeMagnitude), 0, 100);
        if (shakeMagnitude <= 0 && rawShakeMagnitude > 0.0f)
        {
            shakeMagnitude = 1;
        }

        if (shakeMagnitude <= 0)
        {
            StopSpeedShakeFFB();
            return;
        }

        float stabilityRate = GetHighSpeedStabilityRate(speed, profile);
        float shakeFrequency = Mathf.Lerp(profile.speedShakeMinFrequency, profile.speedShakeMaxFrequency, shakeRate);
        shakeFrequency = Mathf.Lerp(shakeFrequency, profile.speedShakeMinFrequency, stabilityRate);
        m_speedShakeTimer += Time.unscaledDeltaTime * shakeFrequency * Mathf.PI * 2.0f;
        if (m_speedShakeTimer > Mathf.PI * 2.0f)
        {
            m_speedShakeTimer -= Mathf.PI * 2.0f;
        }

        int outputForce = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sin(m_speedShakeTimer) * shakeMagnitude), -100, 100);
        LogitechGSDK.LogiPlayConstantForce(m_deviceIndex, outputForce);
    }

    // 仮の路面凹凸
    private void UpdateFakeBumps(float speed, FFBModeSettings profile)
    {
        if (profile == null || !profile.enableFakeBump)
        {
            StopFakeBump();
            return;
        }

        float gain = GetEffectiveMasterGain(profile);
        float bumpFrequencyRate = Mathf.Clamp01(speed / 130.0f);
        bumpFrequencyRate = Mathf.Pow(bumpFrequencyRate, Mathf.Max(0.1f, profile.bumpFrequencySpeedCurvePower));
        float speedMultiplier = Mathf.Lerp(profile.bumpMinFrequencyMultiplier, profile.bumpMaxFrequencyMultiplier, bumpFrequencyRate);
        speedMultiplier *= GetBumpFrequencyRemainMultiplier(speed, profile);
        speedMultiplier *= Mathf.Max(0.05f, GetMovingShakeMultiplier(speed, profile));
        if (m_isBumping)
        {
            m_bumpActiveTimer -= Time.deltaTime;
            if (m_bumpActiveTimer <= 0.0f)
            {
                StopFakeBump();
                m_bumpWaitTimer = UnityEngine.Random.Range(profile.bumpIntervalMin, profile.bumpIntervalMax);
            }
        }
        else
        {
            m_bumpWaitTimer -= Time.deltaTime * speedMultiplier;
            if (m_bumpWaitTimer <= 0.0f)
            {
                float bumpSpeedRate = Mathf.Clamp01(speed / 100.0f);
                bumpSpeedRate = Mathf.Pow(bumpSpeedRate, Mathf.Max(0.1f, profile.bumpSpeedCurvePower));
                float magnitudeFactor = Mathf.Lerp(profile.bumpMinSpeedRate, 1.0f, bumpSpeedRate);
                magnitudeFactor *= GetMovingShakeMultiplier(speed, profile);
                magnitudeFactor *= GetBumpRemainMultiplier(speed, profile);
                int baseMagnitude = UnityEngine.Random.Range(profile.fakeBumpMagnitudeMin, profile.fakeBumpMagnitudeMax + 1);
                float rawBumpMagnitude = baseMagnitude * magnitudeFactor * profile.bumpOutputMultiplier * gain;
                int finalMagnitude = Mathf.Clamp(Mathf.RoundToInt(rawBumpMagnitude), 0, 100);
                if (finalMagnitude <= 0 && rawBumpMagnitude > 0.0f)
                {
                    finalMagnitude = 1;
                }

                if (finalMagnitude <= 0)
                {
                    ResetFakeBumpTimer();
                    return;
                }

                int effectType = UnityEngine.Random.Range(0, 3);
                switch (effectType)
                {
                    case 0:
                        LogitechGSDK.LogiPlayBumpyRoadEffect(m_deviceIndex, finalMagnitude);
                        break;
                    case 1:
                        LogitechGSDK.LogiPlayDirtRoadEffect(m_deviceIndex, finalMagnitude);
                        break;
                    case 2:
                        int period = Mathf.RoundToInt(Mathf.Lerp(140, 70, bumpSpeedRate));
                        LogitechGSDK.LogiPlaySurfaceEffect(m_deviceIndex, 1, finalMagnitude, period);
                        break;
                }

                m_isBumping = true;
                float stabilityRate = GetHighSpeedStabilityRate(speed, profile);
                float durationFactor = Mathf.Lerp(1.2f, 0.55f, stabilityRate);
                m_bumpActiveTimer = profile.bumpDuration * UnityEngine.Random.Range(0.8f, 1.2f) * durationFactor;
            }
        }
    }

    private void StopFakeBump()
    {
        if (!CanCallLogitechSDK())
        {
            return;
        }

        if (!m_isBumping)
        {
            return;
        }

        LogitechGSDK.LogiStopBumpyRoadEffect(m_deviceIndex);
        LogitechGSDK.LogiStopDirtRoadEffect(m_deviceIndex);
        LogitechGSDK.LogiStopSurfaceEffect(m_deviceIndex);
        m_isBumping = false;
    }

    private void ResetFakeBumpTimer()
    {
        FFBModeSettings profile = GetCurrentFFBModeSettings();
        if (profile != null)
        {
            m_bumpWaitTimer = UnityEngine.Random.Range(profile.bumpIntervalMin, profile.bumpIntervalMax);
        }
        else
        {
            m_bumpWaitTimer = 1.0f;
        }

        m_bumpActiveTimer = 0.0f;
    }

    // 物理衝突FFB
    private void OnCollisionEnter(Collision collision)
    {
        // Vehicleに直接付けたままでも一応動くように残しておく
        NotifyVehicleCollision(collision, transform);
    }

    public void NotifyVehicleCollision(Collision collision, Transform vehicleTransform)
    {
        if (!CanUseFFB())
        {
            return;
        }

        FFBModeSettings profile = GetCurrentFFBModeSettings();
        if (profile == null || !profile.enableCrashFFB)
        {
            return;
        }

        if (!LogitechGSDK.LogiIsConnected(m_deviceIndex))
        {
            return;
        }

        if (collision == null || collision.contactCount <= 0)
        {
            return;
        }

        if (vehicleTransform == null)
        {
            return;
        }

        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed < profile.minCrashImpactSpeed)
        {
            return;
        }

        float impactIntensity = Mathf.Clamp01(impactSpeed / 8.0f);
        Vector3 contactPoint = collision.GetContact(0).point;
        Vector3 localContact = vehicleTransform.InverseTransformPoint(contactPoint);
        bool isHitRightSide = localContact.x > 0.0f;
        PlayWallCrashFFB(isHitRightSide, impactIntensity);
    }

    public void PlayWallCrashFFB(bool isHitRightSide, float impactIntensity)
    {
        if (!CanUseFFB())
        {
            return;
        }

        FFBModeSettings profile = GetCurrentFFBModeSettings();
        if (profile == null || !profile.enableCrashFFB)
        {
            return;
        }

        m_crashActiveTimer = profile.crashDuration;
        int baseDirection = isHitRightSide ? -1 : 1;
        if (profile.invertCrashForce)
        {
            baseDirection *= -1;
        }

        int safeMinCrashForce = Mathf.Min(profile.minCrashForce, profile.maxCrashForce);
        int forceMagnitude = Mathf.Clamp(Mathf.RoundToInt(profile.maxCrashForce * Mathf.Clamp01(impactIntensity)), safeMinCrashForce, profile.maxCrashForce);
        m_currentCrashForce = forceMagnitude * baseDirection;
    }

    private void UpdateCrashFFB(FFBModeSettings profile)
    {
        if (m_crashActiveTimer <= 0.0f)
        {
            return;
        }

        if (profile == null)
        {
            return;
        }

        m_crashActiveTimer -= Time.deltaTime;
        if (m_crashActiveTimer > 0.0f)
        {
            float remainRate = Mathf.Clamp01(m_crashActiveTimer / Mathf.Max(0.01f, profile.crashDuration));
            float gain = GetEffectiveMasterGain(profile);
            int outputForce = Mathf.RoundToInt(m_currentCrashForce * remainRate * gain);
            outputForce = Mathf.Clamp(outputForce, -100, 100);
            LogitechGSDK.LogiPlayConstantForce(m_deviceIndex, outputForce);
        }
        else
        {
            m_crashActiveTimer = 0.0f;
            m_currentCrashForce = 0;
            LogitechGSDK.LogiStopConstantForce(m_deviceIndex);
        }
    }

    // 安全停止
    public void StopAllFFB()
    {
        if (!CanCallLogitechSDK())
        {
            return;
        }

        ForceStopAllFFB();
    }

    private void ForceStopAllFFB()
    {
        if (!CanCallLogitechSDK())
        {
            return;
        }

        LogitechGSDK.LogiStopConstantForce(m_deviceIndex);
        LogitechGSDK.LogiStopSpringForce(m_deviceIndex);
        LogitechGSDK.LogiStopDamperForce(m_deviceIndex);
        LogitechGSDK.LogiStopBumpyRoadEffect(m_deviceIndex);
        LogitechGSDK.LogiStopDirtRoadEffect(m_deviceIndex);
        LogitechGSDK.LogiStopSlipperyRoadEffect(m_deviceIndex);
        LogitechGSDK.LogiStopSurfaceEffect(m_deviceIndex);
        LogitechGSDK.LogiStopCarAirborne(m_deviceIndex);
        LogitechGSDK.LogiStopSoftstopForce(m_deviceIndex);
        m_isBumping = false;
        m_crashActiveTimer = 0.0f;
        m_currentCrashForce = 0;
        m_speedShakeTimer = 0.0f;
        ResetFakeBumpTimer();
    }

    private void OnDisable()
    {
        if (m_resumeAction != null && m_resumeAction.action != null)
        {
            m_resumeAction.action.performed -= OnResumeActionPerformed;
        }

        InputSystem.onDeviceChange -= OnInputDeviceChange;
        if (m_resumeInputCoroutine != null)
        {
            StopCoroutine(m_resumeInputCoroutine);
            m_resumeInputCoroutine = null;
        }

        if (m_isPrimaryInstance)
        {
            StopAllFFB();
            DisableResumeAction();
        }
    }

    private void OnDestroy()
    {
        if (m_isPrimaryInstance)
        {
            StopAllFFB();
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
