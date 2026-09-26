using UnityEngine;

// 車両のトルク特性からエンジン回転数と出力トルクを計算するクラス
public class Car_Engine : MonoBehaviour
{
    [SerializeField]
    AnimationCurve m_torqueCurve;
    // 高回転型のゲーム用全負荷出力をPSからワットへ換算する定数
    const float m_wattsPerPS = 735.49875f;
    // 実車公称値ではなく設定されたクランク軸の最大トルク
    [SerializeField, Min(1f)]
    float m_peakTorqueNm = 500f;
    // トルクと回転数から求める出力が超えないゲーム用最大馬力
    [SerializeField, Min(1f)]
    float m_peakPowerPS = 500f;
    [SerializeField, ShowInInspector]
    float m_angularVelocity;
    [SerializeField, ShowInInspector]
    float m_engineRPM;
    [SerializeField, ShowInInspector]
    float m_effectiveTorque;
    [UnityEngine.Serialization.FormerlySerializedAs("m_ReactionTorque")]
    [SerializeField, ShowInInspector]
    float m_reactionTorque;
    // アクセルOFF時に駆動輪へ伝えるエンジン抵抗トルク
    [SerializeField, ShowInInspector]
    float m_engineBrakingTorque;
    // エンジンの慣性モーメント
    [Header("Engine")]
    [SerializeField]
    float m_inertia = 0.24f;
    [UnityEngine.Serialization.FormerlySerializedAs("IdleRPM")]
    [SerializeField]
    float m_idleRPM = 1200f;
    // 走行中の警告開始回転数でカウントダウン用上限とは分ける
    [SerializeField]
    float m_redzoneRPM = 7800f;
    // 設定されたゲーム用の燃料カット上限
    [SerializeField]
    float m_overRevRPM = 8200f;
    // 燃料カットから復帰する回転差を設けて上限付近の細かな切り替えを防ぐ
    [SerializeField, Range(50f, 400f)]
    float m_revLimiterHysteresisRPM = 180f;
    // 待機中は狭い回転差で燃料を復帰し大きく波打つ空ぶかしを防ぐ
    [SerializeField, Range(20f, 100f)]
    float m_countdownRevLimiterDropRPM = 60f;
    // 負荷のない開始待機で回転が一瞬で上がらないよう演出用の毎秒上昇幅を設定する
    [SerializeField, Min(1f)]
    float m_countdownRPMRisePerSecond = 4000f;
    // ATの回転合わせで目標へ近づくほど追加アクセルを弱める回転差
    [SerializeField, Range(200f, 1500f)]
    float m_blipThrottleRPMBand = 800f;
    // 待機時だけ車両側から指定する回転上限で通常走行はゼロに戻す
    public float TemporaryRevLimitRPM { get; set; }

    // 燃料カットの継続状態を回転数の上下限で管理する変数
    bool m_revLimiterCut;
    // 燃料カットの作動状態
    public bool RevLimiterCut => m_revLimiterCut;
    // 空ぶかし音へ燃料カットと吸気応答を含む実際の燃焼入力を渡す
    public float CombustionThrottle => m_injectionCut || m_revLimiterCut ? 0f : m_smoothedThrottle;

