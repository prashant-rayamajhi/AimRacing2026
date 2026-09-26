using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;

#endif
// 既存デフへ走行モードの選択と滑らかな前後配分の切替を追加するクラス
public partial class Differential
{
    // 前後駆動配分と操縦補助を切り替える走行モード
    public enum DriveMode
    {
        Normal,
        Sport,
        Track
    }

    // 発進から走行終了まで使用する駆動モード
    [Header("Drive Mode (1 Normal / 2 Sport / 3 Track)")]
    [SerializeField]
    DriveMode m_driveMode = DriveMode.Normal;
    // スポーツで後輪寄りの駆動特性を作る前輪配分
    [SerializeField, Range(0f, 1f)]
    float m_sportFrontTorqueRatio = 0.30f;
    // トラックで前後均等の駆動特性を作る前輪配分
    [SerializeField, Range(0f, 1f)]
    float m_trackFrontTorqueRatio = 0.50f;
    // 配分が瞬時に飛ばないよう一秒当たりの変化量を制限する値
    [SerializeField, Min(0.01f)]
    float m_modeBlendRate = 0.60f;
    // G923の既存入力を書き換えず数字キーでモードを選ぶ設定
    [SerializeField]
    bool m_enableModeKeyboard = true;
    // G923の未使用ボタンからNormalとTrackだけを切り替える設定
    [SerializeField]
    bool m_enableG923ModeToggle = true;
    // 既存パドルとホーンを避けてモード切替に使うG923のボタン名
    [SerializeField]
    string m_g923ModeButton = "button4";
    // 長押しでモードが往復しないよう機器ごとの押下状態を保持する
    readonly System.Collections.Generic.HashSet<int> m_pressedModeDevices = new System.Collections.Generic.HashSet<int>();
    // 四輪へ同じ物理ステップの配分を渡すための補間済み比率
    float m_appliedFrontTorqueRatio;
    // 最初の駆動計算で未初期化の前輪配分を使わないためのフラグ
    bool m_distributionInitialized;
    // Track専用のタイヤと補助設定を走行中に滑らかに切り替える割合
    public float TrackHandlingBlend { get; private set; }
    // Sportの操舵とタイヤを駆動配分と同じ時間で切り替える割合
    public float SportHandlingBlend { get; private set; }
    // インスペクターや検証から現在の選択を読み取るプロパティ
    public DriveMode CurrentMode => m_driveMode;
    // 配分切替が目指す前輪比率を読み取るプロパティ
    public float TargetFrontTorqueRatio => GetModeFrontRatio(m_driveMode);
    // 中央差動制限を加える前の補間済み前輪比率を読み取るプロパティ
    public float AppliedFrontTorqueRatio => m_distributionInitialized ? m_appliedFrontTorqueRatio : TargetFrontTorqueRatio;

    // 有効化時に保存済みモードを反映して初回の配分変化を防ぐ関数
    void OnEnable()
    {
        m_pressedModeDevices.Clear();
        m_appliedFrontTorqueRatio = TargetFrontTorqueRatio;
        m_distributionInitialized = true;
        TrackHandlingBlend = m_driveMode == DriveMode.Track ? 1f : 0f;
        SportHandlingBlend = m_driveMode == DriveMode.Sport ? 1f : 0f;
    }

    // 数字キーとG923の専用ボタンで既存のペダルや操舵入力に干渉せずモードを選ぶ関数
    void Update()
    {
        if (!Application.isFocused || Time.timeScale <= 0f)
        {
            return;
        }

#if ENABLE_INPUT_SYSTEM
        ReadG923ModeToggle();
#endif
        if (!m_enableModeKeyboard)
        {
            return;
        }

        // 両入力方式が有効なプロジェクトでも片方のキー入力を取りこぼさず一度だけ切り替える
        bool normal = false;
        bool sport = false;
        bool track = false;
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            normal = keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame;
            sport = keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame;
            track = keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame;
        }

#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        normal |= Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1);
        sport |= Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2);
        track |= Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3);
