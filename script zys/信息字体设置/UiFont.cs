using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using TMPro;
using UnityEngine;

//public class UiFont : MonoBehaviour
//这样的可以挂到物体上
public static class UiFont
//这样的是静态类，不能挂，就是一个工具箱
{
    private static Font cached;
    //Font是unity自带的类，专门装一套字体的数据
    //cached是一个变量的名字

    /// <summary>
    /// 中文主字体：阿里巴巴普惠体，含全部常用汉字 + 中文标点（，。、""/《》……）。
    /// 放在 Resources/Fonts 下，运行时 Resources.Load 才拿得到（Resources 外的资源打包不了）。
    /// </summary>
    private const string CjkFontPath = "Fonts/AlibabaPuHuiTi-3-55-Regular";

    public static Font Get()
    //Font Get()是返回类型+方法名字
    {
        if (cached != null) return cached;

        // ① 首选打包进来的中文字体
        try { cached = Resources.Load<Font>(CjkFontPath); } catch { }

        // ② 没打包字体文件时，退到系统里装的中文字体（至少不是方块）
        if (cached == null) cached = LoadSystemCjkFont();

        // ③ 最后才是引擎内置字体：Arial 一个汉字都没有，走到这步中文会全是"豆腐块"。
        //    LegacyRuntime.ttf 同理，留着只是为了别让文字彻底消失。
        if (cached == null)
        {
            try { cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        }
        if (cached == null)
        {
            try { cached = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
        }

        if (cached == null) Debug.LogError("找不到可用字体，文字无法显示：请把中文字体放到 Resources/Fonts 下");
        //Debug.LogError是在控制台打一条红色错误 Debug.Log是白色字体
        return cached;
        //CardDrag里会调用这个方法，给cached赋值
    }

    private static TMP_FontAsset cachedSdf;

    /// <summary>
    /// TMP 用的中文字体资产。TMP 不认 Font，只认 TMP_FontAsset；
    /// 而且默认资产（LiberationSans）里一个汉字都没有，直接写中文就是方块。
    /// ① 先用"工具 → 生成中文 TMP 字体资产"生成好的 SDF（Resources/Fonts/Chinese SDF）
    /// ② 没有就用手上的 TTF 现场生成一个动态资产（缺字会自动补进图集）
    /// </summary>
    public static TMP_FontAsset GetSdf()
    {
        if (cachedSdf != null) return cachedSdf;

        // ★只加载烘焙好的资产，绝不在运行时用 TTF 现场生成：
        //   动态 TMP 资产每遇到没烘过的字就要重建整张图集，切页/开面板会卡成幻灯片。
        //   （要中文 SDF 就用菜单 工具 → 生成中文 TMP 字体资产 先生成好）
        try { cachedSdf = Resources.Load<TMP_FontAsset>("Fonts/Chinese SDF"); } catch { }

        // 兜底：Resources 里没有就用 TMP 的默认字体资产（中文资产已设为默认）
        if (cachedSdf == null)
        {
            try { cachedSdf = TMP_Settings.defaultFontAsset; } catch { }
        }
        return cachedSdf;
    }

    /// <summary>
    /// 从系统已安装的字体里挑一个能显示中文的（微软雅黑 / 黑体 / 宋体…）。
    /// 只在 Resources 里没有字体文件时才用：CreateDynamicFontFromOSFont 是照着
    /// 系统字体名现造的，换台机器可能就取不到，能打包字体文件就别靠它。
    /// </summary>
    private static Font LoadSystemCjkFont()
    {
        string[] installed;
        try { installed = Font.GetOSInstalledFontNames(); } catch { return null; }
        if (installed == null) return null;

        string[] prefer =
        {
            "Microsoft YaHei", "微软雅黑", "MSYH",
            "SimHei", "黑体", "SimSun", "宋体",
            "Noto Sans CJK", "Source Han Sans", "WenQuanYi"
        };

        foreach (string want in prefer)
        {
            foreach (string name in installed)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (name.IndexOf(want, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                try
                {
                    Font f = Font.CreateDynamicFontFromOSFont(name, 16);
                    if (f != null) return f;
                }
                catch { }
            }
        }
        return null;
    }
}
