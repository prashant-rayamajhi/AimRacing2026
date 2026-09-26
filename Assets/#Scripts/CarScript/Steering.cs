using UnityEngine;
using UnityEngine.UIElements;
using VehiclePhysics;

// 車速と車体寸法から左右前輪の操舵角を計算するクラス
public class Steering : MonoBehaviour
{
    enum SteeringType
    {
        Ackerman,
        Parallel
    }

    [SerializeField]
    SteeringType m_type = SteeringType.Ackerman;
    // 前輪のAckermann角を求めるホイールベース
    [SerializeField]
    float m_wheelBase = 2.56f;
    // 左右前輪のAckermann角を求めるトレッド幅
    [SerializeField]
    float m_treadWidth = 1.54f;
    // ハンドル回転量と前輪舵角の関係を示すギア比
    [SerializeField]
    float m_steeringGearRatio = 13.6f;
    // 低速時でも超えない前輪の最大舵角
    [SerializeField, Range(0f, 900f)]
    float m_maxSteerAngle = 35f;
    [SerializeField, Range(180f, 900f)]
    float m_steeringRange = 900f;
    [Header("Speed Sensitive Steering")]
    [SerializeField, Range(20f, 45f)]
    float m_lowSpeedMaxSteerAngle = 35f;
    // 最高速付近で急旋回を防ぎながら操作可能な舵角を残す値
    [SerializeField, Range(5f, 20f)]
    float m_highSpeedMaxSteerAngle = 10f;
    // 低速用舵角から高速用舵角まで段階的に減らす基準速度
    [SerializeField, Range(100f, 250f)]
    float m_fullReductionSpeedKph = 180f;
    // 低速で必要な曲がりやすさを残したまま舵角制限を始める速度
    [SerializeField, Range(0f, 100f)]
    float m_steeringReductionStartKph = 30f;
    // 高速域の入力中央付近を細かく操作しやすくする指数
    [SerializeField, Range(1f, 2f)]
    float m_highSpeedInputExponent = 1.25f;
    [SerializeField, Range(0f, 0.1f)]
    float m_analogDeadZone = 0.015f;
    // 最大舵角を変えず、中間のハンドル操作で曲がりすぎないよう控えめに補う量
    [SerializeField, Range(0f, 1f)]
    float m_analogMidInputBoost = 0.3f;
    // キーボード操舵を急激に最大角へ変化させないための入力速度
    [SerializeField, Range(1f, 15f)]
    float m_keyboardSteerRate = 5f;
    // キーを離した時に操舵を素早く中央へ戻すための入力速度
    [SerializeField, Range(1f, 20f)]
    float m_keyboardReturnRate = 7.5f;
    public float VehicleSpeedKph { get; set; }

    // ハンドルからの入力
    float m_inputAngle;
    float m_currentInput;
    bool m_smoothInput;
    public float InputAngle
    {
        get
        {
            return m_inputAngle;
        }

        set
        {
            m_inputAngle = value;
        }
    }

    public float CurrentInput => m_currentInput;
    public float CurrentCenterAngle { get; private set; }
    public bool SmoothInput { get => m_smoothInput; set => m_smoothInput = value; }
    // Trackの操舵応答だけを切り替えNormalの入力特性を維持する割合
    public float TrackHandlingBlend { get; set; }
    // Sportの前輪が先導する感覚を駆動配分と一緒に切り替える割合
    public float SportHandlingBlend { get; set; }