    [SerializeField]
    float m_throttleResponse = 8f;
    // クラッチ接続時に駆動軸回転へ追従する速さ
    [SerializeField, Range(1f, 20f)]
    float m_drivetrainSynchronizationRate = 8f;
    // 過給圧の立ち上がりをトルクへ反映する設定
    [Header("Turbo Response")]
    [SerializeField, Range(800f, 3000f)]
    float m_boostStartRPM = 1400f;
    [SerializeField, Range(1500f, 4500f)]
    float m_fullBoostRPM = 2600f;
    [SerializeField, Range(1f, 15f)]
    float m_turboSpoolUpRate = 6f;
    [SerializeField, Range(1f, 20f)]
    float m_turboSpoolDownRate = 10f;
    [SerializeField, ShowInInspector]
    float m_turboBoostRatio;
    // エンジン回転数の制限
    [Header("Engine braking")]
    [SerializeField]
    float m_frictionAtIdle = 8f;
    [SerializeField]
    float m_frictionCoef = 0.012f;
    // スロットル入力の制限
    [UnityEngine.Serialization.FormerlySerializedAs("m_Throttle")]
    [SerializeField, ShowInInspector]
    float m_throttle;
    float m_smoothedThrottle;
    bool m_injectionCut;
    // プロパティ
    public float RPM => m_engineRPM;
    public float EngineTorque => m_effectiveTorque;
    // 現在のエンジン抵抗トルクを車両制御へ渡すプロパティ
    public float EngineBrakingTorque => m_engineBrakingTorque;
    public float AngularVelocity => m_angularVelocity;
    public float AngularMomentum => m_angularVelocity * m_inertia;
    public float Inertia => m_inertia;
    public float TurboBoostRatio => m_turboBoostRatio;
    // 停止中のクラッチ同期判定に使用するアイドル角速度
    public float IdleAngularVelocity => m_idleRPM * CarPhysics.m_rpmToRadians;
    // オーバーレブRPM
    public float OverRevRPM { get => m_overRevRPM; set => m_overRevRPM = value; }
    public float RedzoneRPM { get => m_redzoneRPM; set => m_redzoneRPM = value; }
    public bool InjectionCut { get => m_injectionCut; set => m_injectionCut = value; }

    public void Initialize()
    {
        // 回転系の設定は維持し今回のゲーム用トルク特性だけを分離する
        m_inertia = 0.24f;
        m_idleRPM = Mathf.Clamp(m_idleRPM, 600f, 2000f);
        // Inspectorの走行上限を維持し初期化時に古い警告回転数へ戻さない
        m_overRevRPM = Mathf.Max(m_idleRPM, m_overRevRPM);
        m_redzoneRPM = Mathf.Clamp(m_redzoneRPM, m_idleRPM, m_overRevRPM);
        m_revLimiterCut = false;
        TemporaryRevLimitRPM = 0f;
        m_frictionAtIdle = 8f;
        m_frictionCoef = 0.012f;
        m_turboBoostRatio = 0f;
        m_engineBrakingTorque = m_frictionAtIdle;
        // 停止状態から始めても、スターターが回した状態にしてエンストを防ぐ。
        m_engineRPM = Mathf.Max(m_engineRPM, m_idleRPM);
        m_angularVelocity = m_engineRPM * CarPhysics.m_rpmToRadians;
    }

    // クラッチ接続率に応じてエンジン回転を駆動軸回転へ滑らかに同期する関数
    public void SynchronizeToDrivetrain(float _drivetrainAngularVelocity, float _clutchEngagement)
    {
        if (_clutchEngagement <= 0f)
        {
            return;
        }

        float targetAngularVelocity = Mathf.Clamp(Mathf.Abs(_drivetrainAngularVelocity), m_idleRPM * CarPhysics.m_rpmToRadians, m_overRevRPM * CarPhysics.m_rpmToRadians);
        float synchronization = 1f - Mathf.Exp(-m_drivetrainSynchronizationRate * Mathf.Clamp01(_clutchEngagement) * Time.fixedDeltaTime);
        m_angularVelocity = Mathf.Lerp(m_angularVelocity, targetAngularVelocity, synchronization);
        m_engineRPM = m_angularVelocity * CarPhysics.m_radiansToRpm;
    }

    // ATクリープ中に指定された最低回転数を維持する関数
    public void MaintainMinimumRPM(float _minimumRPM)
    {
        // 指定回転数をエンジンの安全な回転範囲へ収める
        float clampedMinimumRPM = Mathf.Clamp(_minimumRPM, m_idleRPM, m_overRevRPM);
        m_angularVelocity = Mathf.Max(m_angularVelocity, clampedMinimumRPM * CarPhysics.m_rpmToRadians);
        m_engineRPM = m_angularVelocity * CarPhysics.m_radiansToRpm;
    }

