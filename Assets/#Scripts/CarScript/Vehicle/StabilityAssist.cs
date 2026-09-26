using UnityEngine;

// 横滑りの検出と片輪制動による姿勢回復を管理するクラス
public partial class VehicleController
{
    // ESCの介入を計算する
    void UpdateESC()
    {
        // 車体のローカル座標系での速度を計算
        Vector3 localVelocity = transform.InverseTransformDirection(m_rigidbody.linearVelocity);
        float forwardSpeed = localVelocity.z;
        float actualSpeedKph = localVelocity.magnitude * 3.6f;
        m_escSlipAngle = Mathf.Atan2(localVelocity.x, Mathf.Max(1f, Mathf.Abs(forwardSpeed))) * Mathf.Rad2Deg;
        // 実際の中央舵角とホイールベースから単純車両モデルの定常ヨーレートを計算する
        float referenceSteerAngle = m_steering.CurrentCenterAngle;
        float kinematicYawRate = forwardSpeed * Mathf.Tan(referenceSteerAngle * Mathf.Deg2Rad) / m_escWheelBase;
        // タイヤが発生できる横加速度を超えない範囲に目標ヨーレートを制限する
        float gripLimitedYawRate = m_escMaximumLateralAcceleration / Mathf.Max(1f, Mathf.Abs(forwardSpeed));
        float targetYawRate = Mathf.Clamp(kinematicYawRate, -gripLimitedYawRate, gripLimitedYawRate);
        // 旋回の許容滑り幅を共有し限界内の滑りを追加トルクで強制しない
        UpdateModeSlipAllowance(targetYawRate, localVelocity, gripLimitedYawRate);
        // 舵を切った瞬間に定常旋回へならないように目標ヨーレートへ過渡応答させる
        // 操舵を戻した後に以前の旋回目標を残さず、ESCが車を曲げ続けることを防ぐ
        // Trackのカウンターでは前の旋回目標を早く解消して戻し操作とESCの逆作用を防ぐ
        bool trackCountersteer = m_differential != null && m_differential.TrackHandlingBlend > 0f && targetYawRate * m_escDesiredYawRate < 0f;
        float recoveryRate = Mathf.Max(m_escYawResponseRate, m_escYawRecenteringRate);
        float yawResponseRate = trackCountersteer ? recoveryRate : (Mathf.Abs(referenceSteerAngle) < 0.05f ? m_escYawRecenteringRate : m_escYawResponseRate);
        m_escDesiredYawRate = Mathf.MoveTowards(m_escDesiredYawRate, targetYawRate, yawResponseRate * Time.fixedDeltaTime);
        float actualYawRate = Vector3.Dot(m_rigidbody.angularVelocity, transform.up);
        float yawError = actualYawRate - m_escDesiredYawRate;
        // ESCの介入条件を判定する
        if (!m_escEnabled || m_isPullUp || GroundedWheelCount < 3 || actualSpeedKph < m_escMinimumSpeedKph || Mathf.Abs(forwardSpeed) < 1f)
        {
            m_escIntervention = 0f;
            m_escBrakeWheelIndex = -1;
            m_escActive = false;
            m_escDesiredYawRate = Mathf.MoveTowards(m_escDesiredYawRate, 0f, m_escYawResponseRate * Time.fixedDeltaTime);
            return;
        }

        // スリップ角とヨー誤差の両方を考慮して、ESCの介入度合いを計算する
        // ESCが最大介入へ達するスリップ角
        float modeBlend = m_differential != null ? m_differential.TrackHandlingBlend : 0f;
        // Sportで許した小さな追従角の範囲は片輪制動で打ち消さない
        // 片輪制動と車体の復帰補助で同じ滑り許容幅を使う
        float slipThreshold = GetModeSlipThreshold();
        float yawThreshold = m_escYawErrorThreshold * Mathf.Lerp(1f, m_trackESCYawThresholdScale, modeBlend);
        float fullSlipThreshold = slipThreshold * m_escFullSlipMultiplier;
        // Trackの最大保護角はアクセルで滑り幅を広げても過度に拡大させない
        if (m_trackDriftHistoryValid)
        {
            float trackFullSlip = Mathf.Max(slipThreshold + 1f, m_trackFullRecoverySlipDegrees);
            fullSlipThreshold = Mathf.Lerp(fullSlipThreshold, trackFullSlip, modeBlend);
            // ドリフトを戻す逆舵を曲がり不足と誤認して左右の片輪制動を切り替えない
            float counterWeight = CalculateTrackCountersteerWeight(m_escSlipAngle, actualYawRate, m_escDesiredYawRate, slipThreshold, fullSlipThreshold);
            yawError *= Mathf.Lerp(1f, m_trackCountersteerESCScale, counterWeight * modeBlend);
        }

        float slipIntervention = Mathf.InverseLerp(slipThreshold, fullSlipThreshold, Mathf.Abs(m_escSlipAngle));
        float yawIntervention = Mathf.InverseLerp(yawThreshold, yawThreshold * m_escFullYawMultiplier, Mathf.Abs(yawError));
        // スリップ角とヨー誤差の大きい方をESCの目標介入率にする
        float targetIntervention = Mathf.Clamp01(Mathf.Max(slipIntervention, yawIntervention));
        targetIntervention = Mathf.Pow(targetIntervention, m_escInterventionExponent);
        // 介入開始と解除で速度を分けて急な制御変化を防ぐ
        float interventionRate = targetIntervention > m_escIntervention ? m_escInterventionRiseRate : m_escInterventionReleaseRate;
        m_escIntervention = Mathf.MoveTowards(m_escIntervention, targetIntervention, interventionRate * Time.fixedDeltaTime);
        m_escActive = m_escIntervention > 0.01f;
        // 操舵方向が小さい場合は実際のヨー方向から車両の回転方向を判定する
        // 回り始めの微小回転を逆旋回と誤判定せず曲がり不足では後輪側の制動を選ぶ
        bool oversteer = IsESCOversteer(actualYawRate, m_escDesiredYawRate, yawThreshold);
        // 旋回誤差が小さい横滑りでは駆動力だけを抑え、片輪制動で不要な旋回を作らない
        m_escBrakeWheelIndex = SelectESCBrakeWheel(yawError, oversteer, forwardSpeed, yawThreshold);
    }