    // 高速時だけ基準舵角をさらに絞り初心者の切り過ぎを抑える倍率
    [Header("Mode Steering")]
    [SerializeField, Range(0.5f, 1f)]
    float m_normalHighSpeedAngleScale = 0.85f;
    // Sportで前側の応答を残しながら高速時の急旋回を抑える倍率
    [SerializeField, Range(0.5f, 1f)]
    float m_sportHighSpeedAngleScale = 0.90f;
    // Trackで速度を上げるほど切り込み過ぎを抑える倍率
    [SerializeField, Range(0.5f, 1f)]
    float m_trackHighSpeedAngleScale = 0.75f;
    // Sportで前輪入力を小さく丸め車体が自然に追従する時定数
    [SerializeField, Range(0.01f, 0.15f)]
    float m_sportTurnResponseSeconds = 0.045f;
    // Trackで急な切り込みを丸める時定数で実車の計測値ではなくゲーム用調整値
    [SerializeField, Range(0.02f, 0.15f)]
    float m_trackTurnResponseSeconds = 0.08f;
    // カウンターや戻し操作を遅らせず滑りから回復しやすくする時定数
    [SerializeField, Range(0.01f, 0.08f)]
    float m_trackReturnResponseSeconds = 0.025f;
    // Trackの切り返しで前輪の向きが急反転しないための時定数
    [SerializeField, Range(0.02f, 0.12f)]
    float m_trackDriftReturnSeconds = 0.06f;
    // 車庫入れやクリープでは操舵を遅らせないための開始速度
    [SerializeField, Min(0f)]
    float m_trackResponseStartKph = 20f;
    // 走行速度でTrackの応答設定を全量使う速度
    [SerializeField, Min(1f)]
    float m_trackResponseFullKph = 60f;
    // モード切替時に古い舵角が飛び出さないよう常時更新する応答用舵角
    float m_trackCenterAngle;
    // 入力機器に応じて操舵入力と判定用速度を滑らかに更新する関数
    public void UpdateSteering(float _deltaTime)
    {
        // G923などのアナログ入力の中央付近にある微小な揺れを除去する
        float targetInput = Mathf.Clamp(m_inputAngle, -1f, 1f);
        if (!m_smoothInput && Mathf.Abs(targetInput) < m_analogDeadZone)
        {
            targetInput = 0f;
        }

        // キーボードだけ短い補間を使い、瞬間的な横滑りを防ぎながら応答性を維持する
        if (m_smoothInput)
        {
            float speedRatio = Mathf.InverseLerp(m_steeringReductionStartKph, m_fullReductionSpeedKph, Mathf.Abs(VehicleSpeedKph));
            float highSpeedSteerRate = Mathf.Lerp(m_keyboardSteerRate, m_keyboardSteerRate * 0.65f, speedRatio);
            // 左右の切り返しではまず戻し速度で中央へ戻し残り時間だけ反対へ切る
            float remainingTime = Mathf.Max(0f, _deltaTime);
            bool reversing = targetInput * m_currentInput < 0f;
            if (reversing)
            {
                float returnTime = Mathf.Abs(m_currentInput) / Mathf.Max(0.01f, m_keyboardReturnRate);
                m_currentInput = Mathf.MoveTowards(m_currentInput, 0f, m_keyboardReturnRate * remainingTime);
                remainingTime = Mathf.Max(0f, remainingTime - returnTime);
            }

            float inputRate = Mathf.Abs(targetInput) < Mathf.Abs(m_currentInput) ? m_keyboardReturnRate : highSpeedSteerRate;
            m_currentInput = Mathf.MoveTowards(m_currentInput, targetInput, inputRate * remainingTime);
        }
        else
        {
            // アナログハンドルは実機の連続入力を遅延させず反映する
            m_currentInput = targetInput;
        }

        // 舵角へ短い一次遅れを加え車体の速度や向きを直接書き換えず切り込みを穏やかにする
        float targetAngle = CalcCenterSteerAngle();
        float modeAmount = Mathf.Clamp01(TrackHandlingBlend + SportHandlingBlend);
        float track = modeAmount * Mathf.InverseLerp(m_trackResponseStartKph, m_trackResponseFullKph, Mathf.Abs(VehicleSpeedKph));
        bool returning = targetAngle * m_trackCenterAngle < 0f || Mathf.Abs(targetAngle) < Mathf.Abs(m_trackCenterAngle);
        float turnTime = Mathf.Lerp(m_sportTurnResponseSeconds, m_trackTurnResponseSeconds, TrackHandlingBlend / Mathf.Max(0.001f, modeAmount));
        // Sportの戻しは維持しTrackだけ切り返しの急激な横力変化を丸める
        float modeReturnTime = Mathf.Lerp(m_trackReturnResponseSeconds, m_trackDriftReturnSeconds, TrackHandlingBlend / Mathf.Max(0.001f, modeAmount));
        float responseTime = returning ? modeReturnTime : turnTime;
        float response = 1f - Mathf.Exp(-Mathf.Max(0f, _deltaTime) / Mathf.Max(0.001f, responseTime));
        m_trackCenterAngle = track <= 0f ? targetAngle : Mathf.Lerp(m_trackCenterAngle, targetAngle, response);
        CurrentCenterAngle = Mathf.Lerp(targetAngle, m_trackCenterAngle, track);
    }

    // 左右前輪の位置に合わせたAckermann舵角を計算する関数
    public float CalcSteerAngle(bool _isRight)
    {
        float steerAngle = CurrentCenterAngle;
        switch (m_type)
        {
            case SteeringType.Ackerman:
                return CalcAckermanAngle(steerAngle, _isRight);
            case SteeringType.Parallel:
                return steerAngle;
            default:
                return 0f;
        }
    }

