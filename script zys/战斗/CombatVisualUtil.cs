using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ============================================================================
// 通用材质工具：HDRP 透明 Unlit，失败时退回内置着色器
// ============================================================================
public static class CombatVisualUtil
{
    public static Material CreateUnlit(Color color)
    {
        Shader sh = Shader.Find("HDRP/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        Material mat = new Material(sh) { name = "CombatVisual" };

        // 透明表面
        if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 1f);
        if (mat.HasProperty("_BlendMode")) mat.SetFloat("_BlendMode", 0f);
        if (mat.HasProperty("_AlphaCutoffEnable")) mat.SetFloat("_AlphaCutoffEnable", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_BLENDMODE_ALPHA");

        // 关键：显式设置混合状态（少了这两行，HDRP 会把它当不透明画成灰白色块）
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

        if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", 1f);
        if (mat.HasProperty("_CullMode")) mat.SetFloat("_CullMode", 0f);
        mat.EnableKeyword("_DOUBLE_SIDED");
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

        mat.renderQueue = 3000;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        return mat;
    }

    /// <summary>鼠标是否压在可交互 UI 上（按钮/卡牌/信息牌）</summary>
    public static bool PointerOverInteractiveUI()
    {
        if (EventSystem.current == null) return false;

        var ped = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var hits = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(ped, hits);

        for (int i = 0; i < hits.Count; i++)
        {
            GameObject go = hits[i].gameObject;
            if (go == null) continue;
            if (go.GetComponentInParent<Selectable>() != null) return true;

            MonoBehaviour[] mbs = go.GetComponentsInParent<MonoBehaviour>();
            for (int j = 0; j < mbs.Length; j++)
                if (mbs[j] is IPointerClickHandler) return true;
        }
        return false;
    }
}