    // 旋回開始時のゼロ回転を除外し実際の逆旋回または回りすぎだけを判定する関数
    static bool IsESCOversteer(float _actualYawRate, float _desiredYawRate, float _yawThreshold)
    {
        // 微小な符号の揺れで前輪と後輪の制動先が切り替わらないよう同じ判定幅を使う
        bool oppositeYaw = _actualYawRate * _desiredYawRate < 0f && Mathf.Abs(_actualYawRate) > _yawThreshold;
        return oppositeYaw || Mathf.Abs(_actualYawRate) > Mathf.Abs(_desiredYawRate) + _yawThreshold;
    }

    // 旋回誤差を打ち消す側の車輪を進行方向に合わせて選ぶ関数
    static int SelectESCBrakeWheel(float _yawError, bool _oversteer, float _forwardSpeed, float _yawThreshold)
    {
        // 微小な誤差で左右のブレーキが切り替わらないよう判定幅を設ける
        if (Mathf.Abs(_yawError) <= _yawThreshold || Mathf.Abs(_forwardSpeed) < 1f)
        {
            return -1;
        }

        // 後退中は制動力の向きが反転するため、補正する車輪の左右も反転する
        bool brakeRightSide = _yawError * _forwardSpeed < 0f;
        return _oversteer ? (brakeRightSide ? 0 : 1) : (brakeRightSide ? 2 : 3);
    }

