// ============================================================================
//  AnomalySurveyorZh —— Anomaly Surveyor 的 DLL 文本汉化补丁
//
//  背景：AnomalySurveyor.dll 实现了一个自定义合同参数类型 `Monolith`
//        （Jool 石像合同 Jool_Monolith.cfg 里 `type = Monolith` 用它），
//        它在运行时动态创建参数，标题是硬编码在 IL 里的英文字面量，
//        无法通过 Localization/*.cfg 本地化。
//
//  做法：用 Harmony 的 transpiler 扫描该程序集所有方法/构造函数的 IL，
//        把匹配的 `ldstr` 操作数替换为中文。
//        这样做的好处：
//          1) 不必猜“哪条串用在哪里”，所有引用点都会被覆盖；
//          2) 写入与后续比较用的是同一份新串，不会出现“显示改了、匹配不上”；
//        代价：只映射【显示用】文本，逻辑标识符一律不入表（见下方 Map 注释）。
//
//  依赖：Harmony 2（KSP 1.12 随 000_Harmony 提供）
//  加载位置：GameData/ContractPacks/AnomalySurveyor/Plugins/AnomalySurveyorZh.dll
//  卸载方式：删掉本 DLL 即恢复原样
// ============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace AnomalySurveyorZh
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class ZhPatcher : MonoBehaviour
    {
        private static bool _done;

        // 目标程序集的文件名（不含扩展名）
        private const string TargetAssemblyName = "AnomalySurveyor";

        // 仅【玩家可见的显示文本】。以下内容**刻意不映射**，因为它们是逻辑标识符，
        // 改了会导致功能失效或存档不匹配：
        //   Kerbin / Mun / Duna / Jool ...  天体名，用于匹配与运算
        //   Scientist / Pilot               乘员专长，用于筛选
        //   Discovery One                   飞船名，被 cfg 的 define 与 Rendezvous 参数引用
        //   starJebName / candidate / monolithDiscovered ...  DATA 字段名
        //   ContractPacks/AnomalySurveyor/...  资源路径
        //   以及所有只写日志的串（Adding EVA parameter... 等，玩家看不到）
        private static readonly Dictionary<string, string> Map = BuildMap();

        private static Dictionary<string, string> BuildMap()
        {
            Dictionary<string, string> m = new Dictionary<string, string>();
            m["Investigate the monolith"] = "调查石像";
            m["Send a Kerbal on EVA"] = "派一名坎巴拉人执行舱外活动";
            m["Approach the monolith with "] = "接近石像：";
            m["...it's full of stars!"] = "……满是星辰！";
            // 彩蛋人物的名字：与 cfg 对话译文「星之 Jeb」保持一致。
            // IL 定位显示这条字面量只出现在 MonolithParameter.CheckParameters 里，
            // 赋值与判断用的是同一条字面量，整体替换后仍然自洽。
            // 唯一注意：**安装本补丁之前**的存档里若已存在英文名 "The Star Jeb"，
            // 该判断会失配（仅影响彩蛋状态判定，不会崩溃或损坏存档）。
            // 若你不希望改这个名字，删掉这一行即可，其余映射不受影响。
            m["The Star Jeb"] = "星之 Jeb";
            return m;
        }

        /// <summary>把匹配的 ldstr 字面量替换为中文</summary>
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction ins in instructions)
            {
                if (ins.opcode == OpCodes.Ldstr && ins.operand is string)
                {
                    string zh;
                    if (Map.TryGetValue((string)ins.operand, out zh))
                    {
                        ins.operand = zh;
                    }
                }
                yield return ins;
            }
        }

        /// <summary>
        /// 判断某方法的 IL 里是否引用了 Map 中的字符串。
        /// 做法：扫描 IL 中所有 0x72(ldstr) 后面 4 字节的元数据令牌并解析。
        /// 可能有极少数误判（把不含目标串的方法也纳入），但转译器只真正改写
        /// ldstr 操作数，所以误判是安全的；这保证不会漏掉真正的引用点。
        /// </summary>
        private static bool ContainsMappedLiteral(MethodBase mi)
        {
            try
            {
                MethodBody body = mi.GetMethodBody();
                if (body == null)
                {
                    return false;
                }
                byte[] il = body.GetILAsByteArray();
                if (il == null)
                {
                    return false;
                }
                for (int i = 0; i + 4 < il.Length; i++)
                {
                    if (il[i] != 0x72)
                    {
                        continue;
                    }
                    int tok = BitConverter.ToInt32(il, i + 1);
                    if ((tok & unchecked((int)0xFF000000)) != 0x70000000)
                    {
                        continue;
                    }
                    string s;
                    try
                    {
                        s = mi.Module.ResolveString(tok);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (s != null && Map.ContainsKey(s))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        public void Awake()
        {
            if (_done)
            {
                return;
            }
            _done = true;

            try
            {
                Assembly target = FindTarget();
                if (target == null)
                {
                    Debug.Log("[AnomalySurveyorZh] 未找到 " + TargetAssemblyName
                              + " 程序集，跳过（不影响游戏）。");
                    return;
                }

                Harmony harmony = new Harmony("AnomalySurveyorZh");
                MethodInfo transpiler = typeof(ZhPatcher).GetMethod(
                    "Transpiler", BindingFlags.Static | BindingFlags.NonPublic);
                if (transpiler == null)
                {
                    Debug.LogError("[AnomalySurveyorZh] 找不到 Transpiler 方法。");
                    return;
                }

                int patched = 0;
                int scanned = 0;
                List<string> hitNames = new List<string>();
                Type[] types = SafeGetTypes(target);
                for (int i = 0; i < types.Length; i++)
                {
                    if (types[i] == null)
                    {
                        continue;
                    }
                    List<MethodBase> members = new List<MethodBase>();
                    try
                    {
                        members.AddRange(types[i].GetMethods(
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.DeclaredOnly));
                    }
                    catch (Exception)
                    {
                    }
                    try
                    {
                        members.AddRange(types[i].GetConstructors(
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static));
                    }
                    catch (Exception)
                    {
                    }

                    for (int j = 0; j < members.Count; j++)
                    {
                        scanned++;
                        // 只给真正含目标字符串的方法挂转译器，足迹最小
                        if (!ContainsMappedLiteral(members[j]))
                        {
                            continue;
                        }
                        try
                        {
                            harmony.Patch(members[j], null, null,
                                          new HarmonyMethod(transpiler));
                            patched++;
                            hitNames.Add(types[i].Name + "." + members[j].Name);
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning("[AnomalySurveyorZh] 补丁失败 "
                                             + types[i].Name + "." + members[j].Name
                                             + " : " + e.Message);
                        }
                    }
                }

                if (patched == 0)
                {
                    Debug.LogWarning("[AnomalySurveyorZh] 未在 " + TargetAssemblyName
                                     + " 中找到任何目标字符串（可能是 mod 已更新）。"
                                     + "汉化未生效，但游戏可正常运行。");
                }
                else
                {
                    Debug.Log("[AnomalySurveyorZh] 已汉化 " + patched + " 个方法"
                              + "（共扫描 " + scanned + "）："
                              + string.Join(", ", hitNames.ToArray()));
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[AnomalySurveyorZh] 初始化失败（不影响游戏本体）：" + e);
            }
        }

        private static Assembly FindTarget()
        {
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < all.Length; i++)
            {
                try
                {
                    if (all[i].GetName().Name == TargetAssemblyName)
                    {
                        return all[i];
                    }
                }
                catch (Exception)
                {
                }
            }
            return null;
        }

        private static Type[] SafeGetTypes(Assembly a)
        {
            try
            {
                return a.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                List<Type> list = new List<Type>();
                for (int i = 0; i < e.Types.Length; i++)
                {
                    if (e.Types[i] != null)
                    {
                        list.Add(e.Types[i]);
                    }
                }
                return list.ToArray();
            }
            catch (Exception)
            {
                return new Type[0];
            }
        }
    }
}
