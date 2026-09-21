using UnityEngine;
using UnityEngine.InputSystem;

// 入力機器の操作を車両制御へ渡すクラス
public class VehicleUnityEvent : MonoBehaviour
{
    [Header("Vehicle")]
    [SerializeField]
    private GameObject m_vehicle;
    private VehicleController m_vehicleController;
    // 切断時にクラッチの古い踏み込み量を破棄するための入力機器
    private InputDevice m_clutchInputDevice;
    // 踏み戻し通知の後も実際の軸値を読み切断状態が残ることを防ぐ参照
    private UnityEngine.InputSystem.Controls.AxisControl m_clutchAxis;
    // 無効な入力マップの軸値を車両へ渡さないためのクラッチアクション
    private InputAction m_clutchAction;
    // 踏んだ時だけ加速する機器ではクラッチ入力だけ反転して踏み込み量へ統一する設定
    [SerializeField, InspectorName("Invert Clutch Pedal")]
    private bool m_invertClutchPedal = true;
    // クラッチ機器の切断通知を購読する関数
    private void OnEnable()
    {
        InputSystem.onDeviceChange += OnClutchDeviceChanged;
    }

    // クラッチ機器が使えなくなった時に踏み込み済みの判定を消す関数
    private void OnClutchDeviceChanged(InputDevice device, InputDeviceChange change)
    {
        if (device != m_clutchInputDevice)
        {
            return;
        }

        if (change != InputDeviceChange.Removed && change != InputDeviceChange.Disconnected && change != InputDeviceChange.Disabled)
        {
            return;
        }

        if (m_vehicleController != null)
        {
            m_vehicleController.ClearPhysicalClutchPedal();
        }

        m_clutchInputDevice = null;
        m_clutchAxis = null;
        m_clutchAction = null;
    }

    private Transmission m_transmission;
    [SerializeField]
    private ChairController2024 Chair;
    [Header("Input Adjust")]
    [SerializeField, Range(1.0f, 4.0f)]
    private float m_accelInputPower = 2.0f;
    [SerializeField, Range(0.0f, 0.1f)]
    private float m_accelDeadZone = 0.02f;
    [SerializeField, Range(1.0f, 4.0f)]
    private float m_brakeInputPower = 1.0f;
    [SerializeField, Range(0.0f, 0.1f)]
    private float m_brakeDeadZone = 0.02f;
    // 未操作のハンドルやゲームパッドが出す微小な操舵値を除去するための値
    [SerializeField, Range(0.0f, 0.15f)]
    private float m_steeringDeadZone = 0.04f;
    [Header("Paddle Shift")]
    // MTからATへ切り替えるパドル長押し時間
    [SerializeField, Range(0.5f, 2.0f)]
    private float m_paddleAutomaticHoldSeconds = 1.0f;
    // 右パドルを押し続けている状態
    private bool m_shiftUpPaddleHeld;
    // 左パドルを押し続けている状態
    private bool m_shiftDownPaddleHeld;
    // 右パドルを押し始めた時刻
    private float m_shiftUpPaddlePressedAt;
    // 左パドルを押し始めた時刻
    private float m_shiftDownPaddlePressedAt;
    // 同じ長押し中にAT切替を一度だけ行うための状態
    private bool m_paddleAutomaticModeSelected;
    private void Awake()
    {
        ResolveVehicleReferences();
    }

    private void Start()
    {
        ResolveVehicleReferences();
    }

    // G923パドルの長押し時間を監視してMTからATへ切り替える関数
    private void Update()
    {
        if (m_transmission == null || m_vehicleController == null)
        {
            ResolveVehicleReferences();
        }

        // クラッチだけ現在値を読み直し踏み戻しイベントの欠落や軸中央のCanceledへ対応する
        RefreshPhysicalClutch();
        if (m_transmission == null || m_vehicleController == null || m_paddleAutomaticModeSelected)
        {
            return;
        }

        if (m_transmission.Type != Transmission.TransmissionType.Manual)
        {
            return;
        }

        bool heldLongEnough = m_shiftUpPaddleHeld && Time.unscaledTime - m_shiftUpPaddlePressedAt >= m_paddleAutomaticHoldSeconds;
        heldLongEnough |= m_shiftDownPaddleHeld && Time.unscaledTime - m_shiftDownPaddlePressedAt >= m_paddleAutomaticHoldSeconds;
        if (!heldLongEnough)
        {
            return;
        }

        m_vehicleController.SetPaddleTransmissionType(Transmission.TransmissionType.Automatic);
        m_paddleAutomaticModeSelected = true;
    }

