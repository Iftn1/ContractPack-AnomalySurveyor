# AnomalySurveyorZh —— DLL 文本汉化补丁说明

## 这是干什么的

`Anomaly Surveyor` 的合同文本、机构名、零件说明等**已经在 `Localization/*.cfg` 里汉化**
（190 个键，全部校验通过）。但有 5 条文本是**硬编码在 `AnomalySurveyor.dll` 里**的，
cfg 本地化机制完全无法覆盖：

| DLL 里的英文 | 中文 | 出现位置（IL 定位） |
|---|---|---|
| `Investigate the monolith` | 调查石像 | `MonolithParameter::GetParameterTitle` |
| `Send a Kerbal on EVA` | 派一名坎巴拉人执行舱外活动 | `MonolithParameter::CreateDelegates` |
| `Approach the monolith with ` | 接近石像： | `MonolithParameter::CreateDelegates` |
| `...it's full of stars!` | ……满是星辰！ | `MonolithParameter::CreateDelegates` |
| `The Star Jeb` | 星之 Jeb | `MonolithParameter::CheckParameters` |

这些会在 **Jool 石像合同**（`AS_Jool_Monolith`，`type = Monolith` 那个自定义参数）
的参数列表里显示。本补丁把它们变成中文。

## 工作原理

1. 启动时（主菜单）用反射找到 `AnomalySurveyor` 程序集；
2. 扫描其**所有方法/构造函数**的 IL，用元数据令牌找出**真正引用了上述字符串**的方法
   （当前版本命中 3 个方法）；
3. 只给这几个方法挂 Harmony **transpiler**，把匹配的 `ldstr` 操作数替换为中文。

为什么用 transpiler 而不是给 `ContractParameter.Title` 打属性补丁：字符串是 IL 字面量，
属性补丁只能覆盖"标题"这一种用法，且**赋值与判断可能用的是同一条字面量**——
在 IL 层整条替换能保证两者始终一致，不会出现"显示改了、内部匹配不上"。

安全边界：**只映射显示用文本**。`Kerbin`、`Mun`、`Duna`、`Jool`、`Scientist`、`Pilot`、
`Discovery One`（飞船名，被 `define` 与 Rendevous 引用）、`starJebName` 等逻辑标识符
一律不入表（单元测试里已包含反向用例）。

## 安装

```
GameData/ContractPacks/AnomalySurveyor/Plugins/AnomalySurveyorZh.dll
```

依赖 **Harmony 2**（KSP 1.12 随 `GameData/000_Harmony` 提供；若你没装，补上即可）。
不需要改动 `AnomalySurveyor.dll` 本身。

**卸载**：直接删掉这个 DLL，立刻恢复原样（cfg 汉化不受影响）。

## 怎么确认生效

进游戏后看 `KSP.log`，搜索 `AnomalySurveyorZh`：

```
[AnomalySurveyorZh] 已汉化 3 个方法（共扫描 …）：MonolithParameter.GetParameterTitle, …
```

若显示"未找到 AnomalySurveyor 程序集"或"未找到任何目标字符串"，说明 mod 更新后
程序集名/字符串变了，此时补丁**自动跳过**、不影响游戏运行。

## 已知注意点

- `The Star Jeb` 这条同时用于**命名与判断**（都在同一条字面量上，替换后仍自洽）。
  唯一影响：**安装本补丁之前**的存档里若已存在英文名 `The Star Jeb`，该判断会失配
  （只影响彩蛋状态判定，不会崩溃、不会损坏存档）。不想要这条，删掉源码里的那一行重编译即可。
- 本补丁**不修改** `AnomalySurveyor.dll` 文件，因此 mod 更新不会破坏它；
  但如果 mod 更新后字符串变了，补丁会自动失效（表现为那几句仍是英文）。

## 重新编译

```powershell
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$m   = 'G:\Kerbal Space Program\KSP_x64_Data\Managed'
$har = 'G:\Kerbal Space Program\GameData\000_Harmony\0Harmony.dll'
& $csc /nologo /target:library /nostdlib+ /noconfig /optimize+ `
  /out:AnomalySurveyorZh.dll `
  /r:"$m\mscorlib.dll" /r:"$m\System.dll" /r:"$m\System.Core.dll" `
  /r:"$m\UnityEngine.dll" /r:"$m\UnityEngine.CoreModule.dll" `
  /r:"$m\Assembly-CSharp.dll" /r:"$har" AnomalySurveyorZh.cs
```

（`/nostdlib+ /noconfig` 必须加：否则 csc 会自带引用桌面版 `mscorlib/System`，
与 KSP 版冲突报 CS1703。）

> ⚠️ 若你重新运行本项目的 `build.py` 重建 cfg 汉化，它会**清空并重建输出目录**，
> 从而删掉 `Plugins/AnomalySurveyorZh.dll`——重新编译放回即可（cfg 汉化与插件互不依赖）。
