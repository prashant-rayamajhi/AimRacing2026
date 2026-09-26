using UnityEngine;

// カウントダウン専用の回転制限と発進時の回転引き継ぎを管理するクラス
public partial class VehicleController
{
    // カウント開始条件をUIと車両で共通にする最低回転数
    [Header("Countdown Launch")]
    [SerializeField, Range(3000f, 6500f)]
    float m_countdownMinimumRPM = 6000f;
    // 開始条件を確実に超えつつ待機中の空ぶかしを抑える追加回転数
    [SerializeField, Range(0f, 1000f)]
    float m_countdownRPMMargin = 0f;
    // UIの開始判定へ渡す回転数
    public float CountdownMinimumRPM => m_countdownMinimumRPM;
    // 描画の間にリミッターから回転が下がっても全開で開始回転へ達したことを判定する
    public bool CountdownReady => m_engine != null && m_isPullUp && m_accelInput >= 0.99f && (m_engine.RPM >= CountdownMinimumRPM || (m_engine.RevLimiterCut && m_engine.TemporaryRevLimitRPM >= CountdownMinimumRPM));
    // 待機専用の上限を通常走行のレブリミット以内に収める回転数
    public float CountdownMaximumRPM => Mathf.Min(m_engine.OverRevRPM, CountdownMinimumRPM + Mathf.Clamp(m_countdownRPMMargin, 0f, 1000f));

    // クラッチをつなぐ発進演出として待機回転数から下げる回転数
    [SerializeField, Range(500f, 3000f)]
    float m_launchRPMDrop = 2000f;
    // クラッチ接続に伴う回転低下を滑らかに見せる時間
    [SerializeField, Range(0.1f, 2f)]
    float m_launchRPMBlendSeconds = 0.8f;
    // 壁に当たって発進できない場合に回転補助を残し続けない制限時間
    [SerializeField, Range(1f, 5f)]
    float m_launchMaximumAssistSeconds = 3f;
    // 発進時の回転低下を計算する開始回転数と経過時間
    float m_launchStartRPM;
    float m_launchElapsed;
    // イントロや通常停止ではなくカウントダウン中であることを区別する状態
    bool m_countdownLaunchActive;
    // 停止復帰のアイドル初期化を発進直後だけ抑える残り時間
    float m_launchRPMBlendRemaining;
    // カウント終了時の実回転数から求めた発進回転数
    float m_launchRetainedRPM;
    // カウントダウン開始時に1速を選びクラッチを切ったまま回転制限を開始する関数
    public void BeginCountdownLaunch()
    {
        if (m_engine == null || m_mission == null || !m_isPullUp)
        {
            return;
        }

        if (m_countdownLaunchActive)
        {
            return;
        }

        // ATとMTともメーター表示だけでなく実際の変速機を1速へ合わせる
        m_mission.PrepareForwardStart();
        m_countdownLaunchActive = true;
        m_launchRPMBlendRemaining = 0f;
        UpdateLaunchRPM();
    }

    // 車体固定を解除する直前に待機回転数を引き継ぐ関数
    void ReleaseCountdownLaunch()
    {
        if (!m_countdownLaunchActive || m_engine == null)
        {
            return;
        }

        // この時点では回転を飛ばさず、次の物理更新から少しずつ低下させる
        m_launchStartRPM = m_engine.RPM;
        m_launchRetainedRPM = Mathf.Max(m_engine.IdleAngularVelocity * CarPhysics.m_radiansToRpm, CountdownMaximumRPM - m_launchRPMDrop);
        m_launchElapsed = 0f;
        m_countdownLaunchActive = false;
        m_launchRPMBlendRemaining = Mathf.Max(m_launchRPMBlendSeconds, m_launchMaximumAssistSeconds);
        m_driveAwayPrepared = true;
    }

    // 待機時の上限と発進直後の最低回転を通常エンジン計算の後へ適用する関数
    void UpdateLaunchRPM()
    {
        if (m_engine == null)
        {
            return;
        }

        if (m_isPullUp)
        {
            // カウント中だけ制限しアクセルを離した時の自然な回転低下は残す
            m_engine.SetLaunchRPM(Mathf.Min(m_engine.RPM, CountdownMaximumRPM));
            return;
        }

        if (m_launchRPMBlendRemaining <= 0f)
        {
            return;
        }

        // ブレーキ中やアクセルを離した時は回転維持の補助を解除する
        if (m_accelInput <= 0.03f || m_brakeInput > 0.01f || m_isPullUp)
        {
            m_launchRPMBlendRemaining = 0f;
            return;
        }

        // 発進中に手動変速やサイドブレーキを操作した場合はプレイヤーの操作へ戻す
        if (m_mission.ActiveGear != 1 || m_mission.IsGearChanging || (m_brake != null && m_brake.HandbrakeActive))
        {
            m_launchRPMBlendRemaining = 0f;
            return;
        }

        // 同じ1速のままクラッチが滑ってつながる発進を補助し、車体速度は書き換えない
        m_launchElapsed += Time.fixedDeltaTime;
        float progress = Mathf.Clamp01(m_launchElapsed / Mathf.Max(0.01f, m_launchRPMBlendSeconds));
        float minimumRPM = Mathf.SmoothStep(m_launchStartRPM, m_launchRetainedRPM, progress);
        if (progress < 1f)
        {
            m_engine.SetLaunchRPM(minimumRPM);
        }
        else
        {
            // 駆動軸が発進回転へ追いついたら通常同期へ戻し、その後のAT変速条件は変えない
            float shaftRPM = m_wheelController != null ? Mathf.Abs(m_wheelController.ShaftAngularVelocity * m_mission.CurrentGearRatio) : 0f;
            shaftRPM *= CarPhysics.m_radiansToRpm;
            if (shaftRPM >= m_launchRetainedRPM)
            {
                m_launchRPMBlendRemaining = 0f;
            }

            m_engine.MaintainMinimumRPM(m_launchRetainedRPM);
        }

        m_launchRPMBlendRemaining = Mathf.Max(0f, m_launchRPMBlendRemaining - Time.fixedDeltaTime);
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
            if (!m_isPullUp)
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
        m_isPullUp = _active;
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