    // 入力と速度から車両中央の基準舵角を計算する関数
    float CalcCenterSteerAngle()
    {
        // ハンドル回転範囲とステアリングギア比から機械的な前輪最大舵角を求める
        float mechanicalMaxAngle = m_steeringRange * 0.5f / Mathf.Max(1f, m_steeringGearRatio);
        // 速度上昇に合わせて舵角を連続的に減らし、低速の曲がりやすさと高速安定性を両立する
        float speedRatio = Mathf.InverseLerp(m_steeringReductionStartKph, m_fullReductionSpeedKph, Mathf.Abs(VehicleSpeedKph));
        float smoothSpeedRatio = speedRatio * speedRatio * (3f - 2f * speedRatio);
        float speedLimitedAngle = Mathf.Lerp(m_lowSpeedMaxSteerAngle, m_highSpeedMaxSteerAngle, smoothSpeedRatio);
        // 低速舵角は保ち高速域の制限だけをモードごとに滑らかに変更する
        float modeScale = m_normalHighSpeedAngleScale;
        modeScale += (m_sportHighSpeedAngleScale - m_normalHighSpeedAngleScale) * Mathf.Clamp01(SportHandlingBlend);
        modeScale += (m_trackHighSpeedAngleScale - m_normalHighSpeedAngleScale) * Mathf.Clamp01(TrackHandlingBlend);
        speedLimitedAngle *= Mathf.Lerp(1f, modeScale, smoothSpeedRatio);
        float effectiveMaxAngle = Mathf.Min(m_maxSteerAngle, mechanicalMaxAngle, speedLimitedAngle);
        float inputExponent = Mathf.Lerp(1f, m_highSpeedInputExponent, smoothSpeedRatio);
        float normalizedInput = Mathf.Abs(m_currentInput);
        // アナログ入力の中間域だけを滑らかに補助し、0度と最大回転位置は元の入力へ一致させる
        if (!m_smoothInput)
        {
            normalizedInput = Mathf.Clamp01(normalizedInput + m_analogMidInputBoost * normalizedInput * normalizedInput * (1f - normalizedInput));
        }

        float shapedInput = Mathf.Sign(m_currentInput) * Mathf.Pow(normalizedInput, inputExponent);
        return shapedInput * effectiveMaxAngle;
    }

    [UnityEngine.Serialization.FormerlySerializedAs("steerFactor")]
    [Space]
    // ステア角補正ファクター
    [SerializeField]
    private float m_steerFactor;
    // パラメータ(値を増やすと曲がらなくなる)
    // 倍率
    [UnityEngine.Serialization.FormerlySerializedAs("lowSpeedCoefStart")]
    public float m_lowSpeedCoefStart = 1.2f;
    // 収束
    [UnityEngine.Serialization.FormerlySerializedAs("lowSpeedCoefEnd")]
    public float m_lowSpeedCoefEnd = 1.0f;
    [UnityEngine.Serialization.FormerlySerializedAs("highSpeedCoefStart")]
    public float m_highSpeedCoefStart = 1.0f;
    [UnityEngine.Serialization.FormerlySerializedAs("highSpeedCoefEnd")]
    public float m_highSpeedCoefEnd = 0.3f;
    // 速度に応じた倍率の係数
    [UnityEngine.Serialization.FormerlySerializedAs("SpeedCoefMin")]
    public float m_speedCoefMin = 1.0f;
    [UnityEngine.Serialization.FormerlySerializedAs("SpeedCoefMax")]
    public float m_speedCoefMax = 1.2f;
    float CalcAckermanAngle(float _steerAngle, bool _isRight)
    {
        float absoluteAngle = Mathf.Abs(_steerAngle) * Mathf.Deg2Rad;
        if (absoluteAngle < 0.0001f)
        {
            return 0f;
        }

        // 中央舵角から旋回半径を求めて内輪と外輪の舵角を計算する
        float turnRadius = m_wheelBase / Mathf.Tan(absoluteAngle);
        float innerAngle = Mathf.Atan(m_wheelBase / Mathf.Max(0.1f, turnRadius - m_treadWidth * 0.5f)) * Mathf.Rad2Deg;
        float outerAngle = Mathf.Atan(m_wheelBase / (turnRadius + m_treadWidth * 0.5f)) * Mathf.Rad2Deg;
        bool rightTurn = _steerAngle > 0f;
        float wheelAngle = rightTurn ? (_isRight ? innerAngle : outerAngle) : (_isRight ? outerAngle : innerAngle);
        return Mathf.Sign(_steerAngle) * wheelAngle;
    }

    // 内側の切れ角から外側の切れ角を計算する
    float CalcAckermanOutsideAngle(float _insideAngle, float _steerAngle)
    {
        bool isRight = true;
        if (_insideAngle < 0)
        {
            _insideAngle *= -1;
            isRight = false;
        }

        float tanA = Mathf.Tan(_insideAngle * Mathf.Deg2Rad);
        float angle = Mathf.Atan(m_wheelBase * tanA / ((m_treadWidth * tanA) + m_wheelBase)) * Mathf.Rad2Deg;
        if (!isRight)
        {
            angle *= -1;
        }

        return angle;
    }
}
