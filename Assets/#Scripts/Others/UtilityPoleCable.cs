using UnityEngine;

// 電柱間のケーブルをLineRendererで表示するクラス
[ExecuteAlways]
public class UtilityPoleCable : MonoBehaviour
{
    // ケーブルの接続先を保持する変数
    [SerializeField]
    Transform receivePoint;
    // ケーブル描画に使用するLineRenderer
    [SerializeField]
    LineRenderer lineRenderer;
    // ケーブル中央のたるみ量
    [SerializeField, Min(0f)]
    float slack = 2f;
    // ケーブルを構成する点の数
    [SerializeField, Range(2, 32)]
    int segmentCount = 12;
    // 接続点の変更をケーブル表示へ反映する関数
    void LateUpdate()
    {
        if (receivePoint == null || lineRenderer == null)
        {
            return;
        }

        lineRenderer.positionCount = segmentCount;
        lineRenderer.useWorldSpace = true;
        Vector3 startPoint = transform.position;
        Vector3 endPoint = receivePoint.position;
        // ケーブル両端を補間し、中央へ向かって自然なたるみを加える処理
        for (int i = 0; i < segmentCount; i++)
        {
            float ratio = i / (float)(segmentCount - 1);
            Vector3 cablePoint = Vector3.Lerp(startPoint, endPoint, ratio);
            cablePoint.y -= Mathf.Sin(ratio * Mathf.PI) * slack;
            lineRenderer.SetPosition(i, cablePoint);
        }
    }
}
