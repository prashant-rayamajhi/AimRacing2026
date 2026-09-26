using UnityEngine;

// 車両制御を役割ごとに分けて管理するクラス
public partial class VehicleController
{
    // 物理演算の更新ごとに呼ばれる処理
    void FixedUpdate()
    {
        // Trackの実クラッチを自動復帰や駆動計算より先に反映する
        UpdateManualLeverClutch();
        // サイドブレーキ入力を先に読み取り、発進補助との競合を防ぐ
        m_brake.UpdateHandbrake(!m_isPullUp);
        // 衝突後の速度補正で相対速度を誤用しないよう物理更新直前の車体速度を保存する
        m_preStepVelocity = m_rigidbody.linearVelocity;
        m_hasPreStepVelocity = true;
        float driveInputRate = m_accelInput > m_smoothedDriveInput ? m_driveInputRiseRate : m_driveInputFallRate;
        m_smoothedDriveInput = Mathf.MoveTowards(m_smoothedDriveInput, m_accelInput, driveInputRate * Time.fixedDeltaTime);
        // 復帰後の実際の発進を計測し、駆動系の初期化で再発進を準備する
        if (!m_brake.HandbrakeActive && !ManualLeverActive)
        {
            ApplyRecoveryRestartAssist();
            PrepareDriveAwayFromAnyStop();
        }

        // 完全停止してブレーキを離した瞬間に、制動中の負スリップを次の発進へ持ち越さない。
        if (!ManualLeverActive && m_previousBrakeInput > 0.01f && m_brakeInput <= 0.01f && m_kph < 0.5f)
        {
            PrepareStationaryRestart();
        }

        // ギア切り替え
        // 実タイヤ半径と現在のレブ上限を渡し設定変更後も安全な変速判定を行う
        m_mission.ConfigureShiftSafety(m_wheelController.WheelRadius, m_engine.OverRevRPM);
        m_mission.TransmissionUpdate(m_engine.RPM, m_kph, m_accelInput);
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
        if (clutchAssistRatio > 0f && !m_isPullUp && m_brakeInput <= 0.01f && m_accelInput > 0.03f && m_mission.CurrentGearRatio != 0f && m_kph < m_launchAssistMaximumSpeedKph)
        {
            float launchRatio = 1f - Mathf.Clamp01(m_kph / m_launchAssistMaximumSpeedKph);
            float launchTorque = m_launchAssistTorque * m_smoothedDriveInput * launchRatio * clutchAssistRatio;
            driveTorque = Mathf.Sign(m_mission.CurrentGearRatio) * Mathf.Max(Mathf.Abs(driveTorque), launchTorque);
        }

        // 0km/hから100km/hまでの加速をゲーム向けに強化する
        if (clutchAssistRatio > 0f && !m_isPullUp && m_brakeInput <= 0.01f && m_accelInput > 0.03f && m_mission.CurrentGearRatio > 0f)
        {
            float accelerationFade = Mathf.InverseLerp(80f, m_arcadeAccelerationFadeEndKph, m_kph);
            float accelerationMultiplier = Mathf.Lerp(1f, m_accelTorqueBoost, m_smoothedDriveInput);
            // 踏み戻した時だけ通常MTと同じ倍率へ戻し半クラッチ中は補助を弱める
            driveTorque *= Mathf.Lerp(1f, Mathf.Lerp(accelerationMultiplier, 1f, accelerationFade), clutchAssistRatio);
        }

        // MTの高い段では低回転の駆動不足を発進補助や加速倍率で打ち消さない
        driveTorque *= m_mission.LowRPMDriveRatio;
        // ATの1速でクリープトルクをアクセル駆動へ滑らかに受け渡す処理
        bool canApplyCreep = m_mission.Type == Transmission.TransmissionType.Automatic && m_mission.ActiveGear == 1 && !m_isPullUp && m_brakeInput <= 0.01f && !m_brake.HandbrakeActive;
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
        float clutchInputSide = shaftVelocity * m_mission.CurrentGearRatio;
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
        clutchInputSide = shaftVelocity * m_mission.CurrentGearRatio;
        // ニュートラルだったとき
        if (m_mission.CurrentGearRatio == 0f)
        {
            // クラッチの出力
            clutchInputSide = m_engine.RPM * CarPhysics.m_rpmToRadians;
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
        bool manualLaunchAllowed = m_mission.Type == Transmission.TransmissionType.Manual && m_mission.ActiveGear == 1 && !m_isPullUp && m_brakeInput <= 0.01f && !m_brake.HandbrakeActive && !m_mission.IsGearChanging;
        m_clutch.ConfigureManualLaunch(manualLaunchAllowed, m_accelInput, m_engine.IdleAngularVelocity);
        m_clutch.DrivetrainUpdate(clutchInputSide, m_engine.AngularVelocity, m_engine.EngineTorque, m_mission.CurrentGearRatio, m_engine.Inertia);
        // エンジンの回転数の更新
        bool atForwardSpeedLimiter = m_mission.Type == Transmission.TransmissionType.Automatic && m_mission.ActiveGear > 0 && m_kph >= m_atMaximumSpeedKph;
        // MTのアップ時だけ駆動を抜きダウン時の回転合わせを燃料カットで妨げない
        bool manualShiftInjectionCut = m_mission.Type == Transmission.TransmissionType.Manual && m_mission.IsGearChanging && m_mission.IsShiftUp && !ManualLeverActive;
        m_engine.InjectionCut = manualShiftInjectionCut || atForwardSpeedLimiter || m_reverseLimiterActive;
        // 待機中も実際の燃料カットで回転制限し発進後は通常上限へ戻す
        m_engine.TemporaryRevLimitRPM = m_isPullUp ? CountdownMaximumRPM : 0f;
        // ATダウンシフト時だけ新しいギアの駆動軸回転へアクセルを補って合わせる
        // 自動クラッチ付きMTも前進段のダウン時は車輪側に回転を合わせる
        bool manualBlip = m_mission.Type == Transmission.TransmissionType.Manual && m_clutch.AutoClutch && m_mission.IsGearChanging && m_mission.IsShiftDown && m_mission.ActiveGear > 0;
        bool automaticBlip = (m_mission.AutomaticBlipActive || manualBlip) && !m_isPullUp;
        float blipRPM = automaticBlip ? Mathf.Abs(clutchInputSide) * CarPhysics.m_radiansToRpm : 0f;
        m_engine.EngineUpdate(m_accelInput, m_clutch.ClutchTorque, blipRPM);
        // クラッチ接続率に合わせて駆動軸回転をエンジンへ段差なく同期する処理
        // 変速中も残っているクラッチ接続分だけ回転を合わせ空ぶかしや急落を抑える
        // 実クラッチでは回転差トルクで合わせるため回転数の直接同期を重ねない
        bool canSynchronizeShift = !ManualLeverActive && !m_clutch.ManualLaunchSlipActive && (!m_mission.IsGearChanging || m_clutch.AutoClutch);
        float drivetrainCoupling = m_mission.CurrentGearRatio != 0f && canSynchronizeShift && !m_isPullUp ? m_clutch.Engagement : 0f;
        m_engine.SynchronizeToDrivetrain(clutchInputSide, drivetrainCoupling);
        // カウント中と発進直後だけ専用の回転範囲を適用する
        // 実クラッチ操作中は発進演出で回転合わせを上書きしない
        if (ManualLeverActive && !m_isPullUp)
        {
            m_launchRPMBlendRemaining = 0f;
        }
        else
        {
            UpdateLaunchRPM();
        }

        // 車速の計算
        m_kph = m_rigidbody.linearVelocity.magnitude * 3600f / 1000f;
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

        if (m_isPullUp || m_brakeInput > 0.01f || m_mission.CurrentGearRatio == 0f || m_clutch.Engagement <= 0f)
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
}