    // 発進演出の回転数を角速度と表示値の両方へ反映する関数
    public void SetLaunchRPM(float _rpm)
    {
        // エンジン停止やオーバーレブを発生させない範囲で発進回転を設定する
        m_engineRPM = Mathf.Clamp(_rpm, m_idleRPM, m_overRevRPM);
        m_angularVelocity = m_engineRPM * CarPhysics.m_rpmToRadians;
    }

    // コース復帰時にエンジンを安定したアイドル状態へ戻す
    public void ResetToIdle(float _throttleInput = 0f)
    {
        // アイドル回転数に戻す
        m_engineRPM = m_idleRPM;
        m_angularVelocity = m_idleRPM * CarPhysics.m_rpmToRadians;
        m_reactionTorque = 0f;
        m_throttle = Mathf.Clamp01(_throttleInput);
        m_smoothedThrottle = m_throttle;
        m_injectionCut = false;
        m_revLimiterCut = false;
        TemporaryRevLimitRPM = 0f;
        m_turboBoostRatio = 0f;
        // アイドル回転で発生可能な正味トルクを準備し、次のクラッチ更新から発進できるようにする。
        float combustionTorque = EvaluateEngineTorque(m_idleRPM) * m_smoothedThrottle;
        float engineBrake = m_frictionAtIdle * Mathf.Lerp(1f, 0.2f, m_smoothedThrottle);
        m_engineBrakingTorque = engineBrake;
        m_effectiveTorque = Mathf.Max(0f, combustionTorque - engineBrake);
    }