    // 入力を渡す車両とトランスミッションを取得する関数
    private void ResolveVehicleReferences()
    {
        if (m_vehicle == null)
        {
            Debug.LogWarning("[VehicleUnityEvent] m_vehicle が設定されていません。");
            return;
        }

        if (m_vehicleController == null)
        {
            m_vehicleController = m_vehicle.GetComponent<VehicleController>();
        }

        if (m_vehicleController == null)
        {
            Debug.LogWarning("[VehicleUnityEvent] VehicleController が取得できません。");
            return;
        }

        if (m_transmission == null)
        {
            m_transmission = m_vehicleController.Transmission;
        }
    }

    // G923系の反転ペダル値を0から1へ変換する関数
    private float ConvertG923PedalValue(float rawValue)
    {
        float value = 1.0f - (rawValue + 1.0f) * 0.5f;
        return Mathf.Clamp01(value);
    }

    // アクセルとブレーキを変えずクラッチだけ機器の入力方向へ合わせる関数
    private float ConvertClutchPedalValue(float rawValue)
    {
        // 共通のペダル変換を変えると他のペダルまで反転するため専用の補正を行う
        float pedal = ConvertG923PedalValue(rawValue);
        return m_invertClutchPedal ? 1f - pedal : pedal;
    }

    // 変速とは独立してクラッチ軸の現在値を車両へ渡す関数
    private void RefreshPhysicalClutch()
    {
        if (m_vehicleController == null || m_clutchAxis == null)
        {
            return;
        }

        // 接続解除やアクション停止をペダルを離した操作と取り違えない
        if (m_clutchAction == null || !m_clutchAction.enabled || !m_clutchAxis.device.added || !m_clutchAxis.device.enabled)
        {
            m_vehicleController.ClearPhysicalClutchPedal();
            return;
        }

        float raw = m_clutchAxis.ReadValue();
        if (float.IsNaN(raw) || float.IsInfinity(raw))
        {
            m_vehicleController.ClearPhysicalClutchPedal();
            return;
        }

        m_vehicleController.SetPhysicalClutchPedal(ConvertClutchPedalValue(raw));
    }

    // シーンに登録された矢印キーとゲームパッドの操舵入力を既存の車両入力処理へ渡す関数
    public void OnHandleKeyboard(InputAction.CallbackContext context)
    {
        // 既存の入力補正とキーを離した時の中央復帰を共用して入力経路の違いによる操舵差を防ぐ
        OnHandle(context);
    }

    // 入力機器の操舵値を車両へ渡す関数
    public void OnHandle(InputAction.CallbackContext context)
    {
        if (m_vehicleController == null)
        {
            ResolveVehicleReferences();
        }

        if (m_vehicleController == null)
        {
            return;
        }

        // Valueアクションが解除された時は残留操舵を確実に中央へ戻す
        float rawValue = context.canceled ? 0.0f : context.ReadValue<float>();
        float value = Mathf.Abs(rawValue) < m_steeringDeadZone ? 0.0f : Mathf.Clamp(rawValue, -1.0f, 1.0f);
        bool isKeyboard = context.control != null && context.control.device is Keyboard;
        m_vehicleController.SetSteeringInput(value, isKeyboard);
    }

    // 入力コンポーネントが停止した時に前回の操舵値を残さないための関数
    private void OnDisable()
    {
        // 無効な入力コンポーネントへ切断通知が届かないよう購読を解除する
        InputSystem.onDeviceChange -= OnClutchDeviceChanged;
        m_clutchInputDevice = null;
        m_clutchAxis = null;
        m_clutchAction = null;
        // 入力停止後に古いクラッチ踏み込みでレバー変速を許可しない
        if (m_vehicleController != null)
        {
            m_vehicleController.ClearPhysicalClutchPedal();
        }

        if (m_vehicleController != null)
        {
            m_vehicleController.SetSteeringInput(0.0f, false);
        }

        m_shiftUpPaddleHeld = false;
        m_shiftDownPaddleHeld = false;
        m_paddleAutomaticModeSelected = false;
    }

