using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GameManager : SingletonMonoBehaviour<GameManager>
{
    // 計測タイム
    [SerializeField, ShowInInspector]
    string m_resultTime;
    [SerializeField, ShowInInspector]
    List<string> m_lapTimes = new List<string>(); // Resultへ引き継ぐ各ラップタイム
    // WIZMO
    [SerializeField]
    WIZMOController m_WIZMOController;
    [SerializeField]
    WIZMODebug m_WIZMODebug;
    // 運転設定
    [SerializeField, ShowInInspector]
    DrivingSettings m_settings;
    float m_steerInput;
    // G923 / Logitech SDK管理
    [Header("G923 / Logitech SDK管理")]
    [SerializeField, Tooltip("G923_FFBControllerへの参照。未設定なら自動検索する")]
    private G923_FFBController m_g923FFBController;
    [SerializeField, Tooltip("Editor上でもLogitech SDKを初期化するか")]
    private bool m_enableLogitechSDKInEditor = true;
    [SerializeField, Tooltip("Start時の初期化失敗後にリトライする")]
    private bool m_retryInitializeLogitechOnStart = true;
    [SerializeField, Range(0, 20), Tooltip("Start時の初期化リトライ回数")]
    private int m_logitechInitializeRetryCount = 5;
    [SerializeField, Min(0.1f), Tooltip("Start時の初期化リトライ間隔 秒")]
    private float m_logitechInitializeRetryInterval = 1.0f;
    [SerializeField, Tooltip("Input System上でG923/Joystick再接続を検知した時にSDK再初期化を要求する")]
    private bool m_reinitializeLogitechOnInputDeviceReconnect = true;
    [SerializeField, Min(0.0f), Tooltip("再接続検知後、SDK再初期化まで待つ時間 秒")]
    private float m_logitechReinitializeDelay = 0.5f;
    private bool m_isLogitechInitialized = false;
    private bool m_isLogitechShutdown = false;
    private Coroutine m_logitechInitializeRetryCoroutine;
    private Coroutine m_logitechReinitializeCoroutine;
#region プロパティ
    public GameStateId CurrentGameState => GameFlowRunner.Instance.CurrentId; // 新遷移エンジンへ完全移譲(翻訳レイヤー無し)
    public string ResultTime { get => m_resultTime; set => m_resultTime = value; }

    public IReadOnlyList<string> LapTimes
    {
        get
        {
            return m_lapTimes;
        }
    }

    public void SetLapTimes(IReadOnlyList<string> _laptimes)
    {
        m_lapTimes.Clear();
        if (_laptimes == null)
        {
            return;
        }

        for (int i = 0; i < _laptimes.Count; ++i)
        {
            m_lapTimes.Add(_laptimes[i]);
        }
    }

    public DrivingSettings DrivingSettings { get => m_settings; set => m_settings = value; }
    public bool ShowStateWIZMO { get; set; }
    public bool ShowMouce { get; set; }
    public WIZMOController WIZMO => m_WIZMOController;
    public bool IsLogitechInitialized => m_isLogitechInitialized;

#endregion
    // 初期化
    protected override void Awake()
    {
        base.Awake();
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
    }

    private void OnEnable()
    {
        InputSystem.onDeviceChange += OnInputDeviceChange;
    }

    private void Start()
    {
        Cursor.visible = false;
        m_settings.isAT = true;
        bool result = InitializeLogitech();
        if (!result && m_retryInitializeLogitechOnStart)
        {
            if (m_logitechInitializeRetryCoroutine != null)
            {
                StopCoroutine(m_logitechInitializeRetryCoroutine);
            }

            m_logitechInitializeRetryCoroutine = StartCoroutine(LogitechInitializeRetryCoroutine("Start初期化失敗"));
        }
    }

    private void OnDisable()
    {
        InputSystem.onDeviceChange -= OnInputDeviceChange;
    }

    // 終了処理
    private void OnDestroy()
    {
        ShutdownLogitech();
    }

    private void OnApplicationQuit()
    {
        ShutdownLogitech();
    }

    // 更新処理
    void Update()
    {
        Cursor.visible = ShowMouce;
    }

    // 入力
    void UpdateSteerInput(InputAction.CallbackContext _context)
    {
        m_steerInput = _context.ReadValue<float>();
    }

    // G923 / Logitech SDK管理
    private bool CanUseLogitechSDK()
    {
#if UNITY_EDITOR
        return m_enableLogitechSDKInEditor;
#else
        return true;
#endif
    }

    private bool InitializeLogitech()
    {
        if (!CanUseLogitechSDK())
        {
            return false;
        }

        if (m_isLogitechInitialized)
        {
            return true;
        }

        m_isLogitechShutdown = false;
        bool result = false;
        try
        {
            result = LogitechGSDK.LogiSteeringInitialize(false);
        }
        catch (Exception e)
        {
            AppLog.LogError("[G923] Logitech SDK初期化中に例外 : " + e.Message);
            m_isLogitechInitialized = false;
            return false;
        }

        m_isLogitechInitialized = result;
        if (!m_isLogitechInitialized)
        {
            AppLog.LogWarning("[G923] Logitech SDKの初期化に失敗しました。G HUB、接続状態、SDK配置を確認してください。");
            return false;
        }

        bool updateResult = LogitechGSDK.LogiUpdate();
        bool connected = LogitechGSDK.LogiIsConnected(0);
        return true;
    }

    private IEnumerator LogitechInitializeRetryCoroutine(string reason)
    {
        for (int i = 0; i < m_logitechInitializeRetryCount; i++)
        {
            if (m_isLogitechInitialized)
            {
                break;
            }

            yield return new WaitForSecondsRealtime(m_logitechInitializeRetryInterval);
            InitializeLogitech();
        }

        if (!m_isLogitechInitialized)
        {
            AppLog.LogWarning("[G923] Logitech SDK初期化リトライ後も失敗しています。EditorではなくBuildでも確認してください。");
        }

        m_logitechInitializeRetryCoroutine = null;
    }

    public void RequestLogitechReinitialize(string reason)
    {
        if (!CanUseLogitechSDK())
        {
            return;
        }

        if (m_logitechReinitializeCoroutine != null)
        {
            StopCoroutine(m_logitechReinitializeCoroutine);
        }

        m_logitechReinitializeCoroutine = StartCoroutine(LogitechReinitializeDelayedCoroutine(reason));
    }

    private IEnumerator LogitechReinitializeDelayedCoroutine(string reason)
    {
        yield return new WaitForSecondsRealtime(m_logitechReinitializeDelay);
        ForceReinitializeLogitech(reason);
        m_logitechReinitializeCoroutine = null;
    }

    public bool ForceReinitializeLogitech(string reason)
    {
        if (!CanUseLogitechSDK())
        {
            AppLog.LogWarning("[G923] Logitech SDKを使用しない設定のため、強制再初期化しません。");
            return false;
        }

        try
        {
            StopG923FFB();
            // 初期化済みでなくても、一度Shutdownを挟むことでSDK側の状態を整理する
            LogitechGSDK.LogiSteeringShutdown();
            m_isLogitechInitialized = false;
            m_isLogitechShutdown = false;
            InputSystem.Update();
            bool result = InitializeLogitech();
            if (!result)
            {
                AppLog.LogWarning("[G923] Logitech SDKの強制再初期化に失敗しました。");
                return false;
            }

            LogitechGSDK.LogiUpdate();
            bool connected = LogitechGSDK.LogiIsConnected(0);
            return connected;
        }
        catch (Exception e)
        {
            AppLog.LogError("[G923] Logitech SDK強制再初期化中に例外 : " + e.Message);
            m_isLogitechInitialized = false;
            return false;
        }
    }

    public void StopG923FFB()
    {
        if (!CanUseLogitechSDK())
        {
            return;
        }

        if (m_g923FFBController == null)
        {
            m_g923FFBController = FindFirstObjectByType<G923_FFBController>();
        }

        if (m_g923FFBController != null)
        {
            m_g923FFBController.StopAllFFB();
        }
    }

    private void ShutdownLogitech()
    {
        if (!CanUseLogitechSDK())
        {
            return;
        }

        if (m_isLogitechShutdown)
        {
            return;
        }

        if (!m_isLogitechInitialized)
        {
            return;
        }

        StopG923FFB();
        try
        {
            LogitechGSDK.LogiSteeringShutdown();
        }
        catch (Exception e)
        {
            AppLog.LogError("[G923] Logitech SDK Shutdown中に例外 : " + e.Message);
        }

        m_isLogitechInitialized = false;
        m_isLogitechShutdown = true;
    }

    // Input System Device監視
    private void OnInputDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (!IsG923OrJoystickDevice(device))
        {
            return;
        }

        string changeName = change.ToString();
        if (!m_reinitializeLogitechOnInputDeviceReconnect)
        {
            return;
        }

        if (changeName == "Added" || changeName == "Reconnected" || changeName == "ConfigurationChanged" || changeName == "Enabled")
        {
            RequestLogitechReinitialize("InputDeviceChange:" + changeName);
        }

        if (changeName == "Disconnected" || changeName == "Removed" || changeName == "Disabled")
        {
            StopG923FFB();
        }
    }

    private bool IsG923OrJoystickDevice(InputDevice device)
    {
        if (device == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(device.displayName))
        {
            if (device.displayName.Contains("G923") || device.displayName.Contains("Racing Wheel") || device.displayName.Contains("Logitech") || device.displayName.Contains("Logicool"))
            {
                return true;
            }
        }

        if (!string.IsNullOrEmpty(device.name))
        {
            if (device.name.Contains("G923") || device.name.Contains("Racing") || device.name.Contains("Logitech") || device.name.Contains("Logicool"))
            {
                return true;
            }
        }

        if (device is Joystick)
        {
            return true;
        }

        return false;
    }
}
