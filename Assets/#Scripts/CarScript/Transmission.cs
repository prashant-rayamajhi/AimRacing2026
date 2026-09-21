using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

// ATとMTのギア選択および変速タイミングを管理するクラス
public class Transmission : MonoBehaviour
{
    public enum TransmissionType
    {
        Automatic,
        Manual
    }

    [SerializeField]
    TransmissionType m_type;
    [Range(-1, 7)]
    // 現在のギア
    [SerializeField]
    int m_currentGear;
    // ギア比のリスト
    [SerializeField]
    List<float> m_gearRatioList = new List<float>();
    // 既存シーンのMT設定を残しつつ、AT選択時だけ公式8速比へ切り替える。
    static readonly float[] GRDatGearRatios =
    {
        0f,
        4.435f,
        2.809f,
        1.933f,
        1.497f,
        1.266f,
        1.000f,
        0.793f,
        0.650f
    };
    // 設定されたゲーム用6速比率を未設定車両にも使用する
    static readonly float[] GRMtGearRatios =
    {
        0f,
        2.800f,
        2.400f,
        2.000f,
        1.600f,
        1.200f,
        1.000f
    };
    // 旧比率を残して展示用の段別速度から計算した前進ギア比へ切り替える設定
    [SerializeField]
    bool m_useBalancedForwardSpeeds = true;
    // クラッチが完全につながったMTの基準回転数での各段の理論速度
    [SerializeField]
    float[] m_manualMaximumSpeedsKph =
    {
        60f,
        85f,
        110f,
        130f,
        150f,
        180f
    };
    // ATの前半をMTと共通にし高速側だけ8段へ分ける各段の理論速度
    [SerializeField]
    float[] m_automaticMaximumSpeedsKph =
    {
        60f,
        85f,
        110f,
        130f,
        150f,
        180f,
        205f,
        230f
    };
    // 速度設定をギア比へ変換する基準回転数で実行中のRPM変化には追従させない
    [SerializeField, Min(1f)]
    float m_gearingReferenceRPM = 8200f;
    // 速度設定の基準となるタイヤ半径でサスペンション変位には追従させない
    [SerializeField, Min(0.01f)]
    float m_gearingReferenceWheelRadius = 0.334f;
    // WheelCollider上で1速の到達速度が公称計算値より少し低くなるため、ゲーム用に早めの変速点へ補正する。
    static readonly float[] GRDatShiftUpSpeedKph =
    {
        0f,
        50f,
        85f,
        125f,
        160f,
        188f,
        210f,
        228f,
        float.PositiveInfinity
    };
    // そのギアで走行を維持できる最低RPM
    static readonly float[] GRMinimumDriveRPM =
    {
        0f,
        0f,
        0f,
        1000f,
        2000f,
        3000f,
        3000f
    };
    // 古いシーンのMT比率より設定された6段を優先する設定
    [SerializeField]
    bool m_useRequestedSixSpeedRatios = true;
    // 指定されたMTの最終減速比を駆動と後退速度計算で共有する
    [SerializeField, Min(0.01f)]
    float m_manualFinalDriveRatio = 4.8f;
    // 高いギアの低回転域から滑らかに駆動力を回復させる回転幅
    [SerializeField, Min(1f)]
    float m_lowRPMRecoveryBand = 750f;
    // 低回転で加速が鈍る状態を作り速度や回転数を強制的にゼロへ戻さない
    public float LowRPMDriveRatio => m_type != TransmissionType.Manual || m_currentGear < 3 ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GRMinimumDriveRPM[Mathf.Clamp(m_currentGear, 0, 6)], GRMinimumDriveRPM[Mathf.Clamp(m_currentGear, 0, 6)] + m_lowRPMRecoveryBand, m_engineRPM));

    // ATが実際にダウンシフトを開始するRPM
    static readonly float[] GRDownshiftRPM =
    {
        0f,
        0f,
        1800f,
        2000f,
        2200f,
        3200f
    };
    // 6MTの公式後退ギア比
    const float k_GRMtReverseGearRatio = 3.831f;
    [SerializeField]
    float m_reverseGearRatio = 3.590f;
    // このスピード以下にならないとリバースに入らない
    [SerializeField]
    float m_reverseChangeSpeed = 5f;
    [SerializeField]
    float m_shiftUpInterval = 1f;
    [SerializeField]
    float m_shiftDownInterval = 0.5f;
    // MTで次の変速を受け付けるまでの時間を秒で指定する設定
    [SerializeField, Min(0.01f)]
    float m_gearChangingTime = 0.3f;
    // AT変速時に速度が途切れないように駆動トルクを残す割合
    [SerializeField, Range(0.3f, 0.9f)]
    float m_automaticShiftTorqueRetention = 0.65f;
    // AT変速の駆動力変化を短く滑らかにする時間
    [SerializeField, Range(0.08f, 0.4f)]
    float m_automaticGearChangingTime = 0.18f;
    // ギア切り替えの時間
    [SerializeField, ShowInInspector]
    bool m_isGearChanging;
    float m_lastShiftChangeTime = 0f;
    [Header("AT Settings")]
    [SerializeField]
    // 単位[Km/h]
    List<float> m_shiftUpSpeed = new List<float>();
    [SerializeField, Range(0f, 1f)]
    // スピードの影響度(0.5の場合、必要な速度の半分でシフトアップする)
    float m_speedInfluence = 1f;
    [SerializeField]
    // 全開時は燃料カット直前まで使い変速中に上限へ張り付かないための回転数
    float m_shiftUpEngineRPM = 8000f;
    [SerializeField]
    float m_shiftDownEngineRPM = 3000f;
    [SerializeField]
    float m_shiftDownEngineRPM_1st = 2200f;
    [SerializeField, Range(0.6f, 0.95f)]
    float m_minimumUpshiftSpeedRatio = 0.82f;
    [SerializeField, Range(0.5f, 0.9f)]
    float m_downshiftSpeedRatio = 0.72f;
    [SerializeField, Range(0f, 1f)]
    float m_fullThrottleThreshold = 0.7f;
    [SerializeField, Range(1f, 15f)]
    float m_fullThrottleRestartSpeed = 8f;
    // ATが衝突や急停止後に1速へ直接戻る車速
    [SerializeField, Range(0.5f, 10f)]
    float m_automaticStopDownshiftSpeedKph = 3f;
    // 展示用MTで低速からの再発進を助けるため前進段だけ1速へ戻す車速
    [SerializeField, Range(0f, 40f)]
    float m_manualFirstGearAssistSpeedKph = 30f;
    // 本来のMT操作を使う場合に低速の自動1速化を無効にする設定
    [SerializeField]
    bool m_manualFirstGearAssist = true;
    // 車速から変速先の回転数を求めるため車両側が渡す実タイヤ半径
    float m_shiftWheelRadius;
    // シフトダウン後の過回転を防ぐため車両側が渡すエンジン上限
    float m_shiftMaximumRPM;
    // 変速先の回転数をメーターではなく実際のタイヤ寸法で計算する関数
    public void ConfigureShiftSafety(float wheelRadius, float maximumRPM)
    {
        m_shiftWheelRadius = Mathf.Max(0.01f, wheelRadius);
        m_shiftMaximumRPM = Mathf.Max(1f, maximumRPM);
    }

    // クラッチがつながった時の変速先RPMを車速と減速比から求める関数
    public float GetSynchronizedRPM(int gear, float speedKph)
    {
        if (gear <= 0 || m_shiftWheelRadius <= 0f)
        {
            return 0f;
        }

        float wheelRPM = Mathf.Abs(speedKph) / 3.6f / (2f * Mathf.PI * m_shiftWheelRadius) * 60f;
        return wheelRPM * GetForwardGearRatio(gear) * FinalDriveRatio;
    }

    // クラッチが完全接続して空転しない時の車速を前進段の回転数から求める関数
    public float GetTheoreticalForwardSpeedKph(int gear, float engineRPM, float wheelRadius)
    {
        if (gear <= 0 || gear > MaxForwardGear || engineRPM < 0f || wheelRadius <= 0f)
        {
            return 0f;
        }

        // 無効な計測値を通常の速度として扱わない
        if (float.IsNaN(engineRPM) || float.IsInfinity(engineRPM) || float.IsNaN(wheelRadius) || float.IsInfinity(wheelRadius))
        {
            return 0f;
        }

        float totalRatio = GetForwardGearRatio(gear) * FinalDriveRatio;
        if (totalRatio <= 0f)
        {
            return 0f;
        }

        return engineRPM / totalRatio * (2f * Mathf.PI * wheelRadius) * 60f / 1000f;
    }

    // 低回転のシフトアップは許可し過回転する前進段だけを拒否する関数
    bool IsForwardShiftSafe(int gear)
    {
        // MTの前進段へのダウン操作は車体側の速度補助で過回転を抑えるため受け付ける
        if (m_type == TransmissionType.Manual && gear > 0 && gear < m_currentGear)
        {
            return true;
        }

        if (gear <= 0)
        {
            return true;
        }

        if (m_shiftMaximumRPM <= 0f || m_shiftWheelRadius <= 0f)
        {
            return false;
        }

        return GetSynchronizedRPM(gear, m_vehicleSpeed) <= m_shiftMaximumRPM;
    }

    // 停止時の不要な空ぶかしを避けてATのダウンシフト回転合わせを許可する最低車速
    [SerializeField, Range(0f, 10f)]
    float m_automaticBlipMinimumSpeedKph = 3f;
    // 前進中のATダウンシフトだけ回転合わせを許可する判定
    public bool AutomaticBlipActive => m_type == TransmissionType.Automatic && m_isGearChanging && m_isShiftDown && m_currentGear > 0 && !m_isPullUp && m_vehicleSpeed > m_automaticBlipMinimumSpeedKph;

    [SerializeField, ShowInInspector]
    bool m_isShiftUp = false;
    [SerializeField, ShowInInspector]
    bool m_isShiftDown = false;
    float m_engineRPM;
    float m_vehicleSpeed;
    float m_throttleInput;
    bool m_isPullUp = false;
