using UnityEngine;

// エンジン回転数に合わせてメーターのLEDを順番に点灯するクラス
public class LED_IndicatorController : MonoBehaviour
{
    // 左から右へ点灯させる既存のLED参照
    [SerializeField]
    private LED_Controller[] m_LED_List;
    // ATの変速前にも赤色が見えるようにした各LEDの点灯開始回転数
    [SerializeField]
    private float[] m_activationRPM =
    {
        3000f,
        3400f,
        3800f,
        4200f,
        4600f,
        5000f,
        5400f,
        5600f,
        5800f
    };
    // 回転数が増えていることを点滅で伝える末尾のLED数
    private const int BlinkingLEDCount = 2;
    // メーター表示中だけ回転数を反映するための状態
    private bool m_isActive;
    // 開始待機の回転上限を走行中のレッドゾーンと区別する車両参照
    [SerializeField]
    private VehicleController m_vehicle;
    // 全LEDを同時に点灯と消灯へ切り替える毎秒の周期数
    [SerializeField, Range(0.5f, 3f)]
    private float m_countdownBlinkFrequencyHz = 2.5f;
    // 燃料カット直後の小さな回転低下で点滅を途切れさせない許容回転差
    [SerializeField, Min(0f)]
    private float m_countdownBlinkMarginRPM = 150f;
    // 全LEDの点滅開始を揃え最初の半周期を点灯するための状態
    private bool m_countdownBlinking;
    private float m_countdownBlinkStartedAt;
    // LEDの参照と設定を確認して初期表示を消灯する関数
    public void Init()
    {
        ValidateThresholds();
        // 既存シーンへ手動で参照を追加しなくても車両の開始待機状態を取得する
        if (m_vehicle == null)
        {
            m_vehicle = FindFirstObjectByType<VehicleController>();
        }

        m_countdownBlinking = false;
        if (m_LED_List == null)
        {
            return;
        }

        // 未設定のLEDがあっても残りのメーター表示を止めない
        for (int index = 0; index < m_LED_List.Length; index++)
        {
            if (m_LED_List[index] == null)
            {
                continue;
            }

            m_LED_List[index].Init();
        }
    }

    // 現在の回転数までLEDを点灯し末尾のLEDを点滅する関数
    public void Run(float engineRPM)
    {
        if (!m_isActive || m_LED_List == null)
        {
            return;
        }

        // 開始待機中の全開上限では末尾だけの点滅より全LEDの同期点滅を優先する
        bool countdownLimit = m_vehicle != null && m_vehicle.Engine != null && ShouldBlinkCountdown(m_vehicle.IsPullUp, m_vehicle.Accel, engineRPM, m_vehicle.Engine.TemporaryRevLimitRPM, m_countdownBlinkMarginRPM);
        if (countdownLimit)
        {
            if (!m_countdownBlinking)
            {
                m_countdownBlinkStartedAt = Time.unscaledTime;
            }

            m_countdownBlinking = true;
            bool lit = CountdownLightOn(Time.unscaledTime - m_countdownBlinkStartedAt, m_countdownBlinkFrequencyHz);
            for (int index = 0; index < m_LED_List.Length; index++)
            {
                // 各LEDの色を残し共通の周期で同時に表示を切り替える
                LED_Controller led = m_LED_List[index];
                if (led == null)
                {
                    continue;
                }

                if (lit)
                {
                    led.Lit();
                }
                else
                {
                    led.LightOut();
                }
            }

            return;
        }

        // アクセルを離した時と発進後は元の回転数別表示へ戻す
        m_countdownBlinking = false;
        // 上限や境界値でも表示が消えないように下限だけで点灯数を決める
        int activeCount = 0;
        if (m_activationRPM != null && !float.IsNaN(engineRPM) && !float.IsInfinity(engineRPM))
        {
            int configuredCount = Mathf.Min(m_LED_List.Length, m_activationRPM.Length);
            for (int index = 0; index < configuredCount; index++)
            {
                if (engineRPM < m_activationRPM[index])
                {
                    break;
                }

                activeCount = index + 1;
            }
        }

        // 回転数が下がった時は不要なLEDを消して変速後の状態を表示する
        for (int index = 0; index < m_LED_List.Length; index++)
        {
            // この位置の点灯と点滅を切り替えるLED参照
            LED_Controller led = m_LED_List[index];
            if (led == null)
            {
                continue;
            }

            if (index >= activeCount)
            {
                led.LightOut();
            }
            else if (index >= activeCount - BlinkingLEDCount)
            {
                led.Blink();
            }
            else
            {
                led.Lit();
            }
        }
    }

    // メーター表示開始時に回転数の反映を許可する関数
    public void Starting()
    {
        m_isActive = true;
    }

    // メーター停止後に警告LEDが残らないよう消灯する関数
    public void Stop()
    {
        m_isActive = false;
        // 次の開始時に前回の点滅位相を持ち越さない
        m_countdownBlinking = false;
        if (m_LED_List == null)
        {
            return;
        }

        for (int index = 0; index < m_LED_List.Length; index++)
        {
            if (m_LED_List[index] != null)
            {
                m_LED_List[index].LightOut();
            }
        }
    }

    // 全開で開始待機の回転上限付近にいる場合だけ同期点滅を許可する関数
    internal static bool ShouldBlinkCountdown(bool waiting, float throttle, float rpm, float limit, float margin)
    {
        return waiting && throttle >= 0.99f && limit > 0f && rpm >= limit - Mathf.Clamp(margin, 0f, limit * 0.1f);
    }

    // 波状の明滅を使わず半周期ごとに点灯と消灯を切り替える関数
    internal static bool CountdownLightOn(float elapsed, float frequency)
    {
        return Mathf.Repeat(Mathf.Max(0f, elapsed) * Mathf.Clamp(frequency, 0.5f, 3f), 1f) < 0.5f;
    }

    // Inspectorで変更した回転数を昇順の有効な値へ整える関数
    private void OnValidate()
    {
        ValidateThresholds();
    }

    // 負数や逆順の設定でLEDの点灯順序が崩れないようにする関数
    private void ValidateThresholds()
    {
        if (m_activationRPM == null)
        {
            return;
        }

        // 直前の点灯回転数より小さい設定を防ぐための下限
        float minimumRPM = 0f;
        for (int index = 0; index < m_activationRPM.Length; index++)
        {
            // Inspectorの入力値を検証するための回転数
            float threshold = m_activationRPM[index];
            if (float.IsNaN(threshold) || float.IsInfinity(threshold))
            {
                threshold = minimumRPM;
            }

            m_activationRPM[index] = Mathf.Max(minimumRPM, threshold);
            minimumRPM = m_activationRPM[index];
        }
    }
}
