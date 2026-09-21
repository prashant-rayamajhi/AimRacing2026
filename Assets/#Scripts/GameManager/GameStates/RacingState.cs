// 走行中Racing(旧Ingame)の新エンジン版State実装
using System.Collections;
using UnityEngine;

public class RacingState : GameStateBindingBase
{
    private const int k_InitialState = 0; // ゴール処理前の状態
    private const int k_GoalState = 1; // ゴール処理開始後の状態
    private const float k_GoalFadeDelaySeconds = 0f; // ゴール表示からフェードまでの待機秒数(余分な走行を見せないため即座にFadeOutする)
    [SerializeField]
    private VehicleController m_vehicle;
    [SerializeField]
    private Line_StartFinish m_finishLine;
    [SerializeField]
    private ChairController m_chairController;
    [SerializeField]
    private MeterUIManager m_meterUIManager;
    [SerializeField]
    private GameObject m_okutamaNameDisplay; // IntroStateと同一オブジェクトを参照。EmergencyReset(F2)でIntroを飛ばした場合の非表示安全網
    private Coroutine m_coroutine; // ゴール後フェード処理
    private int m_state; // 現在のゴール処理状態
    public override GameStateId Id => GameStateId.Racing;

    public override void Enter()
    {
        base.Enter();
        m_state = k_InitialState;
        m_coroutine = null;
        m_chairController.InGameMove = true;
        m_meterUIManager.ShowMeter();
        if (m_okutamaNameDisplay != null)
        {
            m_okutamaNameDisplay.SetActive(false);
        } // Intro専用表示。通常はIntroState側で既に隠れているが、直接Racingへ入った場合に備えて強制非表示にする

        m_vehicle.PullUp(true);
        if (GameManager.Instance.DrivingSettings.isAT)
        {
            m_vehicle.Transmission.Type = Transmission.TransmissionType.Automatic;
        }
        else
        {
            m_vehicle.Transmission.Type = Transmission.TransmissionType.Manual;
        }
    }

    public override void Tick(float _deltaTime)
    {
        if (m_finishLine.IsChecked) // ゴール時
        {
            if (m_state == k_InitialState)
            {
                Music.Resume();
                SoundManager.Instance.PlaySE(SoundManager.SE_Type.Goal);
                m_state = k_GoalState;
                // FFB停止に失敗しても(ハンドル切断・SDK未接続等)ゴール処理自体は必ず進める
                try
                {
                    if (LogitechGSDK.LogiUpdate() && LogitechGSDK.LogiIsConnected(0))
                    {
                        LogitechGSDK.LogiPlayConstantForce(0, 0);
                        LogitechGSDK.LogiStopConstantForce(0);
                        LogitechGSDK.LogiPlayDamperForce(0, 10);
                        LogitechGSDK.LogiStopDamperForce(0);
                    }
                }
                catch (System.Exception e)
                {
                    AppLog.LogWarning("[RacingState] ゴール時のFFB停止に失敗しましたが、ゴール処理は継続します: " + e.Message);
                }

                if (m_coroutine == null)
                {
                    m_coroutine = StartCoroutine(WaitFadeOut(k_GoalFadeDelaySeconds));
                }
            }

            m_chairController.InGameMove = false; // 椅子停止
        }
    }

    private IEnumerator WaitFadeOut(float _waittime)
    {
        yield return new WaitForSeconds(_waittime);
        SoundManager.Instance.PlayAndSwitchBGM(SoundManager.BGM_Type.Result, true); // ResultState.Enter()側もloop:trueで参照するため揃える(異なるとPlayBGMの早期returnでloop更新がスキップされた時に無限ループしなくなる)
        GameFlowRunner.Instance.AdvanceToNext();
    }
}
