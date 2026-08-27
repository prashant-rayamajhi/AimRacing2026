using UnityEngine;

//GRヤリスのトルク特性からエンジン回転数と出力トルクを計算するクラス
public class Car_Engine : MonoBehaviour
{
    [SerializeField] AnimationCurve m_torqueCurve;

    [SerializeField, ShowInInspector] float m_angularVelocity;
    [SerializeField, ShowInInspector] float m_engineRPM;
    [SerializeField, ShowInInspector] float m_effectiveTorque;
    [SerializeField, ShowInInspector] float m_ReactionTorque;

    //エンジンの慣性モーメント
    [Header("GR Yaris G16E-GTS")]
    [SerializeField] float m_inertia = 0.24f;
    [SerializeField] float IdleRPM = 900f;
    [SerializeField] float m_redzoneRPM = 7000f;
    [SerializeField] float m_overRevRPM = 7200f;
    [SerializeField] float m_throttleResponse = 8f;

    //クラッチ接続時に駆動軸回転へ追従する速さ
    [SerializeField, Range(1f, 20f)] float m_drivetrainSynchronizationRate = 8f;

    //過給圧の立ち上がりをトルクへ反映する設定
    [Header("Turbo Response")]
    [SerializeField, Range(800f, 3000f)] float m_boostStartRPM = 1400f;
    [SerializeField, Range(1500f, 4500f)] float m_fullBoostRPM = 2600f;
    [SerializeField, Range(1f, 15f)] float m_turboSpoolUpRate = 6f;
    [SerializeField, Range(1f, 20f)] float m_turboSpoolDownRate = 10f;
    [SerializeField, ShowInInspector] float m_turboBoostRatio;

    //エンジン回転数の制限
    [Header("Engine braking")]
    [SerializeField] float m_frictionAtIdle = 8f;
    [SerializeField] float m_frictionCoef = 0.012f;

    //スロットル入力の制限
    [SerializeField, ShowInInspector] float m_Throttle;
    float m_smoothedThrottle;
    bool m_injectionCut;

    //プロパティ
    public float RPM => m_engineRPM;
    public float EngineTorque => m_effectiveTorque;
    public float AngularVelocity => m_angularVelocity;
    public float AngularMomentum => m_angularVelocity * m_inertia;
    public float Inertia => m_inertia;
    public float TurboBoostRatio => m_turboBoostRatio;
    //停止中のクラッチ同期判定に使用するアイドル角速度
    public float IdleAngularVelocity => IdleRPM * CarPhysics.RPM2Rad;

    //オーバーレブRPM
    public float OverRevRPM { get => m_overRevRPM; set => m_overRevRPM = value; }
    public float RedzoneRPM { get => m_redzoneRPM; set => m_redzoneRPM = value; }
    public bool InjectionCut { get => m_injectionCut; set => m_injectionCut = value; }

    public void Initialize()
    {

        //304PS / 6,500rpm、400N・m / 3,250～4,600rpmを再現するための回転系設定。
        m_inertia = 0.24f;
        IdleRPM = 900f;
        m_redzoneRPM = 7000f;
        m_overRevRPM = 7200f;
        m_frictionAtIdle = 8f;
        m_frictionCoef = 0.012f;
        m_turboBoostRatio = 0f;

        //停止状態から始めても、スターターが回した状態にしてエンストを防ぐ。
        m_engineRPM = Mathf.Max(m_engineRPM, IdleRPM);
        m_angularVelocity = m_engineRPM * CarPhysics.RPM2Rad;
    }

    //クラッチ接続率に応じてエンジン回転を駆動軸回転へ滑らかに同期する関数
    public void SynchronizeToDrivetrain(float drivetrainAngularVelocity, float clutchEngagement)
    {
        if (clutchEngagement <= 0f) { return; }

        float targetAngularVelocity = Mathf.Clamp(
            Mathf.Abs(drivetrainAngularVelocity), IdleRPM * CarPhysics.RPM2Rad, m_overRevRPM * CarPhysics.RPM2Rad);
        float synchronization = 1f - Mathf.Exp(-m_drivetrainSynchronizationRate * Mathf.Clamp01(clutchEngagement) * Time.fixedDeltaTime);
        m_angularVelocity = Mathf.Lerp(m_angularVelocity, targetAngularVelocity, synchronization);
        m_engineRPM = m_angularVelocity * CarPhysics.Rad2RPM;
    }

