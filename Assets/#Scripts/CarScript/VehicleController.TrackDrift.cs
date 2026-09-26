using UnityEngine;

// Trackの滑り幅と切り返し時の復帰補助を管理するクラス
public partial class VehicleController
{
    // アクセルで滑りを維持するときに許す車体と進行方向の角度差
    [Header("Drift Control")]
    [SerializeField, Range(5f, 14f)]
    float m_trackPoweredSlipDegrees = 11f;
    // アクセルを戻したときに許す滑り幅
    [SerializeField, Range(5f, 14f)]
    float m_trackCoastSlipDegrees = 9f;
    // ブレーキで姿勢を整えるときに許す滑り幅
    [SerializeField, Range(4f, 12f)]
    float m_trackBrakingSlipDegrees = 7f;
    // ペダルの踏み替えで滑りの許容幅が急変しないための毎秒変更角度
    [SerializeField, Min(0.1f)]
    float m_trackSlipAllowanceRate = 6f;
    // 接地の小さな揺れを滑りの増大と誤判定しないための平滑化時間
    [SerializeField, Range(0.04f, 0.3f)]
    float m_trackSlipRateFilterSeconds = 0.12f;
    // 滑りが戻り始めたときに復帰補助を弱める減衰比
    [SerializeField, Range(0f, 1.5f)]
    float m_trackRecoveryDamping = 0.8f;
    // 連続カーブで補助トルクの向きが急変しないための角加速度変更上限
    [SerializeField, Min(0.1f)]
    float m_trackYawAccelerationRate = 2.5f;
    // 制御できているカウンター操作に残すESCのヨー誤差割合
    [SerializeField, Range(0.1f, 1f)]
    float m_trackCountersteerESCScale = 0.25f;
    // ペダル操作に関係なくESCの最大回復を許可する滑り角
    [SerializeField, Range(12f, 25f)]
    float m_trackFullRecoverySlipDegrees = 16f;
    // アクセル中に維持できている滑りへ残すESCの制動と駆動制限の割合
    [SerializeField, Range(0.5f, 1f)]
    float m_trackPoweredESCScale = 0.75f;
    // 滑りの拡大を先読みして限界に達する前に通常の保護を戻す時間
    [SerializeField, Range(0.1f, 0.5f)]
    float m_trackESCSlipPredictionSeconds = 0.2f;
    // MTの踏み直しで残ったエンジンブレーキを解除する時間
    [SerializeField, Range(0.02f, 0.35f)]
    float m_trackManualCoastReleaseSeconds = 0.08f;
    // 踏み直し時だけ惰性制動の解除を速め全開後の残留制動をなくす関数
    static float ReleaseManualCoastTorque(float _previous, float _target, float _throttle, float _responseSeconds, float _deltaTime)
    {
        // 全開ではエンジン側の摩擦を残し追加の惰性制動だけを終了する
        if (_throttle >= 1f && _target <= 0f)
        {
            return 0f;
        }

        // 物理更新間隔が変わっても同じ時間で減速トルクを解放する
        float response = 1f - Mathf.Exp(-Mathf.Max(0f, _deltaTime) / Mathf.Max(0.01f, _responseSeconds));
        return Mathf.Lerp(_previous, _target, response);
    }

    // 現在ペダル操作に合わせて滑らかに変更した滑り許容角
    [SerializeField, ShowInInspector]
    float m_trackSlipAllowanceDegrees;
    // 滑りが拡大中か回復中かを判断する毎秒角度変化
    [SerializeField, ShowInInspector]
    float m_trackSlipRateDegrees;
    // 切り返しで急反転させずに適用する復帰角加速度
    float m_trackRecoveryAcceleration;
    // 前の物理更新から滑り角速度を求めるための角度
    float m_trackPreviousSlipDegrees;
    // 空中や衝突前の履歴を復帰直後へ持ち込まないための有効状態
    bool m_trackDriftHistoryValid;
    // 物理更新ごとに滑りの変化速度とペダル別の許容幅を更新する関数
    void UpdateTrackDriftState()
    {
        // 停止や接触中は旋回補助を重ねず次の接地時に履歴を取り直す
        if (!CanApplyModeHandling() || m_differential.TrackHandlingBlend <= 0f)
        {
            m_trackDriftHistoryValid = false;
            m_trackSlipRateDegrees = 0f;
            m_trackRecoveryAcceleration = 0f;
            return;
        }

        // ブレーキはアクセルより優先して滑りを小さくする目標へ切り替える
        float targetAllowance = Mathf.Lerp(m_trackCoastSlipDegrees, m_trackPoweredSlipDegrees, Mathf.Clamp01(m_smoothedDriveInput));
        targetAllowance = Mathf.Lerp(targetAllowance, m_trackBrakingSlipDegrees, Mathf.Clamp01(m_brakeInput));
        float step = Mathf.Max(0.0001f, Time.fixedDeltaTime);
        if (!m_trackDriftHistoryValid)
        {
            m_trackPreviousSlipDegrees = m_escSlipAngle;
            m_trackSlipAllowanceDegrees = targetAllowance;
            m_trackDriftHistoryValid = true;
        }

        // 角度の折り返しと物理更新間隔を考慮して角速度を平滑化する
        float rate = Mathf.DeltaAngle(m_trackPreviousSlipDegrees, m_escSlipAngle) / step;
        float smoothing = 1f - Mathf.Exp(-step / Mathf.Max(0.01f, m_trackSlipRateFilterSeconds));
        m_trackSlipRateDegrees = Mathf.Lerp(m_trackSlipRateDegrees, rate, smoothing);
        m_trackPreviousSlipDegrees = m_escSlipAngle;
        m_trackSlipAllowanceDegrees = Mathf.MoveTowards(m_trackSlipAllowanceDegrees, targetAllowance, m_trackSlipAllowanceRate * step);
    }

