using UnityEngine;

// Trackの外付けレバーだけ実ペダルでクラッチを操作する車両制御
public partial class VehicleController
{
    // Trackのレバー変速で半踏みを拒否する設定
    [Header("Manual Lever")]
    [SerializeField]
    bool m_requireTrackLeverClutch = true;
    // レバー変速と完全切断に必要なクラッチ踏み込み量
    [SerializeField, Range(0.5f, 1f)]
    float m_leverClutchDisengageThreshold = 0.85f;
    // ペダルの小さな揺れを未操作として許す上限
    [SerializeField, Range(0f, 0.2f)]
    float m_leverClutchReleasedThreshold = 0.05f;
    // ペダルを使った変速後は踏み戻しまで実クラッチで駆動を伝える状態
    bool m_manualPedalCouplingSelected;
    // 未接続のペダルを踏み込み済みと誤認しないための受信状態
    bool m_clutchPedalReceived;
    // 踏み込みを零から一で保持しクラッチ接続率と区別する値
    [SerializeField, ShowInInspector]
    float m_physicalClutchPedal;
    // 直近の変速操作がTrackのレバーだったことを保持する状態
    bool m_manualLeverSelected;
    // 現在のモードでレバーにクラッチを要求するかを返す状態
    public bool TrackLeverClutchRequired => m_requireTrackLeverClutch && m_mission != null && m_differential != null && m_mission.Type == Transmission.TransmissionType.Manual && m_differential.CurrentMode == Differential.GRFourMode.Track;
    // 自動変速補助と回転合わせを止めて実ペダルを優先する状態
    public bool ManualLeverActive => m_manualLeverSelected && m_manualPedalCouplingSelected && TrackLeverClutchRequired;
    // 実クラッチを踏むほど加速補助も弱め完全切断中の駆動漏れを防ぐ割合
    public float ManualDriveAssistRatio => ManualLeverActive ? Mathf.Clamp01(m_clutch.Engagement) : 1f;

    // 入力側が変換した実クラッチ踏み込み量を受け取る関数
    public void SetPhysicalClutchPedal(float pedal)
    {
        // 不正値を接続率へ流すとトルクまで壊れるため入力未受信として切断する
        if (float.IsNaN(pedal) || float.IsInfinity(pedal))
        {
            ClearPhysicalClutchPedal();
            return;
        }

        m_physicalClutchPedal = Mathf.Clamp01(pedal);
        m_clutchPedalReceived = true;
        // 最初の変速前でもペダルを踏んだ時点で駆動を切れるよう実クラッチへ切り替える
        if (TrackLeverClutchRequired && m_physicalClutchPedal > m_leverClutchReleasedThreshold)
        {
            m_manualLeverSelected = true;
            m_manualPedalCouplingSelected = true;
        }

        // 既存Normalの入力解釈を変更せずTrackレバーだけ下流で接続率へ変換する
        Clutch = m_physicalClutchPedal;
        UpdateManualLeverClutch();
    }

    // 入力コンポーネント停止後に古い踏み込み値で変速を許可しない関数
    public void ClearPhysicalClutchPedal()
    {
        m_clutchPedalReceived = false;
        m_physicalClutchPedal = 0f;
        Clutch = 0f;
        UpdateManualLeverClutch();
    }