    // キーボードとゲームパッドのアクセル入力を車両へ渡す関数
    public void OnAccel(InputAction.CallbackContext context)
    {
        if (m_vehicleController == null)
        {
            ResolveVehicleReferences();
        }

        if (m_vehicleController == null)
        {
            return;
        }

        if (context.canceled)
        {
            m_vehicleController.Accel = 0.0f;
            return;
        }

        float value = context.ReadValue<float>();
        value = Mathf.Clamp01(value);
        if (value < m_accelDeadZone)
        {
            value = 0.0f;
        }

        float outputAccel = Mathf.Pow(value, m_accelInputPower);
        m_vehicleController.Accel = outputAccel;
    }

    // G923のアクセルペダル入力を車両へ渡す関数
    public void OnAccelPedal(InputAction.CallbackContext context)
    {
        if (m_vehicleController == null)
        {
            ResolveVehicleReferences();
        }

        if (m_vehicleController == null)
        {
            return;
        }

        if (context.canceled)
        {
            m_vehicleController.Accel = 0.0f;
            return;
        }

        float rawValue = context.ReadValue<float>();
        float value = ConvertG923PedalValue(rawValue);
        if (value < m_accelDeadZone)
        {
            value = 0.0f;
        }

        float outputAccel = Mathf.Pow(value, m_accelInputPower);
        m_vehicleController.Accel = outputAccel;
    }

    // キーボードとゲームパッドのブレーキ入力を車両へ渡す関数
    public void OnBrake(InputAction.CallbackContext context)
    {
        if (m_vehicleController == null)
        {
            ResolveVehicleReferences();
        }

        if (m_vehicleController == null)
        {
            return;
        }

        if (context.canceled)
        {
            m_vehicleController.Brake = 0.0f;
            return;
        }

        float value = context.ReadValue<float>();
        value = Mathf.Clamp01(value);
        if (value < m_brakeDeadZone)
        {
            value = 0.0f;
        }

        float outputBrake = Mathf.Pow(value, m_brakeInputPower);
        m_vehicleController.Brake = outputBrake;
    }

    // G923のブレーキペダル入力を車両へ渡す関数
    public void OnBrakePedal(InputAction.CallbackContext context)
    {
        if (m_vehicleController == null)
        {
            ResolveVehicleReferences();
        }

        if (m_vehicleController == null)
        {
            return;
        }

        if (context.canceled)
        {
            m_vehicleController.Brake = 0.0f;
            return;
        }

        float rawValue = context.ReadValue<float>();
        float value = ConvertG923PedalValue(rawValue);
        if (value < m_brakeDeadZone)
        {
            value = 0.0f;
        }

        float outputBrake = Mathf.Pow(value, m_brakeInputPower);
        m_vehicleController.Brake = outputBrake;
    }

    // G923のクラッチペダル入力を車両へ渡す関数
    public void OnClutch(InputAction.CallbackContext context)
    {
        // 実クラッチを最後に送った機器の切断を監視する
        m_clutchInputDevice = context.control?.device;
        m_clutchAxis = context.control as UnityEngine.InputSystem.Controls.AxisControl;
        m_clutchAction = context.action;
        if (m_vehicleController == null)
        {
            ResolveVehicleReferences();
        }

        if (m_vehicleController == null)
        {
            return;
        }

        // 軸入力はCanceledでも現在値を読み完全な踏み戻しまで追従する
        if (m_clutchAxis != null)
        {
            RefreshPhysicalClutch();
            return;
        }

        if (context.canceled)
        {
            // G923の軸中央でもCanceledが出るため実軸値を読み半踏みを未操作にしない
            if (m_vehicleController.TrackLeverClutchRequired && context.control is UnityEngine.InputSystem.Controls.AxisControl clutchAxis)
            {
                m_vehicleController.SetPhysicalClutchPedal(ConvertClutchPedalValue(clutchAxis.ReadValue()));
            }
            else
            {
                m_vehicleController.SetPhysicalClutchPedal(0f);
            }

            return;
        }

        float rawValue = context.ReadValue<float>();
        float value = ConvertClutchPedalValue(rawValue);
        // 踏み込み量を接続率と混同せずTrackレバーの変速判定へ渡す
        m_vehicleController.SetPhysicalClutchPedal(value);
    }