#region プロパティ
    // AT/MT 切り替え
    public TransmissionType Type
    {
        get => m_type;
        set
        {
            m_type = value;
            m_currentGear = Mathf.Clamp(m_currentGear, -1, MaxForwardGear);
        }
    }

    // 現在のギア比
    public float CurrentGearRatio
    {
        get
        {
            if (m_currentGear >= 0)
            {
                return GetForwardGearRatio(m_currentGear);
            }
            else
            {
                return m_type == TransmissionType.Automatic ? -m_reverseGearRatio : -k_GRMtReverseGearRatio;
            }
        }
    }

    public float FinalDriveRatio
    {
        get
        {
            if (m_type == TransmissionType.Automatic)
            {
                return 3.329f;
            }

            // MT減速比
            return m_manualFinalDriveRatio;
        }
    }

    public int MaxForwardGear => m_type == TransmissionType.Automatic ? GRDatGearRatios.Length - 1 : GetManualGearRatios().Count - 1;

    // 後退ギア比と最終減速比からレブリミット到達時の理論後退速度を返す関数
    public float GetTheoreticalReverseMaximumSpeedKph(float wheelRadius, float maximumEngineRPM)
    {
        // ATとMTに対応する後退ギア比
        float reverseRatio = m_type == TransmissionType.Automatic ? m_reverseGearRatio : k_GRMtReverseGearRatio;
        // レブリミット時のタイヤ回転数
        float wheelRPM = maximumEngineRPM / Mathf.Max(0.01f, reverseRatio * FinalDriveRatio);
        return wheelRPM * 2f * Mathf.PI * Mathf.Max(0.01f, wheelRadius) * 60f / 1000f;
    }

    public int ActiveGear { get => m_currentGear; }
    public bool IsPullUp { set => m_isPullUp = value; }
    public bool IsGearChanging => m_isGearChanging;
    public bool IsShiftUp => m_isShiftUp;
    public bool IsShiftDown => m_isShiftDown;

    // AT変速中の駆動力を段差なく減らして戻す割合
    public float DriveTorqueRatio
    {
        get
        {
            if (m_type != TransmissionType.Automatic || !m_isGearChanging)
            {
                return 1f;
            }

            float progress = Mathf.Clamp01((Time.time - m_lastShiftChangeTime) / Mathf.Max(0.01f, m_automaticGearChangingTime));
            return Mathf.Lerp(1f, m_automaticShiftTorqueRetention, Mathf.Sin(progress * Mathf.PI));
        }
    }