    // LTBの押下時だけクラッチと既存の過回転保護を通して一段変速する関数
    public bool RequestLeverShift(bool up)
    {
        if (m_mission == null || m_clutch == null)
        {
            return false;
        }

        // ATではクラッチ付きMTへ切り替えず停止中の走行方向だけを選択する
        if (m_mission.Type == Transmission.TransmissionType.Automatic)
        {
            return RequestAutomaticDirection(up);
        }

        if (TrackLeverClutchRequired)
        {
            // ペダル切断を未踏みと誤認して実クラッチ操作中の変速を許可しない
            if (ManualLeverActive && !m_clutchPedalReceived)
            {
                return false;
            }

            m_manualLeverSelected = true;
            // 未操作なら自動クラッチを使い踏んだ場合だけ実ペダルへ切り替える
            m_manualPedalCouplingSelected = m_clutchPedalReceived && m_physicalClutchPedal > m_leverClutchReleasedThreshold;
            UpdateManualLeverClutch();
            // 半踏みの要求は予約せず離すか踏み切ってからレバーを操作し直してもらう
            if (!IsLeverClutchPositionAllowed(m_physicalClutchPedal, m_leverClutchReleasedThreshold, m_leverClutchDisengageThreshold))
            {
                return false;
            }
        }

        // 変速ロックや過回転で拒否された時も成功と表示しないための直前ギア
        int previousGear = m_mission.ActiveGear;
        if (up)
        {
            m_mission.ShiftUp();
        }
        else
        {
            m_mission.ShiftDown();
        }

        return previousGear != m_mission.ActiveGear;
    }

    // パドルは半踏み判定を通さず変速間隔と過回転保護だけで変速する関数
    public bool RequestPaddleShift(bool up)
    {
        if (m_mission == null || m_clutch == null)
        {
            return false;
        }

        // パドルにもレバーと同じAT停止条件を適用する
        if (m_mission.Type == Transmission.TransmissionType.Automatic)
        {
            return RequestAutomaticDirection(up);
        }

        // 変速許可とは別に踏み込み中の駆動切断を維持する
        SelectPaddleClutch();
        int previousGear = m_mission.ActiveGear;
        if (up)
        {
            m_mission.ShiftUp();
        }
        else
        {
            m_mission.ShiftDown();
        }

        return previousGear != m_mission.ActiveGear;
    }

    // メーターの更新待ちではなく現在の車体速度でATの方向切替を判定する関数
    bool RequestAutomaticDirection(bool up)
    {
        if (m_rigidbody == null || m_IsPullUp)
        {
            return false;
        }

        return m_mission.RequestAutomaticSelector(up, m_rigidbody.linearVelocity.magnitude * 3.6f);
    }

    // 高回転ダウンシフト時の速度変更を一瞬で行わず短時間で収める秒数
    [SerializeField, Range(0.1f, 1f)]
    float m_manualDownshiftSlowdownSeconds = 0.3f;
    // 入力経路に関係なく成立した変速を検出するための前回ギア
    int m_previousSpeedLimitGear;
    // 最初の物理更新をダウンシフトと誤認しないための初期化状態
    bool m_speedLimitGearInitialized;
    // 選択段の最高速度へ収めるまでの残り時間
    float m_downshiftSlowdownRemaining;
    // 成立したMTダウンシフトだけにゲーム用の減速補助を適用する関数
    void ApplyManualDownshiftSpeedLimit()
    {
        if (m_mission == null || m_rigidbody == null || m_engine == null || m_wheelController == null)
        {
            return;
        }

        // 前後左右の速度だけを制限し落下やサスペンションの上下運動は残す
        int gear = m_mission.ActiveGear;
        bool downshift = m_speedLimitGearInitialized && gear > 0 && gear < m_previousSpeedLimitGear;
        bool changed = !m_speedLimitGearInitialized || gear != m_previousSpeedLimitGear;
        m_previousSpeedLimitGear = gear;
        m_speedLimitGearInitialized = true;
        if (m_IsPullUp || m_mission.Type != Transmission.TransmissionType.Manual || gear <= 0)
        {
            m_downshiftSlowdownRemaining = 0f;
            return;
        }

        // アップシフトやNへの変更で古い段の制限を残さない
        if (changed)
        {
            m_downshiftSlowdownRemaining = downshift ? m_manualDownshiftSlowdownSeconds : 0f;
        }

        if (m_downshiftSlowdownRemaining <= 0f)
        {
            return;
        }

        // 実際のギア比とタイヤ半径とエンジン上限から選択段の最高速度を求める
        float limitKph = m_mission.GetTheoreticalForwardSpeedKph(gear, m_engine.OverRevRPM, m_wheelController.WheelRadius);
        if (limitKph <= 0f)
        {
            m_downshiftSlowdownRemaining = 0f;
            return;
        }

        Vector3 velocity = m_rigidbody.linearVelocity;
        Vector3 planar = Vector3.ProjectOnPlane(velocity, Vector3.up);
        float speed = planar.magnitude;
        float target = limitKph / 3.6f;
        if (speed <= target)
        {
            m_downshiftSlowdownRemaining = 0f;
            return;
        }

        // 残り時間内に上限へ到達する割合で減速しドリフトの進行方向を変えない
        float blend = Mathf.Clamp01(Time.fixedDeltaTime / Mathf.Max(Time.fixedDeltaTime, m_downshiftSlowdownRemaining));
        float nextSpeed = Mathf.Lerp(speed, target, blend);
        m_rigidbody.linearVelocity = velocity - planar + planar * (nextSpeed / speed);
        m_KPH = m_rigidbody.linearVelocity.magnitude * 3.6f;
        m_downshiftSlowdownRemaining = Mathf.Max(0f, m_downshiftSlowdownRemaining - Time.fixedDeltaTime);
    }

