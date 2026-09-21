using FMODUnity;
using UnityEngine;

public class TireSound : MonoBehaviour
{
    // // インスタンス作成
    // // スリップ度合は縦方向のスリップ速度と横方向の速度のベクトルの大きさで決める
    // // スリップパラメータを設定
    // // 地面に接触していないときは切る
    [Header("References")]
    [SerializeField]
    VehicleController m_vehicleController;
    [SerializeField]
    WheelController2026 m_wheelController;
    [SerializeField]
    // 名前変更前のシーンとPrefabに保存された音源設定を引き継ぐ
    [UnityEngine.Serialization.FormerlySerializedAs("m_eventName")]
    EventReference m_tireEventName;
    [Header("Corner Squeal")]
    [SerializeField]
    float m_cornerSpeedKph = 80f;
    // 車輪の最大横力が出る滑り量を基準に音を出し始める割合
    [SerializeField, Range(0.01f, 0.9f)]
    float m_cornerPeakSlipStartRatio = 0.15f;
    // 最大横力へ近づいた時に音を強めモードごとの摩擦曲線にも追従させる割合
    [SerializeField, Range(0.5f, 2f)]
    float m_cornerPeakSlipFullRatio = 1f;
    [Header("Brake Squeal")]
    [SerializeField]
    float m_brakeSlipStart = 0.05f;
    [SerializeField]
    float m_brakeSlipFull = 0.16f;
    // 低速の低いスキール音から高速の高い音へ移る速度範囲
    [SerializeField]
    Vector2 m_squealPitchSpeedKph = new Vector2(30f, 150f);
    [Header("Debug")]
    [SerializeField, ShowInInspector]
    float m_speedKph;
    [SerializeField, ShowInInspector]
    float m_maxSideSlip;
    [SerializeField, ShowInInspector]
    float m_maxBrakeSlip;
    [SerializeField, ShowInInspector]
    float m_slipVol;
    [SerializeField, ShowInInspector]
    float m_slipPitch;
    FMOD.Studio.EventInstance m_tireEventInst;
    // 既存Tire音源の音量カーブに合わせて線形音量を変換する下限と上限
    [SerializeField]
    float m_bankSilentDecibels = -80f;
    [SerializeField]
    float m_bankFullDecibels = -6.50725f;
    // 滑り音の急な入り切りを抑える立ち上がり時間と余韻
    [SerializeField, Min(0.01f)]
    float m_soundAttackSeconds = 0.04f;
    [SerializeField, Min(0.01f)]
    float m_soundReleaseSeconds = 0.12f;
    // FMODへ送る音量を滑らかにするための前回音量
    float m_audibleVolume;
    // 無効なパラメーター名による警告の大量発生を防ぐ状態
    bool m_parameterErrorReported;
    // 実走検証で音量の計算結果とFMODへの送信結果を分けて読む値
    public float RequestedVolume => m_slipVol;
    public float SentVolumeParameter { get; private set; }

    // 振幅比をデシベルへ換算し既存バンクの音量カーブへ対応させる関数
    float CalculateVolumeParameter(float amplitude)
    {
        if (amplitude <= 0.0001f)
        {
            return 0f;
        }

        float decibels = m_bankFullDecibels + 20f * Mathf.Log10(Mathf.Clamp01(amplitude));
        return Mathf.InverseLerp(m_bankSilentDecibels, m_bankFullDecibels, decibels);
    }

    // 発進の瞬間だけ音を加えタイヤの物理摩擦は変更しない
    [SerializeField, Range(0.1f, 1f)]
    float m_launchSquealSeconds = 0.4f;
    bool m_wasPullUp;
    float m_launchSquealRemaining;
    // 各輪の音源が別のタイヤの滑りまで重複再生しないための対象番号
    [SerializeField, Range(-1, 3)]
    int m_soundWheelIndex = -1;
    // 旧車輪参照を使わず車両本体の現行車輪制御と音源の対象輪を取得する関数
    void ResolveVehicleReferences()
    {
        if (m_vehicleController == null)
        {
            m_vehicleController = GetComponentInParent<VehicleController>();
        }

        if (m_vehicleController != null)
        {
            m_wheelController = m_vehicleController.WheelComtroller;
        }

        if (m_wheelController == null)
        {
            m_wheelController = GetComponentInParent<WheelController2026>();
        }

        if (m_soundWheelIndex >= 0)
        {
            return;
        }

        switch (gameObject.name)
        {
            case "Tire_FR":
                m_soundWheelIndex = 0;
                break;
            case "Tire_FL":
                m_soundWheelIndex = 1;
                break;
            case "Tire_RR":
                m_soundWheelIndex = 2;
                break;
            case "Tire_RL":
                m_soundWheelIndex = 3;
                break;
        }
    }