#endregion
    // 初期化処理
    public void Initialize()
    {
    }

    // レース開始前に前進1速を選択してカウント終了直後に発進できるようにする関数
    public void PrepareForwardStart()
    {
        m_automaticNeutralSelected = false;
        m_currentGear = 1;
        m_isGearChanging = false;
        m_isShiftUp = false;
        m_isShiftDown = false;
        m_lastShiftChangeTime = Time.time - m_gearChangingTime;
    }

    // 復帰前に始まった変速処理だけを取り消し、選択中のギアは維持する
    public void ResetDynamics()
    {
        m_engineRPM = 0f;
        m_vehicleSpeed = 0f;
        m_isGearChanging = false;
        m_isShiftUp = false;
        m_isShiftDown = false;
        m_lastShiftChangeTime = Time.time - m_gearChangingTime;
    }

    // VehicleControllerから明示的に呼ぶ更新処理。Unity標準のFixedUpdateとは分ける。
    public void TransmissionUpdate(float _engineRPM, float _speed, float _throttleInput)
    {
        m_engineRPM = _engineRPM;
        m_vehicleSpeed = Mathf.Abs(_speed);
        m_throttleInput = Mathf.Clamp01(_throttleInput);
        switch (m_type)
        {
            case TransmissionType.Automatic:
                AutomaticShift(_speed);
                break;
            case TransmissionType.Manual:
                // クラッチ操作の有無によらず低速の前進段を1速へ戻しNとRは維持する
                if (m_manualFirstGearAssist && !m_isPullUp && m_currentGear >= 2 && m_vehicleSpeed <= m_manualFirstGearAssistSpeedKph)
                {
                    ShiftTo(1);
                }

                break;
        }

        float changingTime = m_type == TransmissionType.Automatic ? m_automaticGearChangingTime : m_gearChangingTime;
        if (changingTime <= Time.time - m_lastShiftChangeTime)
        {
            m_isGearChanging = false;
            m_isShiftUp = false;
            m_isShiftDown = false;
        }
        else
        {
            m_isGearChanging = true;
        }
    }

    // 衝突後の車速に合わせてATの前進ギアを安全な段まで下げる関数
    public void SynchronizeAutomaticGearAfterImpact(float _speed)
    {
        if (m_type != TransmissionType.Automatic || m_currentGear <= 1)
        {
            return;
        }

        // 壁との接触で実際に残った車速から再加速に適したギアを求める
        float speed = Mathf.Abs(_speed);
        int targetGear = GetAutomaticRecoveryGear(speed);
        if (targetGear < m_currentGear)
        {
            ShiftTo(targetGear);
        }
    }

    // シフトアップより低い車速境界を使い、往復変速を防ぎながら復帰先の前進段を求める関数
    int GetAutomaticRecoveryGear(float speed)
    {
        // 停止付近はアクセルとRPMに関係なく発進用の1速を選ぶ
        if (speed <= m_automaticStopDownshiftSpeedKph)
        {
            return 1;
        }

        // 急減速で複数段が合わなくなった時も、一度の変速で車速に合う段まで戻す
        int targetGear = m_currentGear;
        while (targetGear > 1 && speed < GetAutomaticShiftSpeed(targetGear - 1) * m_downshiftSpeedRatio)
        {
            targetGear--;
        }

        return targetGear;
    }

    bool ShouldDownshiftForLowRPM()
    {
        if (m_currentGear <= 1)
        {
            return false;
        }

        int gear = Mathf.Clamp(m_currentGear, 0, GRDownshiftRPM.Length - 1);
        float requiredRPM = GRDownshiftRPM[gear];
        // アクセルを踏んでいるほど早めにダウンシフト
        float throttleBias = Mathf.Lerp(0f, 300f, m_throttleInput);
        return m_engineRPM < requiredRPM + throttleBias;
    }

    // 前進ATだけを車速と回転数に合わせて自動変速する関数
    void AutomaticShift(float _speed)
    {
        // NとRの時は処理しない
        if (m_currentGear <= 0)
        {
            return;
        }

        // 衝突や急停止後はエンジン回転数を待たず1速へ戻して再発進を遅らせない
        if (Mathf.Abs(_speed) <= m_automaticStopDownshiftSpeedKph && m_currentGear > 1)
        {
            ShiftTo(1);
            return;
        }

        // 接触後の空転やクラッチ滑りでRPMが高くても、実車速に合う段へ戻して再加速を遅らせない
        int recoveryGear = GetAutomaticRecoveryGear(m_vehicleSpeed);
        if (recoveryGear < m_currentGear && Time.time - m_lastShiftChangeTime >= m_shiftDownInterval)
        {
            ShiftTo(recoveryGear);
            return;
        }

        // シフトアップの条件
        // ・エンジンRPMがシフトアップRPMを超えている
        // ・現在の車速がスピード * 影響度の値を超えている
        // ・シフトアップ待機時間を過ぎている
        float shiftSpeed = GetAutomaticShiftSpeed(m_currentGear);
        // 上限を固定値で切らず車両が渡した実エンジン上限に合わせる
        float shiftRPM = m_shiftMaximumRPM > 0f ? Mathf.Min(m_shiftUpEngineRPM, m_shiftMaximumRPM) : m_shiftUpEngineRPM;
        bool reachedPowerPeak = m_engineRPM >= shiftRPM;
        // 全開時は旧車速表だけで早上がりせず軽いアクセル時のみ車速による早期変速を許す
        bool reachedGearSpeed = m_throttleInput < m_fullThrottleThreshold && _speed >= shiftSpeed * m_speedInfluence;
        bool reachedMinimumUpshiftSpeed = _speed >= shiftSpeed * m_minimumUpshiftSpeedRatio;
        float downshiftSpeed = GetAutomaticShiftSpeed(m_currentGear - 1) * m_downshiftSpeedRatio;
        bool fullThrottleAllowsDownshift = m_throttleInput < m_fullThrottleThreshold || _speed < m_fullThrottleRestartSpeed;
        if (m_currentGear < MaxForwardGear && reachedMinimumUpshiftSpeed && (reachedPowerPeak || reachedGearSpeed) && m_shiftUpInterval <= Time.time - m_lastShiftChangeTime)
        {
            ShiftUp();
        }

        // 低回転時は、速度条件より先に適切なギアへ戻す
        if (ShouldDownshiftForLowRPM() && m_shiftDownInterval <= Time.time - m_lastShiftChangeTime)
        {
            ShiftDown();
            return;
        }

        // シフトダウンの条件
        // ・エンジンRPMがシフトダウンRPMを下回っている
        // ・1速ではない
        // ・現在の車速がひとつ前のシフトチェンジの速度を下回ってる
        // ・シフトダウン待機時間を過ぎている
        if (!m_isShiftUp && m_engineRPM < m_shiftDownEngineRPM && m_currentGear != 1 && _speed < downshiftSpeed && fullThrottleAllowsDownshift && m_shiftDownInterval <= Time.time - m_lastShiftChangeTime)
        {
            // 2速の時は別のシフトダウンRPMを使う
            if (m_currentGear != 2)
            {
                ShiftDown();
            }
            else if (m_engineRPM < m_shiftDownEngineRPM_1st)
            {
                ShiftDown();
            }
        }
    }

    void ManualShift()
    {
        if (Input.GetButtonDown("ShiftUp"))
            ShiftUp();
        if (Input.GetButtonDown("ShiftDown"))
            ShiftDown();
    }

    // 停止とみなす微小な車速の上限で前後方向を問わず切替を保護する
    [SerializeField, Range(0.05f, 1f)]
    float m_automaticSelectorStopSpeedKph = 0.3f;
    // プレイヤーが選んだNをアクセルによる発進補助から保護する状態
    bool m_automaticNeutralSelected;
    // 明示的に選んだATのNを維持する必要があるか返すプロパティ
    public bool AutomaticNeutralSelected => m_type == TransmissionType.Automatic && m_automaticNeutralSelected;

    // 停止中だけATの走行方向をRとNと1速の順で一段選択する関数
    public bool RequestAutomaticSelector(bool up, float actualSpeedKph)
    {
        if (m_type != TransmissionType.Automatic || m_isPullUp)
        {
            return false;
        }

        // 後退中や横滑り中の入力も停止として扱わないよう車体の速度の大きさを使う
        if (float.IsNaN(actualSpeedKph) || float.IsInfinity(actualSpeedKph))
        {
            return false;
        }

        if (Mathf.Abs(actualSpeedKph) > m_automaticSelectorStopSpeedKph)
        {
            return false;
        }

        if (Time.time - m_lastShiftChangeTime < Mathf.Max(0.01f, m_gearChangingTime))
        {
            return false;
        }

        // 停止直後に高い段が残っていても前進位置を1速として扱う
        int selected = Mathf.Clamp(m_currentGear, -1, 1);
        int target = Mathf.Clamp(selected + (up ? 1 : -1), -1, 1);
        if (target == m_currentGear)
        {
            return false;
        }

        // 過回転と変速通知の共通処理を使いATの種類自体は変更しない
        ShiftTo(target);
        if (m_currentGear != target)
        {
            return false;
        }

        m_automaticNeutralSelected = target == 0;
        return true;
    }

    public void ShiftUp()
    {
        // 開始前ならギアチェンジを無効化
        if (m_isPullUp)
        {
            return;
        }

        // MTの連打と最高段での空操作が変速時間を更新することを防ぐ
        if (ManualShiftLocked || m_currentGear >= MaxForwardGear)
        {
            return;
        }

        // 停止時のNから1速を妨げず走行中の過回転だけを防ぐ
        if (!IsForwardShiftSafe(m_currentGear + 1))
        {
            return;
        }

        // 経過時間(現在の時間 - 保持した時間)が待機時間を上回っていたら
        if (m_gearChangingTime <= Time.time - m_lastShiftChangeTime || m_currentGear <= 0)
        {
            m_currentGear++;
            m_isShiftUp = true;
            m_isShiftDown = false;
            m_isGearChanging = true;
            // ギア切り替え時の時間を保持
            m_lastShiftChangeTime = Time.time;
        }

        // ギア比リストの要素数をオーバーフローしないように
        if (m_currentGear > MaxForwardGear)
        {
            m_currentGear = MaxForwardGear;
        }
    }

    public void ShiftDown()
    {
        // 開始前ならギアチェンジを無効化
        if (m_isPullUp)
        {
            return;
        }

        // MTではNとRの切り替えにも同じ待ち時間を適用する
        if (ManualShiftLocked || m_currentGear <= -1)
        {
            return;
        }

        if (!IsForwardShiftSafe(m_currentGear - 1))
        {
            return;
        }

        // 前進中の誤操作で駆動方向が反転すると、クラッチとタイヤに不自然な負回転が出るため。
        if (m_currentGear == 0 && m_vehicleSpeed > m_reverseChangeSpeed)
        {
            return;
        }

        // 経過時間(現在の時間 - 保持した時間)が待機時間を上回っていたら
        if (m_gearChangingTime <= Time.time - m_lastShiftChangeTime || m_currentGear <= 1)
        {
            m_currentGear--;
            m_isShiftDown = true;
            m_isShiftUp = false;
            m_isGearChanging = true;
            // ギア切り替え時の時間を保持
            m_lastShiftChangeTime = Time.time;
        }

        // 1を下回らないように補正
        if (m_currentGear < -1)
        {
            m_currentGear = -1;
        }
    }

    public void ShiftTo(int _gear)
    {
        // スタート前の車体固定中は外部処理から前進または後退ギアへ変更させない
        if (m_isPullUp)
        {
            return;
        }

        // 低速の1速補助も進行中のMT変速が終わってから実行する
        if (ManualShiftLocked)
        {
            return;
        }

        // ATとMTそれぞれのギア数を超えない範囲へ指定値を収める
        int targetGear = Mathf.Clamp(_gear, -1, MaxForwardGear);
        if (targetGear == m_currentGear)
        {
            return;
        }

        // 直接段を指定する操作も逐次変速と同じ過回転と後退の保護を通す
        if (!IsForwardShiftSafe(targetGear))
        {
            return;
        }

        if (targetGear < 0 && m_vehicleSpeed > m_reverseChangeSpeed)
        {
            return;
        }

        // メーターとクラッチが変速方向を正しく判断できるように状態を更新する
        m_isShiftUp = targetGear > m_currentGear;
        m_isShiftDown = targetGear < m_currentGear;
        m_currentGear = targetGear;
        m_isGearChanging = true;
        m_lastShiftChangeTime = Time.time;
    }

    // フレーム数に依存せずMT変速中の追加要求を受け付けない判定
    // 実クラッチによる変速状態を診断側へ伝える値で低速1速補助とは分ける
    public bool ManualClutchControlled { get; set; }

    bool ManualShiftLocked => m_type == TransmissionType.Manual && Time.time - m_lastShiftChangeTime < Mathf.Max(0.01f, m_gearChangingTime);

    float GetForwardGearRatio(int gear)
    {
        // Nは駆動を伝えず前進段だけ新しい速度配分を適用する
        if (gear == 0)
        {
            return 0f;
        }

        float targetSpeed = GetBalancedMaximumSpeedKph(gear);
        if (targetSpeed > 0f)
        {
            // 車速とタイヤ外周から逆算して速度と駆動トルクの両方に同じギア比を使う
            float wheelTravelPerMinute = m_gearingReferenceRPM * 2f * Mathf.PI * m_gearingReferenceWheelRadius;
            return wheelTravelPerMinute * 60f / (1000f * targetSpeed * FinalDriveRatio);
        }

        if (m_type == TransmissionType.Automatic)
        {
            return GRDatGearRatios[Mathf.Clamp(gear, 0, GRDatGearRatios.Length - 1)];
        }

        IReadOnlyList<float> manualRatios = GetManualGearRatios();
        return manualRatios[Mathf.Clamp(gear, 0, manualRatios.Count - 1)];
    }

    // シーン側のMTギア比が不足している時だけGRヤリス6MTの予備値を返す関数
    IReadOnlyList<float> GetManualGearRatios()
    {
        if (m_useRequestedSixSpeedRatios)
        {
            return GRMtGearRatios;
        }

        return m_gearRatioList != null && m_gearRatioList.Count >= GRMtGearRatios.Length ? m_gearRatioList : GRMtGearRatios;
    }

    float GetAutomaticShiftSpeed(int gear)
    {
        // 旧速度表によって新しいギアで変速できなくなることを防ぐ
        if (m_type == TransmissionType.Automatic && GetBalancedMaximumSpeedKph(gear) > 0f)
        {
            float radius = m_shiftWheelRadius > 0f ? m_shiftWheelRadius : m_gearingReferenceWheelRadius;
            float maximumRPM = m_shiftMaximumRPM > 0f ? m_shiftMaximumRPM : m_gearingReferenceRPM;
            return GetTheoreticalForwardSpeedKph(gear, Mathf.Min(m_shiftUpEngineRPM, maximumRPM), radius);
        }

        return GRDatShiftUpSpeedKph[Mathf.Clamp(gear, 0, GRDatShiftUpSpeedKph.Length - 1)];
    }

    // 設定不足や逆転した速度表を使わず有効な前進段の目標速度だけ取得する関数
    float GetBalancedMaximumSpeedKph(int gear)
    {
        if (!m_useBalancedForwardSpeeds || gear <= 0)
        {
            return 0f;
        }

        float[] speeds = m_type == TransmissionType.Automatic ? m_automaticMaximumSpeedsKph : m_manualMaximumSpeedsKph;
        int count = m_type == TransmissionType.Automatic ? GRDatGearRatios.Length - 1 : GetManualGearRatios().Count - 1;
        if (speeds == null || speeds.Length != count || gear > count)
        {
            return 0f;
        }

        if (m_gearingReferenceRPM <= 0f || m_gearingReferenceWheelRadius <= 0f || FinalDriveRatio <= 0f)
        {
            return 0f;
        }

        // 一部の段だけ旧比率へ戻って順序が逆転しないよう速度表全体を検査する
        float previousSpeed = 0f;
        foreach (float speed in speeds)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= previousSpeed)
            {
                return 0f;
            }

            previousSpeed = speed;
        }

        return speeds[gear - 1];
    }
}