    // 未操作と踏み切りだけを許可し半踏みの変速を拒否する関数
    static bool IsLeverClutchPositionAllowed(float pedal, float released, float disengaged)
    {
        // 不正な入力値を踏み切りと誤認して変速しない
        if (float.IsNaN(pedal) || float.IsInfinity(pedal))
        {
            return false;
        }

        return pedal <= Mathf.Clamp(released, 0f, 0.2f) || pedal >= Mathf.Clamp(disengaged, 0.5f, 1f);
    }

    // パドルへ持ち替えた時は従来の自動クラッチへ戻す関数
    public void SelectPaddleClutch()
    {
        // パドルの変速通知で踏み込み中のクラッチを勝手につながない
        if (TrackLeverClutchRequired && m_clutchPedalReceived && m_physicalClutchPedal > m_leverClutchReleasedThreshold)
        {
            m_manualLeverSelected = true;
            m_manualPedalCouplingSelected = true;
            UpdateManualLeverClutch();
            return;
        }

        if (!m_manualLeverSelected)
        {
            return;
        }

        m_manualLeverSelected = false;
        m_manualPedalCouplingSelected = false;
        if (m_clutch != null)
        {
            m_clutch.AutoClutch = true;
        }

        if (m_clutch != null)
        {
            m_clutch.PhysicalManualCoupling = false;
        }

        if (m_mission != null)
        {
            m_mission.ManualClutchControlled = false;
        }
    }

    // モード切替と踏み込み量を物理更新前に反映する関数
    void UpdateManualLeverClutch()
    {
        if (m_mission == null || m_clutch == null)
        {
            return;
        }

        if (m_manualLeverSelected && !TrackLeverClutchRequired)
        {
            SelectPaddleClutch();
        }

        m_mission.ManualClutchControlled = ManualLeverActive;
        m_clutch.PhysicalManualCoupling = ManualLeverActive;
        if (!ManualLeverActive)
        {
            // ペダルなしのレバー変速でもパドルと同じ自動切断を働かせる
            if (m_manualLeverSelected)
            {
                m_clutch.AutoClutch = true;
            }

            return;
        }

        m_clutch.AutoClutch = false;
        // 未受信時は安全側に切断しペダル入力を待つ
        // 踏み戻し側の微小な残留入力を半クラッチ扱いせず完全につなぐ
        float released = Mathf.Clamp(m_leverClutchReleasedThreshold, 0f, 0.2f);
        float disengaged = Mathf.Clamp(m_leverClutchDisengageThreshold, 0.5f, 1f);
        m_clutch.ClutchInput = m_clutchPedalReceived ? 1f - Mathf.InverseLerp(released, disengaged, m_physicalClutchPedal) : 0f;
    }
}
