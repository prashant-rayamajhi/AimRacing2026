// セクターポイントの処理(単純な当たり判定で通過を判定)
using UnityEngine;

// セクターポイントの番号
public enum SectorNumber
{
    Sector1,
    Sector2,
    Sector3,
    None,
}

public class SectorPoint : MonoBehaviour
{
    // SectorTimeManager
    [SerializeField]
    private SectorTimeManager m_sectorTimeManager;
    // セクターポイントの番号
    [SerializeField]
    private SectorNumber m_sectorNumber;
    // 通過済みか
    private bool m_isPassing;
#region プロパティ
    public bool Passing { set => m_isPassing = value; }

#endregion
    void Start()
    {
        m_isPassing = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        // プレイヤーか、まだ通過していないかを判定
        if (other.gameObject.tag == "Player" && !m_isPassing)
        {
            // 車が通過したら時間を登録する
            m_sectorTimeManager.RegisterSectorTime(m_sectorNumber);
            m_isPassing = true;
        }
    }
}