    public void EngineUpdate(float _throttleInput, float _clutchReactionTorque, float _revMatchTargetRPM = 0f)
    {
        // スロットル入力の制限
        m_throttle = Mathf.Clamp01(_throttleInput);
        // ATダウンシフトの目標回転に足りない分だけ燃焼トルクを追加し回転数を直接飛ばさない
        float blipTarget = Mathf.Clamp(_revMatchTargetRPM, 0f, m_overRevRPM);
        float blipThrottle = Mathf.Clamp01((blipTarget - m_engineRPM) / Mathf.Max(1f, m_blipThrottleRPMBand));
        m_throttle = Mathf.Max(m_throttle, blipThrottle);
        // 負値はタイヤ側からエンジンを回すバックドライブとして扱う
        m_reactionTorque = _clutchReactionTorque;
        // ペダルを踏んだ瞬間だけ最大トルクが出ないよう、吸気の応答を少し丸める
        m_smoothedThrottle = Mathf.MoveTowards(m_smoothedThrottle, m_throttle, m_throttleResponse * Time.fixedDeltaTime);
        // エンジン回転数の計算
        m_engineRPM = m_angularVelocity * CarPhysics.m_radiansToRpm;
        // 上限で燃料を切り復帰回転まで自然なエンジン抵抗で下げる
        float revLimit = TemporaryRevLimitRPM > m_idleRPM ? Mathf.Min(TemporaryRevLimitRPM, m_overRevRPM) : m_overRevRPM;
        // 待機用の回転差を通常走行へ持ち越さず走行中の駆動力変動を従来どおりに保つ
        float recoveryDrop = TemporaryRevLimitRPM > m_idleRPM ? Mathf.Clamp(m_countdownRevLimiterDropRPM, 20f, 100f) : m_revLimiterHysteresisRPM;
        if (m_engineRPM >= revLimit)
        {
            m_revLimiterCut = true;
        }
        else if (m_engineRPM <= revLimit - recoveryDrop)
        {
            m_revLimiterCut = false;
        }

        bool revLimiterActive = m_injectionCut || m_revLimiterCut;
        float fullLoadTorque = EvaluateEngineTorque(m_engineRPM);
        // 回転数とアクセル量から目標過給圧を求め、急なトルク段差を防ぐ処理
        float boostByRPM = Mathf.InverseLerp(m_boostStartRPM, m_fullBoostRPM, m_engineRPM);
        float targetBoost = boostByRPM * m_smoothedThrottle;
        float turboRate = targetBoost > m_turboBoostRatio ? m_turboSpoolUpRate : m_turboSpoolDownRate;
        m_turboBoostRatio = Mathf.MoveTowards(m_turboBoostRatio, targetBoost, turboRate * Time.fixedDeltaTime);
        // 公式トルクカーブを最大過給時として扱い、過給前も自然吸気相当のトルクを残す処理
        float turboTorqueRatio = Mathf.Lerp(0.78f, 1f, m_turboBoostRatio);
        float combustionTorque = revLimiterActive ? 0f : fullLoadTorque * turboTorqueRatio * m_smoothedThrottle;
        // アクセルオフでは回転数に比例した抵抗を残し、実車らしいエンジンブレーキを作る。
        float engineBrake = m_frictionAtIdle + m_frictionCoef * Mathf.Max(0f, m_engineRPM - m_idleRPM);
        // 待機中の燃料カットでは全開ペダルによる抵抗軽減を外し回転が上限に張り付くのを防ぐ
        float resistanceThrottle = TemporaryRevLimitRPM > m_idleRPM && revLimiterActive ? 0f : m_smoothedThrottle;
        engineBrake *= Mathf.Lerp(1f, 0.2f, resistanceThrottle);
        m_engineBrakingTorque = engineBrake;
        // カーブを正味の軸トルクとして扱い全負荷時に内部抵抗を二重に引かない
        if (!revLimiterActive)
        {
            combustionTorque += engineBrake * m_smoothedThrottle;
        }

        float netTorque = combustionTorque - engineBrake - m_reactionTorque;
        // 通常走行はトルクと慣性で積分し待機中の上昇だけを展示用に緩める
        float angularAcceleration = netTorque / Mathf.Max(0.01f, m_inertia);
        if (TemporaryRevLimitRPM > m_idleRPM)
        {
            angularAcceleration = Mathf.Min(angularAcceleration, m_countdownRPMRisePerSecond * CarPhysics.m_rpmToRadians);
        }

        m_angularVelocity += angularAcceleration * Time.fixedDeltaTime;
        // エンジン回転数の制限
        float minimumAngularVelocity = m_idleRPM * CarPhysics.m_rpmToRadians;
        float maximumAngularVelocity = revLimit * CarPhysics.m_rpmToRadians;
        // 積分で上限へ到達した瞬間にもカットを保持し丸め誤差による燃料カット漏れを防ぐ
        if (m_angularVelocity >= maximumAngularVelocity)
        {
            m_revLimiterCut = true;
        }

        m_angularVelocity = Mathf.Clamp(m_angularVelocity, minimumAngularVelocity, maximumAngularVelocity);
        m_engineRPM = m_angularVelocity * CarPhysics.m_radiansToRpm;
        // クラッチへ渡す値は角速度ではなく、クランク軸の正味トルク [N m]。
        m_effectiveTorque = Mathf.Max(0f, combustionTorque - engineBrake);
    }

    // 低回転の弱さと高回転の伸びを持つゲーム用正味トルクを返す関数
    public float EvaluateEngineTorque(float _rpm)
    {
        // 低回転から過給が効くまでを急な段差なくつなぐ基準点
        const float lowRPM = 1000f;
        const float boostRPM = 3000f;
        const float peakRPM = 4500f;
        float amount = _rpm < boostRPM ? Mathf.Lerp(0.13f, 0.86f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lowRPM, boostRPM, _rpm))) : Mathf.Lerp(0.86f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(boostRPM, peakRPM, _rpm)));
        float powerLimitedTorque = m_peakPowerPS * m_wattsPerPS / Mathf.Max(1f, _rpm * CarPhysics.m_rpmToRadians);
        return Mathf.Min(m_peakTorqueNm * amount, powerLimitedTorque);
    }
}