#endif
        if (normal)
        {
            SetDriveMode(DriveMode.Normal);
        }
        else if (sport)
        {
            SetDriveMode(DriveMode.Sport);
        }
        else if (track)
        {
            SetDriveMode(DriveMode.Track);
        }
    }

#if ENABLE_INPUT_SYSTEM
    // G923の押下だけを読みFFBや既存InputActionの設定を変更せずモードを切り替える関数
    void ReadG923ModeToggle()
    {
        if (!m_enableG923ModeToggle || string.IsNullOrEmpty(m_g923ModeButton))
        {
            return;
        }

        foreach (InputDevice device in InputSystem.devices)
        {
            // 他のゲームパッドやG29のデバッグボタンを誤って受け取らないよう機種を限定する
            string identity = device.description.product + " " + device.displayName + " " + device.layout;
            if (identity.IndexOf("G923", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var button = device.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(m_g923ModeButton);
            if (button == null)
            {
                continue;
            }

            // 入力更新方式が異なっても押した瞬間に一度だけ切り替え離したら次を受け付ける
            if (!button.isPressed)
            {
                m_pressedModeDevices.Remove(device.deviceId);
                continue;
            }

            if (!m_pressedModeDevices.Add(device.deviceId))
            {
                continue;
            }

            // チーム側が同じボタンを割り当てた場合は既存機能を優先し二重実行を防ぐ
            foreach (InputAction action in InputSystem.ListEnabledActions())
            {
                foreach (InputControl control in action.controls)
                {
                    if (control != button)
                    {
                        continue;
                    }

                    Debug.LogWarning($"[駆動モード] {m_g923ModeButton} は {action.name} が使用中のため切替を中止しました。", this);
                    return;
                }
            }

            SetDriveMode(m_driveMode == DriveMode.Track ? DriveMode.Normal : DriveMode.Track);
            return;
        }
    }

#endif
    // 不正な選択値を拒否して駆動モードの切替先を更新する関数
    public void SetDriveMode(DriveMode _mode)
    {
        if (_mode != DriveMode.Normal && _mode != DriveMode.Sport && _mode != DriveMode.Track)
        {
            return;
        }

        if (_mode == m_driveMode)
        {
            return;
        }

        m_driveMode = _mode;
    }

    // 四輪のトルク計算前に一度だけ前後配分を更新する関数
    public void UpdateDriveDistribution(float _deltaTime)
    {
        // モードの名前だけでなく旋回設定も同じ速度で切り替える
        TrackHandlingBlend = Mathf.MoveTowards(TrackHandlingBlend, m_driveMode == DriveMode.Track ? 1f : 0f, Mathf.Max(0.01f, m_modeBlendRate) * Mathf.Max(0f, _deltaTime));
        // 切替途中で別モードを選んでも二つの補助割合の合計を1以下に保つ
        SportHandlingBlend = Mathf.MoveTowards(SportHandlingBlend, m_driveMode == DriveMode.Sport ? 1f : 0f, Mathf.Max(0.01f, m_modeBlendRate) * Mathf.Max(0f, _deltaTime));
        // 初期化直後は選択モードを使い、走行中の切替だけを補間する
        if (!m_distributionInitialized)
        {
            m_appliedFrontTorqueRatio = TargetFrontTorqueRatio;
            m_distributionInitialized = true;
        }

        m_appliedFrontTorqueRatio = Mathf.MoveTowards(m_appliedFrontTorqueRatio, TargetFrontTorqueRatio, Mathf.Max(0.01f, m_modeBlendRate) * Mathf.Max(0f, _deltaTime));
    }

    // 選択モードに対応する前輪比率を安全な範囲で返す関数
    float GetModeFrontRatio(DriveMode _mode)
    {
        switch (_mode)
        {
            case DriveMode.Sport:
                return Mathf.Clamp01(m_sportFrontTorqueRatio);
            case DriveMode.Track:
                return Mathf.Clamp01(m_trackFrontTorqueRatio);
            default:
                return Mathf.Clamp01(m_frontTorqueRatio);
        }
    }
}