    // 滑りが増えているときに補い収まり始めたら介入を引く復帰量を求める関数
    static float CalculateTrackRecovery(float _slipDegrees, float _rateDegrees, float _allowance, float _followSeconds, float _damping)
    {
        // 許容幅の内側では横滑りを強制せずタイヤとプレイヤーの操作に任せる
        float excess = Mathf.Abs(_slipDegrees) - _allowance;
        if (excess <= 0f)
        {
            return 0f;
        }

        // 横滑り角の比例項と角速度の減衰項を同じ角加速度の単位で合成する
        float seconds = Mathf.Max(0.2f, _followSeconds);
        float outwardRate = Mathf.Sign(_slipDegrees) * _rateDegrees * Mathf.Deg2Rad;
        float recovery = excess * Mathf.Deg2Rad / (seconds * seconds) + 2f * Mathf.Max(0f, _damping) * outwardRate / seconds;
        return Mathf.Sign(_slipDegrees) * Mathf.Max(0f, recovery);
    }

    // 滑りが拡大する予測量を使い限界に近づくほどESCの緩和を解除する関数
    static float CalculateTrackPoweredESCScale(float _slip, float _rate, float _allowance, float _fullSlip, float _prediction, float _minimum)
    {
        // 回復中の角速度は危険の増加として扱わず拡大している分だけ先読みする
        float outwardRate = Mathf.Max(0f, Mathf.Sign(_slip) * _rate);
        float predictedSlip = Mathf.Abs(_slip) + outwardRate * Mathf.Max(0f, _prediction);
        float danger = Mathf.InverseLerp(_allowance, Mathf.Max(_allowance + 1f, _fullSlip), predictedSlip);
        return Mathf.Lerp(Mathf.Clamp01(_minimum), 1f, danger);
    }

    // 切り返し前の復帰力を残さず必要な補助だけ滑らかに増やす関数
    static float LimitTrackRecovery(float _previous, float _requested, float _rate, float _deltaTime)
    {
        // 回復済みなら補助を解除し逆向きへ滑ったら古い補助を持ち越さない
        if (_requested == 0f)
        {
            return 0f;
        }

        if (_previous * _requested < 0f)
        {
            _previous = 0f;
        }

        // 収束中の要求より強い補助を残さず介入の立ち上がりだけを制限する
        float limited = Mathf.MoveTowards(_previous, _requested, Mathf.Max(0f, _rate) * Mathf.Max(0f, _deltaTime));
        return Mathf.Sign(_requested) * Mathf.Min(Mathf.Abs(limited), Mathf.Abs(_requested));
    }

    // 滑りを戻すカウンター操作だけESCの定常旋回との比較を緩める関数
    static float CalculateTrackCountersteerWeight(float _slip, float _yaw, float _desiredYaw, float _allowance, float _fullSlip)
    {
        // 旋回開始や同方向への切り増しや速度方向と逆の異常旋回は通常ESCを維持する
        if (_yaw * _desiredYaw >= 0f || _yaw * _slip >= 0f)
        {
            return 0f;
        }

        // 小さな角度では通常ESCを残し限界角では保護を全量復帰する
        float drift = Mathf.InverseLerp(2f, Mathf.Max(3f, _allowance), Mathf.Abs(_slip));
        float severe = Mathf.InverseLerp(_allowance, Mathf.Max(_allowance + 1f, _fullSlip), Mathf.Abs(_slip));
        return drift * (1f - severe);
    }
}