    // シフトアップ操作をトランスミッションへ渡す関数
    public void OnShiftUp(InputAction.CallbackContext context)
    {
        if (m_transmission == null)
        {
            ResolveVehicleReferences();
        }

        if (m_transmission == null)
        {
            return;
        }

        bool isLogitechPaddle = IsLogitechPaddle(context, "button5");
        if (context.canceled)
        {
            if (isLogitechPaddle)
            {
                m_shiftUpPaddleHeld = false;
            }

            if (!m_shiftUpPaddleHeld && !m_shiftDownPaddleHeld)
            {
                m_paddleAutomaticModeSelected = false;
            }

            return;
        }

        // Press設定の押下確定で一度だけ変速しStarted通知の有無に依存させない
        if (!context.performed)
        {
            return;
        }

        // ATはクラッチ操作を要求せず停止中だけ走行方向を選択する
        if (m_transmission.Type == Transmission.TransmissionType.Automatic)
        {
            if (IsExternalLever(context))
            {
                m_vehicleController.RequestLeverShift(true);
            }
            else
            {
                m_vehicleController.RequestPaddleShift(true);
            }

            return;
        }

        if (isLogitechPaddle)
        {
            m_shiftUpPaddleHeld = true;
            m_shiftUpPaddlePressedAt = Time.unscaledTime;
            m_paddleAutomaticModeSelected = false;
        }

        // LTBだけ実クラッチ判定へ送りパドルとキーボードの従来操作を維持する
        if (IsExternalLever(context))
        {
            m_vehicleController.RequestLeverShift(true);
        }
        else
        {
            // パドルの変速許可をレバー用のクラッチ踏み込み判定から分離する
            m_vehicleController.RequestPaddleShift(true);
        }
    }

    // シフトダウン操作をトランスミッションへ渡す関数
    public void OnShiftDown(InputAction.CallbackContext context)
    {
        if (m_transmission == null)
        {
            ResolveVehicleReferences();
        }

        if (m_transmission == null)
        {
            return;
        }

        bool isLogitechPaddle = IsLogitechPaddle(context, "button6");
        if (context.canceled)
        {
            if (isLogitechPaddle)
            {
                m_shiftDownPaddleHeld = false;
            }

            if (!m_shiftUpPaddleHeld && !m_shiftDownPaddleHeld)
            {
                m_paddleAutomaticModeSelected = false;
            }

            return;
        }

        // 押下確定だけを使い開始通知との二重変速を防ぐ
        if (!context.performed)
        {
            return;
        }

        // 走行中の要求ではATを維持し停止中だけ1速からNからRへ選択する
        if (m_transmission.Type == Transmission.TransmissionType.Automatic)
        {
            if (IsExternalLever(context))
            {
                m_vehicleController.RequestLeverShift(false);
            }
            else
            {
                m_vehicleController.RequestPaddleShift(false);
            }

            return;
        }

        if (isLogitechPaddle)
        {
            m_shiftDownPaddleHeld = true;
            m_shiftDownPaddlePressedAt = Time.unscaledTime;
            m_paddleAutomaticModeSelected = false;
        }

        // LTBのダウン操作もアップと同じクラッチ判定を必ず通す
        if (IsExternalLever(context))
        {
            m_vehicleController.RequestLeverShift(false);
        }
        else
        {
            // パドルのダウン操作もクラッチ位置で拒否せず過回転保護を通す
            m_vehicleController.RequestPaddleShift(false);
        }
    }

    // G923とG29のパドル入力だけを判定する関数
    private bool IsLogitechPaddle(InputAction.CallbackContext context, string buttonName)
    {
        string controlPath = context.control?.path ?? string.Empty;
        string deviceName = context.control?.device?.displayName ?? string.Empty;
        bool isLogitechWheel = deviceName.Contains("G923") || deviceName.Contains("G29");
        return isLogitechWheel && controlPath.EndsWith("/" + buttonName);
    }

    // 既存のLTB-SMG入力定義と機器情報から外付けレバーだけを判定する関数
    private bool IsExternalLever(InputAction.CallbackContext context)
    {
        // 製品名とレイアウト名のどちらで接続されてもレバーを識別する情報
        var device = context.control?.device;
        if (device == null)
        {
            return false;
        }

        string identity = device.description.product + " " + device.displayName + " " + device.layout;
        return identity.IndexOf("LTB-SMG", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ホーン操作を車両へ渡す関数
    public void OnHorn(InputAction.CallbackContext context)
    {
        if (m_vehicleController == null)
        {
            ResolveVehicleReferences();
        }

        if (m_vehicleController == null)
        {
            return;
        }

        if (context.canceled)
        {
            m_vehicleController.IsHorn = 0.0f;
            return;
        }

        float value = context.ReadValue<float>();
        m_vehicleController.IsHorn = value;
    }
}
