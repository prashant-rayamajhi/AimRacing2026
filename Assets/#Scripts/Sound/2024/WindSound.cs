using UnityEngine;
using FMODUnity;

// 車速を既存のFMOD風イベントへ渡すクラス
public class WindSound : MonoBehaviour
{
    // 風切り音に使う車速を取得する車両
    [SerializeField]
    VehicleController m_vehicle;
    // 既存の風切り音イベント
    [SerializeField]
    EventReference m_eventRef_Wind;
    // 再生中の風音を更新して終了時に解放する参照
    FMOD.Studio.EventInstance m_windEvent;
    // 既存Inspectorの風音量を維持する設定
    [SerializeField, Range(0.0f, 1.0f)]
    float WindVolume = 1.0f;
    // 高速時に風切り音を補強する最大音量差
    [SerializeField, Range(0f, 6f)]
    float m_highSpeedGainDb = 3f;
    // 追加音量が最大になる車速
    [SerializeField, Min(1f)]
    float m_fullWindSpeedKph = 200f;
    // 音量の急変を抑える応答時間
    [SerializeField, Min(0.01f)]
    float m_volumeResponseSeconds = 0.2f;
    // 滑らかな音量変化に使う前回音量
    float m_currentVolume;
    // 同じ再生エラーの連続出力を防ぐ状態
    bool m_reportedSoundError;
    // 初期音量を消してから既存の風音イベントを再生する関数
    void Start()
    {
        if (m_vehicle == null || m_eventRef_Wind.IsNull)
        {
            Debug.LogWarning("[WindSound] 車両またはWindイベントが未設定です。", this);
            enabled = false;
            return;
        }

        m_windEvent = RuntimeManager.CreateInstance(m_eventRef_Wind);
        if (!m_windEvent.isValid())
        {
            Debug.LogWarning("[WindSound] Windイベントを生成できません。バンクとイベント参照を確認してください。", this);
            enabled = false;
            return;
        }

        m_windEvent.setVolume(0f);
        m_windEvent.setParameterByName("SPEED", Mathf.Abs(m_vehicle.KPH));
        RuntimeManager.AttachInstanceToGameObject(m_windEvent, gameObject.transform);
        ReportSoundError(m_windEvent.start());
    }

    // 前進と後退の車速に合わせて風音を滑らかに更新する関数
    void Update()
    {
        if (!m_windEvent.isValid())
        {
            return;
        }

        if (m_vehicle == null)
        {
            m_windEvent.setVolume(0f);
            return;
        }

        // 後退でも負の車速を送らず既存の速度カーブを使う
        float speed = Mathf.Abs(m_vehicle.KPH);
        // 高速時の補強量をデシベルからイベント音量へ変換する
        float gain = Mathf.Lerp(0f, m_highSpeedGainDb, Mathf.Clamp01(speed / Mathf.Max(1f, m_fullWindSpeedKph)));
        float target = m_vehicle.IsPullUp ? 0f : Mathf.Clamp01(WindVolume) * Mathf.Pow(10f, gain / 20f);
        // フレームレートによらず指定時間で音量を追従させる
        float response = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, m_volumeResponseSeconds));
        m_currentVolume = Mathf.Lerp(m_currentVolume, target, response);
        ReportSoundError(m_windEvent.setParameterByName("SPEED", speed));
        // 未定義のWINDVOLではなくイベント音量へInspector設定を渡す
        ReportSoundError(m_windEvent.setVolume(m_currentVolume));
    }

    // 再生接続の不具合を一度だけ報告する関数
    void ReportSoundError(FMOD.RESULT result)
    {
        if (result == FMOD.RESULT.OK || m_reportedSoundError)
        {
            return;
        }

        m_reportedSoundError = true;
        Debug.LogWarning($"[WindSound] 風音の再生または設定に失敗しました: {result}", this);
    }

    // 無効化中に風音だけが鳴り続けないよう停止する関数
    void OnDisable()
    {
        if (m_windEvent.isValid())
        {
            m_windEvent.setPaused(true);
        }
    }

    // 再有効化時に音を重複生成せず再開する関数
    void OnEnable()
    {
        if (m_windEvent.isValid())
        {
            m_windEvent.setPaused(false);
        }
    }

    // シーン終了時に風音の再生とイベント参照を解放する関数
    void OnDestroy()
    {
        if (!m_windEvent.isValid())
        {
            return;
        }

        m_windEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        m_windEvent.release();
    }
}
