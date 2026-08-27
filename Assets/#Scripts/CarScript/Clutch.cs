using UnityEngine;

//エンジンと変速機の回転差からクラッチの伝達トルクを計算するクラス
public class Clutch : MonoBehaviour
{
    //ATでクラッチ接続を自動制御するための設定
    [SerializeField] bool m_clutchAuto = true;

    //現在のクラッチが伝達できる最大トルク
    [SerializeField, ShowInInspector] float m_Calclate_ClutchMaxTorque;

    //クラッチ板を押し付ける圧着力
    [SerializeField, ShowInInspector] float m_CrimpingForce;

    //AT発進時に半クラッチから完全接続へ移る回転数範囲
    [SerializeField] Vector2 m_lockRange = new Vector2(900f, 1400f);

    //ATクラッチを接続する速さ
    [SerializeField, Range(0.5f, 20f)] float m_autoEngagementRate = 5f;

    //変速時にATクラッチを切り離す速さ
    [SerializeField, Range(0.5f, 30f)] float m_autoReleaseRate = 15f;

    //最大エンジントルクに耐えるクラッチ設計トルク
    [SerializeField] float m_DesignTorque = 700f;

    //クラッチ板の摩擦力を求める摩擦係数
    [SerializeField] float m_frictionCoef = 0.55f;

    //クラッチ板の有効半径を求める外径
    [SerializeField] float m_ClutchOD = 0.35f;

    //クラッチ板の有効半径を求める内径
    [SerializeField] float m_ClutchID = 0.25f;

    //クラッチの伝達容量を求める摩擦面数
    [SerializeField] float m_ClutchSurface = 4f;

    //変速中にクラッチを切るための状態
    [SerializeField, ShowInInspector] bool m_isGearChanging;

    //イントロ中にクラッチを切るための車体固定状態
    [SerializeField, ShowInInspector] bool m_isPullUp;

    //変速機へ渡す現在のクラッチトルク
    [SerializeField, ShowInInspector] float m_OutputTorque;

    //半クラッチの伝達トルクを求める入出力回転差
    [SerializeField, ShowInInspector] float m_clutchSlip;

    //車輪側から戻ってきたクラッチ角速度
    [SerializeField, ShowInInspector] float m_clutchAngularVelocity;

    //エンジン側のクラッチ角速度
    [SerializeField, ShowInInspector] float m_engineAngularVelocity;

    //エンジンからクラッチへ入力されるトルク
    [SerializeField, ShowInInspector] float m_engineTorque;

    //クラッチの現在接続率
    [SerializeField, ShowInInspector] float m_clutchInput;

    //旧デバッグ表示との互換性を保つクラッチロック値
    [SerializeField, ShowInInspector] float m_clutchLock;

    //クラッチ接続変化による振動量
    [SerializeField, ShowInInspector] float m_clutchInput_diff;

    //クラッチ接続変化を求める前回値
    float m_clutchInput_prev;

    //旧振動処理の振幅割合
    [SerializeField] float m_AmplitudeRatio;

    //旧振動処理を収束させる減衰割合
    [SerializeField] float m_DampingRatio;

    //旧振動処理の振動速度
    [SerializeField] float m_AmplitudeSpeed;

    //現在ギアのクラッチ接続条件を判断するギア比
    float m_gearRatio;

    //旧振動処理の開始時刻
    float m_zeroPoint;

    //現在のクラッチ伝達トルク
    public float ClutchTorque => m_OutputTorque;

    //現在のクラッチ接続率
    public float Engagement => m_clutchInput;

    //イントロ中の車体固定状態
    public bool IsPullUp { get => m_isPullUp; set => m_isPullUp = value; }

    //現在の変速状態
    public bool GearChanging { get => m_isGearChanging; set => m_isGearChanging = value; }

    //ATクラッチ制御の有効状態
    public bool AutoClutch { get => m_clutchAuto; set => m_clutchAuto = value; }

    //MTから受け取るクラッチ接続率
    public float ClutchInput { set => m_clutchInput = Mathf.Clamp01(value); }

    //旧振動演出へ渡す振動量
    public float Oscillation { set => m_clutchInput_diff = value; }

    //エンジン側と車輪側の状態からクラッチトルクを更新する関数
    public void DrivetrainUpdate(in float clutchOutputSide, in float engineAngularVelocity, in float engineOutputTorque,
        in float gearRatio, in float engineInertia)
    {
        m_clutchAngularVelocity = clutchOutputSide;
        m_engineAngularVelocity = engineAngularVelocity;
        m_engineTorque = engineOutputTorque;
        m_gearRatio = gearRatio;
        m_clutchSlip = m_engineAngularVelocity - m_clutchAngularVelocity;
        CalcClutchTorque();
    }

    //復帰前の回転差や伝達トルクを消去する関数
    public void ResetDynamics()
    {
        m_OutputTorque = 0f;
        m_clutchSlip = 0f;
        m_clutchAngularVelocity = 0f;
        m_engineAngularVelocity = 0f;
        m_engineTorque = 0f;
        m_clutchInput = m_clutchAuto ? 0f : m_clutchInput;
        m_clutchLock = m_clutchInput;
        m_clutchInput_diff = 0f;
        m_clutchInput_prev = m_clutchInput;
    }

    //クラッチの摩擦容量と回転差から伝達トルクを計算する関数
    void CalcClutchTorque()
    {
        if (m_clutchAuto) { UpdateAutomaticClutch(); }
        float effectiveRadius = (m_ClutchOD + m_ClutchID) / 4f;
        float diskForceRatio = m_frictionCoef * effectiveRadius * m_ClutchSurface;
        m_CrimpingForce = m_DesignTorque / Mathf.Max(0.001f, diskForceRatio) * m_clutchInput;
        m_Calclate_ClutchMaxTorque = diskForceRatio * m_CrimpingForce;
        if (m_clutchInput <= 0f || m_engineTorque <= 0f) { m_OutputTorque = 0f; return; }
        float couplingTorque = m_clutchSlip * 0.8f * m_clutchInput;
        float requestedTorque = m_engineTorque * m_clutchInput + couplingTorque;
        m_OutputTorque = Mathf.Clamp(requestedTorque, -m_Calclate_ClutchMaxTorque, m_Calclate_ClutchMaxTorque);
    }

    //AT発進時の回転数に応じてクラッチを滑らかに接続する関数
    void UpdateAutomaticClutch()
    {
        if (m_gearRatio == 0f || m_isGearChanging || m_isPullUp)
        {
            //イントロ、ニュートラル、変速中はクラッチを直ちに切ってエンジン回転を駆動輪から解放する
            m_clutchInput = 0f;
            m_clutchLock = 0f;
            return;
        }

        float engineRPM = Mathf.Abs(m_engineAngularVelocity) * CarPhysics.Rad2RPM;
        float drivetrainRPM = Mathf.Abs(m_clutchAngularVelocity) * CarPhysics.Rad2RPM;
        float couplingRPM = Mathf.Max(engineRPM, drivetrainRPM);
        float targetEngagement = Mathf.InverseLerp(m_lockRange.x, Mathf.Max(m_lockRange.x + 1f, m_lockRange.y), couplingRPM);
        float responseRate = targetEngagement > m_clutchInput ? m_autoEngagementRate : m_autoReleaseRate;
        m_clutchInput = Mathf.MoveTowards(m_clutchInput, targetEngagement, responseRate * Time.fixedDeltaTime);
        m_clutchLock = m_clutchInput;
    }
}