    void Reset()
    {
        m_vehicleController = GetComponentInParent<VehicleController>();
        m_wheelController = GetComponent<WheelController2026>();
    }

    // 車両のAwakeによる参照準備後に音源を作成する関数
    void Start()
    {
        if (m_vehicleController == null)
        {
            m_vehicleController = GetComponentInParent<VehicleController>();
        }

        if (m_wheelController == null)
        {
            m_wheelController = GetComponent<WheelController2026>();
        }

        ResolveVehicleReferences();
        if (m_vehicleController == null || m_wheelController == null || m_tireEventName.IsNull)
        {
            AppLog.LogError($"[TireSound] {name}: 車両参照またはTire Event Nameが未設定です。", this);
            enabled = false;
            return;
        }

        // 設定済みイベントがバンクにない場合も音側だけを止めて原因を一度通知する
        try
        {
            m_tireEventInst = RuntimeManager.CreateInstance(m_tireEventName);
        }
        catch (EventNotFoundException exception)
        {
            AppLog.LogError($"[TireSound] {name}: バンク内に設定された音源がありません。{exception.Message}", this);
            enabled = false;
            return;
        }

        RuntimeManager.AttachInstanceToGameObject(m_tireEventInst, transform);
        m_tireEventInst.start();
    }

    void FixedUpdate()
    {
        if (m_vehicleController == null || m_wheelController == null)
        {
            return;
        }

        // 作成できなかった音源へ毎フレーム値を送らない
        if (!m_tireEventInst.isValid())
        {
            return;
        }

        // 後退中も前進と同じ速度基準で音量と音程を計算する
        m_speedKph = Mathf.Abs(m_vehicleController.KPH);
        CalculateTireSound();
        // 線形音量をそのままデシベルのカーブへ渡して小音量が消えることを防ぐ
        float responseSeconds = m_slipVol > m_audibleVolume ? m_soundAttackSeconds : m_soundReleaseSeconds;
        float response = 1f - Mathf.Exp(-Time.fixedDeltaTime / Mathf.Max(0.01f, responseSeconds));
        m_audibleVolume = Mathf.Lerp(m_audibleVolume, m_slipVol, response);
        // 空中と発進待機では余韻を残さず摩擦音を止める
        bool grounded = m_soundWheelIndex < 0 ? m_vehicleController.GroundedWheelCount > 0 : m_wheelController.IsGrounded(m_soundWheelIndex);
        if (m_vehicleController.IsPullUp || !grounded)
        {
            m_audibleVolume = 0f;
        }

        SentVolumeParameter = CalculateVolumeParameter(m_audibleVolume);
        FMOD.RESULT volumeResult = m_tireEventInst.setParameterByName("SLIP_VOL", SentVolumeParameter);
        FMOD.RESULT pitchResult = m_tireEventInst.setParameterByName("SLIP_PITCH", m_slipPitch);
        // バンクとコードの設定名が違う場合に黙って無音にならないよう一度通知する
        if (!m_parameterErrorReported && (volumeResult != FMOD.RESULT.OK || pitchResult != FMOD.RESULT.OK))
        {
            AppLog.LogError($"[TireSound] {name}: 音量設定={volumeResult}, 音程設定={pitchResult}", this);
            m_parameterErrorReported = true;
        }
    }

