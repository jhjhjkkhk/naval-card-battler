using System.Collections;
using UnityEngine;

// ============================================================================
// 沉船处理
//   · 血量归零 → 停止移动、停止开火、倾斜下沉、沉到水下后销毁
//   · 演算导演会跳过已沉的船
// ============================================================================
public class ShipSink : MonoBehaviour
{
    public bool Sunk { get; private set; }

    public static void Sink(ShipStats ship, string reason = "")
    {
        if (ship == null) return;

        ShipSink sink = ship.GetComponent<ShipSink>();
        if (sink == null) sink = ship.gameObject.AddComponent<ShipSink>();
        if (sink.Sunk) return;

        sink.Sunk = true;
        ship.currentHp = 0;

        Debug.Log("[沉没] " + ship.shipName + " 被击沉" + (string.IsNullOrEmpty(reason) ? "" : "（" + reason + "）"));

        // 停止移动
        ShipMover mv = ship.GetComponent<ShipMover>();
        if (mv != null) mv.enabled = false;

        // 不能被选中 / 不能再被打
        Collider[] cols = ship.GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;

        ShipSelector.DeselectShip();

        DamagePopup.Show(ship.transform.position, ship.maxHp, true);

        // 特效：深色球
        ImpactFx.Play(ship.transform.position + Vector3.up * 3f, FxKind.Penetration);

        sink.StartCoroutine(sink.SinkRoutine());
    }

    IEnumerator SinkRoutine()
    {
        float t = 0f;
        CombatSettings cfg = CombatSettings.Instance;
        float duration = cfg.sinkDuration;
        Vector3 start = transform.position;
        Quaternion startRot = transform.rotation;

        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);

            transform.position = start + Vector3.down * (k * cfg.sinkDepth);
            transform.rotation = startRot * Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, cfg.sinkTiltDegrees, k));

            yield return null;
        }

        gameObject.SetActive(false);
    }
}