    // アクセル開度と姿勢の崩れ方から車体へ実際に適用するESC介入率を計算する関数
    float GetAppliedESCIntervention()
    {
        // 通常旋回の補助と限界時の補助を分け、アクセルオフで急に片輪制動を増やさない
        float applied = CalculateESCApplication(m_escIntervention, m_smoothedDriveInput, m_escPoweredInterventionScale, m_escCoastInterventionScale);
        // 接地したTrackのアクセル操作だけ緩和しNormalやSportと接触中の保護は変更しない
        if (!m_trackDriftHistoryValid)
        {
            return applied;
        }

        float scale = CalculateTrackPoweredESCScale(m_escSlipAngle, m_trackSlipRateDegrees, GetModeSlipThreshold(), m_trackFullRecoverySlipDegrees, m_trackESCSlipPredictionSeconds, m_trackPoweredESCScale);
        // ブレーキを踏むほど従来の姿勢回復へ戻しペダルの役割を保つ
        float powered = Mathf.Clamp01(m_smoothedDriveInput) * (1f - Mathf.Clamp01(m_brakeInput));
        return applied * Mathf.Lerp(1f, scale, powered * Mathf.Clamp01(m_differential.TrackHandlingBlend));
    }

    // ペダル開度で通常旋回の補助を補間し、大きく滑った時は制動を最大まで許可する関数
    static float CalculateESCApplication(float _intervention, float _throttle, float _poweredScale, float _coastScale)
    {
        // 外部設定が範囲外でも制動率が負値や最大値超過にならないよう制限する
        float demand = Mathf.Clamp01(_intervention);
        // 惰性と加速の補助を同じ倍率にすればペダル操作だけでは制動率が変わらない
        float normalScale = Mathf.Lerp(Mathf.Clamp01(_coastScale), Mathf.Clamp01(_poweredScale), Mathf.Clamp01(_throttle));
        // 限界付近では減速の弱さより姿勢回復を優先するため通常旋回の倍率を解除する
        float severeInstabilityRatio = Mathf.InverseLerp(0.65f, 1f, demand);
        return demand * Mathf.Lerp(normalScale, 1f, severeInstabilityRatio);
    }

    // 速度と舵角から子供でも扱いやすいコーナー用トルク倍率を計算する関数
    float GetCornerAssistTorqueRatio()
    {
        if (!m_cornerAssistEnabled || m_isPullUp || m_mission.CurrentGearRatio <= 0f || m_brakeInput > 0.01f)
        {
            m_cornerAssistIntervention = Mathf.MoveTowards(m_cornerAssistIntervention, 0f, m_cornerAssistReleaseRate * Time.fixedDeltaTime);
            return 1f;
        }

        float speedRatio = Mathf.InverseLerp(m_cornerAssistStartSpeedKph, m_cornerAssistFullSpeedKph, m_kph);
        float steerRatio = Mathf.InverseLerp(m_cornerAssistStartSteerAngle, m_cornerAssistFullSteerAngle, Mathf.Abs(m_steering.CurrentCenterAngle));
        // 実際の横滑りが小さい時は速度を奪わず、大きく滑り始めた時だけ補助を強める
        float fullSlipThreshold = m_escSlipAngleThreshold * m_escFullSlipMultiplier;
        float slipDemand = Mathf.InverseLerp(m_escSlipAngleThreshold * 0.5f, fullSlipThreshold, Mathf.Abs(m_escSlipAngle));
        float cornerDemand = Mathf.Lerp(m_cornerAssistLowSlipDemand, 1f, slipDemand);
        float targetIntervention = speedRatio * steerRatio * Mathf.Clamp01(m_smoothedDriveInput) * cornerDemand * Mathf.Clamp01(m_drivingAssistStrength);
        float rate = targetIntervention > m_cornerAssistIntervention ? m_cornerAssistRiseRate : m_cornerAssistReleaseRate;
        m_cornerAssistIntervention = Mathf.MoveTowards(m_cornerAssistIntervention, targetIntervention, rate * Time.fixedDeltaTime);
        // Trackではハンドル角だけを理由に駆動を削らず実際の滑りに対するESCと空転制御へ任せる
        float trackBlend = m_differential != null ? m_differential.TrackHandlingBlend : 0f;
        float minimumTorque = Mathf.Lerp(m_cornerAssistMinimumTorqueRatio, 1f, trackBlend);
        return Mathf.Lerp(1f, minimumTorque, m_cornerAssistIntervention);
    }
}
