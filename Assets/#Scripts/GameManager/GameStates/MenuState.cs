// AT/MT選択メニューMenuの新エンジン版State実装
using UnityEngine;
using System;
using System.Collections;
using AimRacing.Ranking;
using DG.Tweening;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class MenuState : GameStateBindingBase
{
    [SerializeField]
    private UnityEvent m_onSteerRightEvent;
    [SerializeField]
    private UnityEvent m_onSteerLeftEvent;
    [SerializeField, Range(0.01f, 1f)]
    private float m_steerRange; // 左右選択の入力しきい値
    [SerializeField]
    private TwoChoice m_twoChoice; // 最終確認演出(中央移動・拡大)の呼び出し先
    [SerializeField]
    private float m_confirmMinDelay = 0.3f; // 最終確認表示開始から2回目入力を受け付けるまでの最短時間(多重発火対策)
    [SerializeField]
    private float m_bgmFadeOutDuration = 0.1f; // 遷移確定時にBGMをフェードアウトする秒数
    [SerializeField]
    private GameObject m_entryTransmission; // 名前入力完了後に表示するTransmission選択UI
    [SerializeField]
    private GameObject m_nameEntryUI; // 「名前を入力してください」案内UI(シーン側で用意したCanvas)
    [SerializeField]
    private Transform m_nameEntryContent; // m_nameEntryUIの中身。Scale 0↔等倍で出入りさせる対象
    [SerializeField]
    private float m_nameEntryScaleDuration = 0.3f; // m_nameEntryContentの拡大/縮小にかかる時間
    [SerializeField]
    private Image m_nameEntryGuideImage; // EntryNameのImage。表示中はm_gasPedalGuideImageと同じ頻度で点滅させる
    [SerializeField]
    private GameObject m_entryUI; // EntryNameとEntryTransmissionの間に表示する案内(Entry)UI(シーン側で用意したCanvas)
    [SerializeField]
    private Transform m_entryContent; // m_entryUIの中身。EntryNameと同じくScale 0↔等倍で出入りさせる対象
    [SerializeField]
    private float m_entryScaleDuration = 0.3f; // m_entryContentの拡大/縮小にかかる時間
    [SerializeField]
    private float m_entryDisplayDuration = 3f; // Entry表示から自動でEntryTransmissionへ進むまでの秒数
    [SerializeField]
    private TMP_Text m_entryPlayerNameText; // Entry画面に表示する、入力されたプレイヤー名のテキスト
    [SerializeField]
    private Image m_gasPedalGuideImage; // 「Determined by gas pedal」画像。EntryTransmission表示中は1秒間隔で点滅させる
    [SerializeField]
    private TimeRemainingCountdown m_timeRemaining; // 2回目のアクセル確定時にカウントダウンを停止する対象
    private readonly AxisPressdOnce m_pressedOnce = new AxisPressdOnce(); // 1回目のアクセル(最終確認表示)検出用
    private readonly AxisPressdOnce m_confirmPressedOnce = new AxisPressdOnce(); // 2回目のアクセル(遷移確定)検出用
    private readonly AxisPressdOnce m_brakePressedOnce = new AxisPressdOnce(); // ハンコンのブレーキペダル踏み込み(選択への戻り)検出用
    private DrivingSettings m_selectedSettings;
    private bool m_isSelected;
    private bool m_isConfirming; // 1回目のアクセルを踏み、最終確認(2回目のアクセル待ち)中か
    private float m_confirmStartTime; // 最終確認表示に入った時刻(m_confirmMinDelay判定用)
    private bool m_triger;
    private bool m_changed; // 遷移処理が一度きりであることを保証するフラグ(m_trigerとは別に持つ)
    private bool m_isNameEntryDone; // 名前入力(EntryName→Entry→EntryTransmission切り替え)が完了済みか
    private bool m_isEntryTransmissionActive; // EntryTransmission(Transmission選択UI)が完全に有効化・表示済みか
    public override GameStateId Id => GameStateId.Menu;

    public override void Enter()
    {
        base.Enter();
        MenuInputDeviceSharing.Ensure(GetComponent<PlayerInput>()); // Manager常駐のPlayerInputに機器を先取りされていても入力が空にならないようにする
        StartCoroutine(SoundManager.Instance.DelayProcess(0.0f, () =>
        {
            if (!SoundManager.Instance.IsBGMPlaying()) // Loading画面側(Opening→Menu)で先行再生済みなら再度呼ばない(二重呼び出しはSwitchBGMの副作用で無関係な曲へクロスフェードしてしまう)
            {
                SoundManager.Instance.PlayAndSwitchBGM(SoundManager.BGM_Type.Entry, true);
            }
        }));
        m_selectedSettings.isAT = true;
        m_isNameEntryDone = false;
        m_isEntryTransmissionActive = false;
        if (m_entryTransmission != null)
        {
            m_entryTransmission.SetActive(false);
        } // 名前入力完了まではTransmission選択を隠す

        if (m_twoChoice != null)
        {
            m_twoChoice.gameObject.SetActive(false);
        } // Start()の入場演出が先走らないよう合わせて隠す

        if (m_entryUI != null)
        {
            m_entryUI.SetActive(false);
        } // 名前入力完了まではEntry(中間案内)も隠す

        if (m_nameEntryUI != null)
        {
            m_nameEntryUI.SetActive(true);
        } // 「名前を入力してください」を表示

        if (m_nameEntryContent != null)
        {
            m_nameEntryContent.localScale = Vector3.zero;
            m_nameEntryContent.DOScale(1f, m_nameEntryScaleDuration); // TwoChoiceのアイコンと同じ0→等倍の入場演出
        }

        if (m_nameEntryGuideImage != null)
        {
            m_nameEntryGuideImage.DOFade(0f, 1f).SetLoops(-1, LoopType.Yoyo); // m_gasPedalGuideImageと同じ1秒間隔でフェードイン/アウトを繰り返す
        }

        if (TcpServer.Instance != null)
        {
            TcpServer.Instance.StartNameEntryServer(); // Menu進入時に名前入力の受付を開始する
            TcpServer.OnNameReceived += OnNameEntryReceived;
        }
    }

    public override void Exit()
    {
        base.Exit();
        if (TcpServer.Instance != null)
        {
            TcpServer.Instance.StopNameEntryServer(); // Menu退出時に名前入力の受付を終了する
            TcpServer.OnNameReceived -= OnNameEntryReceived;
        }
    }

    /// <summary>
    /// TcpServerが名前を受信した際に呼ばれ、案内UIを縮小してEntry(中間案内)へ切り替えます。
    /// </summary>
    /// <param name = "_playerName">受信したプレイヤー名</param>
    private void OnNameEntryReceived(string _playerName)
    {
        if (m_isNameEntryDone)
        {
            return;
        } // 二重発火防止

        m_isNameEntryDone = true;
        if (m_entryPlayerNameText != null)
        {
            m_entryPlayerNameText.text = _playerName;
        } // Entry画面に表示する名前を確定する

        if (m_nameEntryGuideImage != null)
        {
            m_nameEntryGuideImage.DOKill();
        } // EntryName退出と同時に点滅を停止する

        if (m_nameEntryContent != null)
        {
            m_nameEntryContent.DOScale(0f, m_nameEntryScaleDuration).OnComplete(ShowEntry); // 等倍→0への縮小(入場演出の逆再生)
        }
        else
        {
            ShowEntry();
        }
    }

    /// <summary>
    /// EntryNameの縮小完了を受けてEntry(中間案内)を表示し、一定時間後にEntryTransmissionへ自動的に進みます。
    /// </summary>
    private void ShowEntry()
    {
        if (m_nameEntryUI != null)
        {
            m_nameEntryUI.SetActive(false);
        }

        if (m_entryUI != null)
        {
            m_entryUI.SetActive(true);
        }

        if (m_entryContent != null)
        {
            m_entryContent.localScale = Vector3.zero;
            m_entryContent.DOScale(1f, m_entryScaleDuration); // EntryNameと同じ0→等倍の入場演出
        }

        if (m_entryPlayerNameText != null)
        {
            m_entryPlayerNameText.transform.localScale = Vector3.zero;
            m_entryPlayerNameText.transform.DOScale(1f, m_entryScaleDuration); // Imageの入場演出と同じ0→等倍
        }

        StartCoroutine(SoundManager.Instance.DelayProcess(m_entryDisplayDuration, HideEntry));
    }

    /// <summary>
    /// Entry表示から一定時間経過後、Entryを縮小してEntryTransmissionへ切り替えます。
    /// </summary>
    private void HideEntry()
    {
        if (m_entryPlayerNameText != null)
        {
            m_entryPlayerNameText.transform.DOScale(0f, m_entryScaleDuration); // Imageの退場演出と同じ等倍→0
        }

        if (m_entryContent != null)
        {
            m_entryContent.DOScale(0f, m_entryScaleDuration).OnComplete(ShowEntryTransmission); // 等倍→0への縮小(入場演出の逆再生)
        }
        else
        {
            ShowEntryTransmission();
        }
    }

    /// <summary>
    /// Entryの縮小完了を受けてEntryTransmissionを表示します。
    /// </summary>
    private void ShowEntryTransmission()
    {
        if (m_entryUI != null)
        {
            m_entryUI.SetActive(false);
        }

        if (m_entryTransmission != null)
        {
            m_entryTransmission.SetActive(true);
        }

        if (m_twoChoice != null)
        {
            m_twoChoice.gameObject.SetActive(true); // ここで初めてStart()が走り、入場演出が正しいタイミングで再生される
        }

        SoundManager.Instance.PlaySE(SoundManager.SE_Type.GuideATMT); // EntryTransmission表示と同時にガイド音声を再生
        if (m_gasPedalGuideImage != null)
        {
            m_gasPedalGuideImage.DOFade(0f, 1f).SetLoops(-1, LoopType.Yoyo); // 最終決定まで1秒間隔でフェードイン/アウトを繰り返す
        }

        m_isEntryTransmissionActive = true; // EntryTransmissionの有効化・表示が完了したので、ここからTransmission選択の入力を受け付ける
    }

    public override void Tick(float _deltaTime)
    {
        if (!m_confirmPressedOnce.PressedOnce && !m_triger)
        {
            return;
        }

        if (m_changed)
        {
            return;
        }

        m_changed = true;
        OnTriger();
        if (m_twoChoice != null)
        {
            m_twoChoice.PlayExitAnimation();
        } // 入場演出(サイズ0→等倍)の逆再生

        if (m_gasPedalGuideImage != null)
        {
            m_gasPedalGuideImage.DOKill();
        } // 最終決定したので点滅を停止する

        if (m_timeRemaining != null)
        {
            m_timeRemaining.StopCountdown();
        } // 2回目のアクセル確定と同時にカウントダウンを停止する

        SoundManager.Instance.FadeOutBGM(m_bgmFadeOutDuration); // 完了後の停止処理はFadeOutBGM内部のコルーチンに任せる(直後にStopBGMを呼ぶとフェードを待たず即座に打ち切ってしまう)
        GameManager.Instance.DrivingSettings = m_selectedSettings;
        GameFlowRunner.Instance.AdvanceToNext();
    }

    public void OnTriger()
    {
        if (!m_isSelected)
        {
            return;
        } // 選ばれるまではトリガー(上矢印/右トリガー等)でも進めない

        if (!m_isConfirming) // 1回目相当:最終確認表示へ
        {
            EnterConfirmation();
            return;
        }

        if (Time.time - m_confirmStartTime < m_confirmMinDelay)
        {
            return;
        } // 確認演出直後の多重発火を無視する

        if (m_triger)
        {
            return;
        } // 2回目相当:遷移確定

        m_triger = true;
        SoundManager.Instance.PlaySE(SoundManager.SE_Type.EntryPushSE);
    }

    public void OnPedal(InputAction.CallbackContext _context)
    {
        if (!m_isEntryTransmissionActive)
        {
            return;
        } // EntryTransmissionが表示されるまでTransmission選択(アクセル確定)を反応させない

        float value = _context.ReadValue<float>();
        value = 1 - (value + 1) / 2;
        if (!m_isSelected)
        {
            return;
        } // 選ばれるまでアクセルさせない

        if (!m_isConfirming) // 1回目のアクセル判定
        {
            m_pressedOnce.AxisCheck(value);
            if (m_pressedOnce.PressedOnce)
            {
                EnterConfirmation();
            }

            return;
        }

        if (Time.time - m_confirmStartTime < m_confirmMinDelay)
        {
            return;
        } // 確認演出直後の多重発火を無視する

        m_confirmPressedOnce.AxisCheck(value); // 2回目(最終確認)のアクセル判定
    }

    /// <summary>
    /// 1回目のアクセル/トリガーで最終確認演出(中央移動・拡大)へ移行し、AT/MTのボイスを再生します。
    /// </summary>
    private void EnterConfirmation()
    {
        if (m_isConfirming)
        {
            return;
        } // 二重発火防止

        m_isConfirming = true;
        m_confirmStartTime = Time.time;
        SoundManager.Instance.PlaySE(m_selectedSettings.isAT ? SoundManager.SE_Type.AT : SoundManager.SE_Type.MT);
        if (m_twoChoice != null)
        {
            m_twoChoice.ShowFinalConfirmation();
        }
    }

    public void OnBrakeTriger()
    {
        if (!m_isConfirming)
        {
            return;
        } // 最終確認中でなければ何もしない

        if (m_changed)
        {
            return;
        } // 既に遷移確定済みなら手遅れ

        m_isConfirming = false; // 選択状態(m_isSelected)は維持し、最終確認だけ解除する
        m_pressedOnce.Reset();
        m_confirmPressedOnce.Reset();
        m_brakePressedOnce.Reset();
        m_triger = false;
        if (m_twoChoice != null)
        {
            m_twoChoice.CancelFinalConfirmation();
        }
    }

    /// <summary>
    /// ハンコンのブレーキペダル(G923)入力を受け取り、最終確認中の踏み込みで選択へ戻します。
    /// </summary>
    /// <param name = "_context">ブレーキペダル軸の入力コンテキスト</param>
    public void OnBrakePedal(InputAction.CallbackContext _context)
    {
        if (!m_isConfirming)
        {
            return;
        } // 最終確認中でなければ踏み込み検出自体を行わない(確認前の誤操作蓄積を防ぐ)

        float value = _context.ReadValue<float>();
        value = 1 - (value + 1) / 2;
        m_brakePressedOnce.AxisCheck(value);
        if (m_brakePressedOnce.PressedOnce)
        {
            OnBrakeTriger();
        }
    }

    public void OnSelect(InputAction.CallbackContext _context)
    {
        if (!m_isEntryTransmissionActive)
        {
            return;
        } // EntryTransmissionが表示されるまでTransmission選択(ハンドル左右)を反応させない

        float value = _context.ReadValue<float>();
        if (m_isConfirming)
        {
            return;
        } // 最終確認に入ったら左右選択を無効化する(OnTriger経由の1回目も含めて確実に塞ぐ)

        if (value > m_steerRange)
        {
            m_onSteerRightEvent.Invoke();
            m_isSelected = true;
            m_selectedSettings.isAT = false;
        }
        else if (value < -m_steerRange)
        {
            m_onSteerLeftEvent.Invoke();
            m_isSelected = true;
            m_selectedSettings.isAT = true;
        }
    }
}