    //コース復帰時にエンジンを安定したアイドル状態へ戻す
    public void ResetToIdle(float throttleInput = 0f)
    {
        //アイドル回転数に戻す
        m_engineRPM = IdleRPM;
        m_angularVelocity = IdleRPM * CarPhysics.RPM2Rad;
        m_ReactionTorque = 0f;
        m_Throttle = Mathf.Clamp01(throttleInput);
        m_smoothedThrottle = m_Throttle;
        m_injectionCut = false;
        m_turboBoostRatio = 0f;

        //アイドル回転で発生可能な正味トルクを準備し、次のクラッチ更新から発進できるようにする。
        float combustionTorque = EvaluateGRYarisTorque(IdleRPM) * m_smoothedThrottle;
        float engineBrake = m_frictionAtIdle * Mathf.Lerp(1f, 0.2f, m_smoothedThrottle);
        m_effectiveTorque = Mathf.Max(0f, combustionTorque - engineBrake);
    }

    public void EngineUpdate(float throttleInput, float clutchReactionTorque)
    {
        //スロットル入力の制限
        m_Throttle = Mathf.Clamp01(throttleInput);
        
        //負値はタイヤ側からエンジンを回すバックドライブとして扱う
        m_ReactionTorque = clutchReactionTorque;

        //ペダルを踏んだ瞬間だけ最大トルクが出ないよう、吸気の応答を少し丸める
        m_smoothedThrottle = Mathf.MoveTowards(m_smoothedThrottle, m_Throttle, m_throttleResponse * Time.fixedDeltaTime);

        //エンジン回転数の計算
        m_engineRPM = m_angularVelocity * CarPhysics.Rad2RPM;
        bool revLimiterActive = m_injectionCut || m_engineRPM >= m_overRevRPM;
        float fullLoadTorque = EvaluateGRYarisTorque(m_engineRPM);

        //回転数とアクセル量から目標過給圧を求め、急なトルク段差を防ぐ処理
        float boostByRPM = Mathf.InverseLerp(m_boostStartRPM, m_fullBoostRPM, m_engineRPM);
        float targetBoost = boostByRPM * m_smoothedThrottle;
        float turboRate = targetBoost > m_turboBoostRatio ? m_turboSpoolUpRate : m_turboSpoolDownRate;
        m_turboBoostRatio = Mathf.MoveTowards(m_turboBoostRatio, targetBoost, turboRate * Time.fixedDeltaTime);

        //公式トルクカーブを最大過給時として扱い、過給前も自然吸気相当のトルクを残す処理
        float turboTorqueRatio = Mathf.Lerp(0.78f, 1f, m_turboBoostRatio);
        float combustionTorque = revLimiterActive ? 0f : fullLoadTorque * turboTorqueRatio * m_smoothedThrottle;

        //アクセルオフでは回転数に比例した抵抗を残し、実車らしいエンジンブレーキを作る。
        float engineBrake = m_frictionAtIdle + m_frictionCoef * Mathf.Max(0f, m_engineRPM - IdleRPM);
        engineBrake *= Mathf.Lerp(1f, 0.2f, m_smoothedThrottle);

        float netTorque = combustionTorque - engineBrake - m_ReactionTorque;
        m_angularVelocity += netTorque / Mathf.Max(0.01f, m_inertia) * Time.fixedDeltaTime;

        //エンジン回転数の制限
        float minimumAngularVelocity = IdleRPM * CarPhysics.RPM2Rad;
        float maximumAngularVelocity = m_overRevRPM * CarPhysics.RPM2Rad;
        m_angularVelocity = Mathf.Clamp(m_angularVelocity, minimumAngularVelocity, maximumAngularVelocity);
        m_engineRPM = m_angularVelocity * CarPhysics.Rad2RPM;

        //クラッチへ渡す値は角速度ではなく、クランク軸の正味トルク [N m]。
        m_effectiveTorque = Mathf.Max(0f, combustionTorque - engineBrake);
    }

    float EvaluateGRYarisTorque(float rpm)
    {
        //進化型G16E-GTSの公称値（最大400 N m / 3,250～4,600 rpm、304 PS / 6,500 rpm）に合わせた補間
        if (rpm < 1500f) { return Mathf.Lerp(110f, 230f, Mathf.InverseLerp(900f, 1500f, rpm)); }
        if (rpm < 2500f) { return Mathf.Lerp(230f, 360f, Mathf.InverseLerp(1500f, 2500f, rpm)); }
        if (rpm < 3250f) { return Mathf.Lerp(360f, 400f, Mathf.InverseLerp(2500f, 3250f, rpm)); }
        if (rpm <= 4600f) { return 400f; }
        if (rpm < 5500f) { return Mathf.Lerp(400f, 375f, Mathf.InverseLerp(4600f, 5500f, rpm)); }
        if (rpm < 6500f) { return Mathf.Lerp(375f, 329f, Mathf.InverseLerp(5500f, 6500f, rpm)); }
        return Mathf.Lerp(329f, 250f, Mathf.InverseLerp(6500f, m_overRevRPM, rpm));
    }
}
