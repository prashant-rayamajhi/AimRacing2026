using TMPro;
using UnityEngine;
using UnityEngine.Events;

// 各セクターの通過時間と表示を管理するクラス
public class Line_Sector : MonoBehaviour
{
    // セクター通過時の時間保存に使う変数
    [SerializeField]
    private TimeKeeper _timeKeeper;
    // 通過したセクタータイムを画面へ表示するための変数
    [SerializeField]
    private TextMeshProUGUI _tmp;
    // 既存シーンのセクター番号設定を保持するための変数
    [SerializeField]
    private int _sectorCount;
    // 同じセクターを一走行中に重複判定しないための変数
    [SerializeField]
    private bool _isChecked;
#pragma warning restore CS0414
    // セクター表示アニメーションを開始するための変数
    [SerializeField]
    private UnityEvent _unityEvent = new UnityEvent();
    // 保存と表示の対象となるセクターを指定する変数
    [SerializeField]
    private SectorNumber sectorNumber;
    // このセクターを通過済みか返す変数
    public bool IsChecked => _isChecked;

    // セクター通過状態を次の走行用に戻す関数
    public void ResetCheck()
    {
        _isChecked = false;
    }

    // ゲーム開始時にセクターを未通過へ戻す関数
    private void Start()
    {
        _isChecked = false;
    }

    // プレイヤー車両がセクターへ入った時だけ時間を登録する関数
    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<VehicleController>() == null)
        {
            return;
        }

        RegisterTime();
    }

    // セクター別の保存時間を取得するための変数
    [SerializeField]
    private SectorTimeManager m_sectorTimeManager;
    // セクタータイムを保存して表示と演出へ反映する関数
    private void RegisterTime()
    {
        if (_isChecked)
        {
            return;
        }

        if (_timeKeeper == null || m_sectorTimeManager == null)
        {
            return;
        }

        _timeKeeper.SaveTime();
        // 保存されたセクタータイムを分秒ミリ秒の表示へ変換する処理
        TimerInfo sectorTime = m_sectorTimeManager.GetSectorTime(sectorNumber);
        string minutes = sectorTime.minutes.ToString("00");
        string seconds = sectorTime.seconds.ToString("00");
        string milliseconds = sectorTime.milliSecond.ToString("000");
        if (_tmp != null)
        {
            _tmp.text = minutes + ":" + seconds + "." + milliseconds;
        }

        AnimationStart();
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySE(SoundManager.SE_Type.LapSignal);
        }

        _isChecked = true;
    }

    // セクター通過時の表示アニメーションを開始する関数
    public void AnimationStart()
    {
        _unityEvent.Invoke();
    }
}
