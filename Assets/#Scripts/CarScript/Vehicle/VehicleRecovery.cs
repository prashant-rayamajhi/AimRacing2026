using UnityEngine;

// 車両制御を役割ごとに分けて管理するクラス
public partial class VehicleController
{
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

    // 停止状態からの発進準備を行う
    void PrepareDriveAwayFromAnyStop()
    {
        // カウントダウンから引き継いだ回転数を通常の停止復帰でアイドルへ戻さない
        if (m_launchRPMBlendRemaining > 0f)
        {
            return;
        }

        // ATが停止中にNへ残った場合はアクセル入力で1速へ戻して発進を可能にする
        if (!m_isPullUp && m_mission.Type == Transmission.TransmissionType.Automatic && m_mission.ActiveGear == 0 && !m_mission.AutomaticNeutralSelected && m_accelInput > 0.03f && m_brakeInput <= 0.01f)
        {
            m_mission.PrepareForwardStart();
        }

        // 停止状態からの発進準備を行う条件を判定する
        bool canDrive = !m_isPullUp && m_mission.ActiveGear != 0 && m_accelInput > 0.03f && m_brakeInput <= 0.01f;
        // 一度走り出した後は再び監視を有効にする。
        if (!canDrive || m_kph > 1f)
        {
            m_driveAwayPrepared = false;
            return;
        }

        // 発進準備が完了している場合は、再度準備を行わない。
        if (m_driveAwayPrepared || m_kph > 0.5f)
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
        if (m_mission.ActiveGear == 0 || m_kph >= 1f)
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
        m_kph = m_rigidbody.linearVelocity.magnitude * 3.6f;
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
        m_kph = 0f;
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
}