    void CalculateTireSound()
    {
        float maximumSideSlip = 0f;
        float maximumBrakeSlip = 0f;
        // 生の滑り量ではなく対象輪の摩擦曲線に対する音量要求を集計する
        float cornerAmount = 0f;
        // 待機解除を一度だけ検出し発進音の長さを制限する
        if (m_wasPullUp && !m_vehicleController.IsPullUp && m_vehicleController.Accel > 0.03f)
        {
            m_launchSquealRemaining = m_launchSquealSeconds;
        }

        m_wasPullUp = m_vehicleController.IsPullUp;
        m_launchSquealRemaining = Mathf.Max(0f, m_launchSquealRemaining - Time.fixedDeltaTime);
        for (int i = 0; i < m_wheelController.WheelCount; i++)
        {
            // 四輪個別の音源はその車輪だけを測り全輪の音量を四重にしない
            if (m_soundWheelIndex >= 0 && i != m_soundWheelIndex)
            {
                continue;
            }

            if (!m_wheelController.IsGrounded(i))
            {
                continue;
            }

            float sideSlip = Mathf.Abs(m_wheelController.GetSidewaysSlip(i));
            float forwardSlip = m_wheelController.GetForwardSlip(i);
            maximumSideSlip = Mathf.Max(maximumSideSlip, sideSlip);
            // 後輪の摩擦曲線が広がるTrackでも同じ限界割合でタイヤの踏ん張りを伝える
            float peakSlip = m_wheelController.GetSidewaysPeakSlip(i);
            float startSlip = peakSlip * m_cornerPeakSlipStartRatio;
            float fullSlip = Mathf.Max(startSlip + 0.001f, peakSlip * m_cornerPeakSlipFullRatio);
            cornerAmount = Mathf.Max(cornerAmount, Mathf.InverseLerp(startSlip, fullSlip, sideSlip));
            // 前後進で滑りの符号が変わってもブレーキ入力中だけ制動音へ渡す
            float brakeSlip = m_vehicleController.Brake > 0.01f ? Mathf.Abs(forwardSlip) : 0f;
            maximumBrakeSlip = Mathf.Max(maximumBrakeSlip, brakeSlip);
        }

        m_maxSideSlip = maximumSideSlip;
        m_maxBrakeSlip = maximumBrakeSlip;
        // コーナースキール
        // 各輪の実際の摩擦曲線から上で求めた音量を使用する
        // 80km/h以上でのみコーナースキール
        if (m_speedKph >= m_cornerSpeedKph)
        {
            cornerAmount = Mathf.Clamp01(cornerAmount);
        }
        else
        {
            cornerAmount = 0f;
        }

        // ブレーキキーキー
        // 停止付近で滑り率だけが大きくなっても強い摩擦音を出さない
        float brakeSpeedRatio = Mathf.Clamp01(m_speedKph / Mathf.Max(1f, m_squealPitchSpeedKph.x));
        float brakeAmount = Mathf.InverseLerp(m_brakeSlipStart, m_brakeSlipFull, maximumBrakeSlip) * brakeSpeedRatio;
        // SLIP_VOL
        // コーナーとブレーキの強い方を採用
        m_slipVol = Mathf.Max(cornerAmount, brakeAmount);
        // 80km/h未満ではSlip音を完全にOFF
        // 80km/h未満でも制動音を残し発進時は接地中だけ短いスキール音を加える
        if (m_launchSquealRemaining > 0f && m_vehicleController.GroundedWheelCount > 0)
        {
            m_slipVol = Mathf.Max(m_slipVol, m_launchSquealRemaining / m_launchSquealSeconds);
        }

        if (m_vehicleController.IsPullUp || m_vehicleController.GroundedWheelCount == 0)
        {
            m_slipVol = 0f;
        }

        // ABS作動中は少しだけ鳴らす
        if (m_wheelController.AnyABSActive && m_vehicleController.Brake > 0.01f)
        {
            // 停止直前まで一定音量が残らないようABS音にも速度を反映する
            m_slipVol = Mathf.Max(m_slipVol, 0.25f * brakeSpeedRatio);
        }

        // ABSの音量補助より後で接地を判定し浮いた車輪や待機中には摩擦音を鳴らさない
        bool soundWheelGrounded = m_soundWheelIndex < 0 ? m_vehicleController.GroundedWheelCount > 0 : m_wheelController.IsGrounded(m_soundWheelIndex);
        if (m_vehicleController.IsPullUp || !soundWheelGrounded)
        {
            m_slipVol = 0f;
        }

        // SLIP_PITCH
        // 滑り量は音量へ使い音の高低は走行速度で切り替える
        float fullPitchSpeed = Mathf.Max(m_squealPitchSpeedKph.x + 1f, m_squealPitchSpeedKph.y);
        m_slipPitch = Mathf.InverseLerp(m_squealPitchSpeedKph.x, fullPitchSpeed, Mathf.Abs(m_speedKph));
        // 0～1に制限
        m_slipPitch = Mathf.Clamp01(m_slipPitch);
    }

    void OnDestroy()
    {
        if (m_tireEventInst.isValid())
        {
            m_tireEventInst.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            m_tireEventInst.release();
        }
    }
}
