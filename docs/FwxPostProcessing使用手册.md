# FwxPostProcessing 后处理平台 — 使用手册

## 1. 概述

FwxPostProcessing 是一个将 **NX CAM 的 CLS 文件**转换为 **Siemens NC 加工程序**的后处理平台。
采用**模板驱动架构**，支持 3 轴 / 3+2 / 5 轴联动加工，AC 双摆头机床。

### 1.1 支持的机床运动学

- **AC 双摆头**：A 绕 X 轴旋转，C 绕 Z 轴旋转
- **3 轴**：刀轴固定为 Z 方向（A=0, C=0）
- **3+2 定位**：刀轴固定但非 Z 方向，输出一次 A/C 锁轴后 XYZ 加工
- **5 轴联动**：刀轴连续变化，每行输出 A/C

### 1.2 输入输出

| 项目 | 格式 | 说明 |
|------|------|------|
| 输入 | `.cls` | NX CAM 导出的 CLS（Cutter Location Source）文件 |
| 输出 | `.mpf` / `.nc` | Siemens 840D NC 程序文件 |
| 模板 | `.tpl` | 文本模板，定义 NC 输出格式 |

---

## 2. 快速入门

### 2.1 运行环境

- Windows 操作系统
- .NET 8 Runtime
- Visual Studio 2022（用于编译和开发）

### 2.2 编译

```bash
cd FwxPostProcessing
dotnet build PostProcessor.Wpf -c Debug
```

### 2.3 启动

编译后运行 `PostProcessor.Wpf/bin/Debug/net8.0-windows/PostProcessor.Wpf.exe`

或者从 Visual Studio 按 F5 启动。

---

## 3. 界面操作指南

### 3.1 主界面布局

```
┌──────────────────────────────────────────────────┐
│  Input File:  [______________] [Browse...]        │
│  Output File: [______________] [Browse...]        │
│  Template:    [______________] [Browse...]        │
│                [层间AC重置] [Preview] [Save]      │
├──────────────────────────────────────────────────┤
│  NC Preview (只读文本框，等宽字体)                │
│                                                   │
│                                                   │
└──────────────────────────────────────────────────┘
```

### 3.2 基本操作流程

**步骤 1：选择输入 CLS 文件**

点击 Input File 旁的 `Browse...`，选择 `.cls` 文件。支持：
- **单个文件**：直接选择
- **多个文件**：按 Ctrl 多选，程序自动用 `|` 分隔合并
- **文件夹**：在输入路径直接填入文件夹路径，程序会合并文件夹内所有 `.cls`
- **手动拼接**：用 `|` 或 `;` 分隔多个路径

**步骤 2：选择模板文件**

点击 Template 旁的 `Browse...`，选择 `.tpl` 模板文件。
默认模板：`PostProcessor.Core/Templating/Templates/Siemens_AC_TRAORI.tpl`

**步骤 3：选择输出路径（可选）**

点击 Output File 旁的 `Browse...`，指定保存路径。不填则只预览不保存。

**步骤 4：层间 AC 重置（可选）**

勾选 **层间AC重置** 后点击 Preview 才生效。详见第 6 节。

**步骤 5：预览 NC 代码**

点击 `Preview`，NC 代码会显示在下方的预览框中。

**步骤 6：保存 NC 代码**

点击 `Save`，将当前预览保存到输出文件。

---

## 4. CLS 输入文件格式

### 4.1 支持的 CLS 关键字

| CLS 语法 | 说明 |
|----------|------|
| `TOOL/1` | 换刀指令（数字刀号） |
| `TOOL PATH/name,TOOL,toolname` | 刀轨段开始（含刀具名） |
| `END-OF-PATH` | 刀轨段结束 |
| `GOTO/x,y,z` | 直线运动（纯坐标） |
| `GOTO/x,y,z,i,j,k` | 直线运动（坐标 + 刀轴向量） |
| `RAPID` | 快速移动（仅影响下一条 GOTO） |
| `FEDRAT/rate,MMPF` | 进给率 |
| `SPINDL/rpm,CLW` | 主轴转速与方向 |
| `SPEED/rpm,CLW` | 同上 |
| `CIRCLE/cx,cy,cz,nx,ny,nz` | 圆弧定义 |
| `CYCLE/family,params...` | 孔循环开始 |
| `CYCLE/OFF` | 孔循环结束 |
| `PAINT/COLOR,186` | UG 导出格式 — 进刀 |
| `PAINT/COLOR,31` | UG 导出格式 — 切削 |
| `PAINT/COLOR,37` | UG 导出格式 — 退刀 |
| `jindao` / `进刀` | 自定义格式 — 进刀 |
| `qiexue` / `切削` | 自定义格式 — 切削 |
| `tuidao` / `退刀` | 自定义格式 — 退刀 |
| `END` | 程序结束 |

### 4.2 工艺阶段标记

CLS 中可以用 **PAINT/COLOR** 或 **拼音/中文关键词** 标记进刀/切削/退刀阶段。
层间 AC 重置功能依赖这些标记来识别工序层。

**对应关系：**

| CLS 标记 | 阶段 | 说明 |
|----------|------|------|
| `PAINT/COLOR,186` | 进刀 | UG 后处理 CLS |
| `PAINT/COLOR,31` | 切削 | UG 后处理 CLS |
| `PAINT/COLOR,37` | 退刀 | UG 后处理 CLS |
| `jindao` / `进刀` | 进刀 | 自定义 CLS |
| `qiexue` / `切削` | 切削 | 自定义 CLS |
| `tuidao` / `退刀` | 退刀 | 自定义 CLS |

---

## 5. 模板系统

### 5.1 模板语法

模板文件（`.tpl`）使用 `[SECTION]` 按段组织，渲染引擎逐行处理。

**变量替换：**
```
{{变量名}}               → 替换为变量值
{{变量名:F2}}            → 格式化（F2=2位小数）
{{=表达式}}              → 数学计算，如 {{=X+10:F1}}
```

**条件判断：**
```
{{IF 变量名 == "值"}}     → 条件开始
{{ELSE}}                 → 否则
{{ENDIF}}                → 结束
```

**支持的运算符：**
- 比较：`==`, `!=`, `>`, `<`, `>=`, `<=`
- 数学：`+`, `-`, `*`, `/`，支持括号

### 5.2 模板事件列表

| 模板段 | 触发条件 | 常用变量 |
|--------|----------|----------|
| `EVENT_START_PROGRAM` | 程序开始 | ProgramName, AxisMode, IsThreeAxis/IsFiveAxis |
| `EVENT_END_PROGRAM` | 程序结束 | ProgramName |
| `EVENT_START_PATH` | 刀轨段开始 | PathName, ToolName |
| `EVENT_END_PATH` | 刀轨段结束 | PathName |
| `EVENT_TOOL_CHANGE` | 换刀 | ToolCall, ToolNumber, ToolName |
| `EVENT_SPINDLE` | 主轴转速 | SpindleRpm, SpindleMCode |
| `RAPID` / `EVENT_RAPID` | 快速移动 | XField, YField, ZField, AField, CField |
| `LINEAR` / `EVENT_LINEAR` | 直线插补 | XField, YField, ZField, AField, CField, FField |
| `ARC_CW` / `EVENT_ARC_CW` | 顺时针圆弧 | XField, YField, ZField, ArcI, ArcJ, AField, CField, FField |
| `ARC_CCW` / `EVENT_ARC_CCW` | 逆时针圆弧 | 同上 |
| `EVENT_PHASE_JINDAO` | 进刀阶段 | PhaseType, PhaseText |
| `EVENT_PHASE_QIEXUE` | 切削阶段 | PhaseType, PhaseText |
| `EVENT_PHASE_TUIDAO` | 退刀阶段 | PhaseType, PhaseText |
| `EVENT_ROTARY_SETUP` | 3+2 锁轴 | A, C |
| `CYCLE_*` | 孔循环（多段） | CycleFamily, Cycle_RAPTO, Cycle_FEDTO |
| `HEADER` | 文件头部 | （固定内容） |
| `FOOTER` | 文件尾部 | （固定内容） |

### 5.3 事件优先级

模板段支持两种命名方式：
- `EVENT_XXX`：专用事件段（优先级高）
- `XXX`：通用段（备选）

例如：`EVENT_LINEAR` 存在则优先，否则回退到 `LINEAR`。

### 5.4 孔循环模板段命名

```
CYCLE_<FAMILY>_<VARIANT>_<SUFFIX>
CYCLE_<FAMILY>_<SUFFIX>
CYCLE_<SUFFIX>
```

SUFFIX 为 `START` / `FIRST_HOLE` / `HOLE` / `END`。

示例优先顺序：
- `CYCLE_DRILL_DEEP_FIRST_HOLE` → 最具体优先
- `CYCLE_DRILL_FIRST_HOLE`
- `CYCLE_FIRST_HOLE`

### 5.5 模板变量说明

| 变量 | 类型 | 说明 |
|------|------|------|
| `ProgramName` | string | 程序名称 |
| `PathName` | string | 当前刀轨段名称 |
| `ToolName` | string | 刀具名称 |
| `ToolNumber` | string | 刀号 |
| `ToolCall` | string | 复合换刀指令（`T="R3"` 或 `T1`） |
| `SpindleRpm` | string | 主轴转速 |
| `SpindleMCode` | string | 主轴方向（`M3` / `M4`） |
| `X`, `Y`, `Z` | string | 坐标值（X12.3456） |
| `XField`, `YField`, `ZField` | string | 去重坐标（相同值返回空字符串） |
| `A`, `C` | string | 角度值 |
| `AField`, `CField` | string | 去重角度（带前缀） |
| `ArcI`, `ArcJ` | string | 圆弧 I/J 值 |
| `FeedRate` | string | 进给率 |
| `FField` | string | 去重进给（带 F 前缀） |
| `PhaseType` | string | 阶段类型枚举名 |
| `PhaseText` | string | 阶段中文文本（进刀/切削/退刀） |
| `AxisMode` | string | 轴模式（ThreeAxis/ThreePlusTwo/FiveAxis） |
| `IsThreeAxis` | bool (1/0) | 是否为三轴模式 |
| `IsThreePlusTwo` | bool (1/0) | 是否为 3+2 模式 |
| `IsFiveAxis` | bool (1/0) | 是否为五轴联动模式 |
| `AxisLocked` | bool (1/0) | 3+2 模式下是否已锁轴 |
| `CycleFamily` | string | 循环类别（DRILL/BORE/TAP） |
| `CycleVariant` | string | 循环子类型（DEEP/BRKCHP/BACK） |
| `Cycle_RAPTO` | string | 快速趋近高度 |
| `Cycle_FEDTO` | string | 进给深度 |
| `Cycle_MMPM` | string | 进给速度（mm/min） |
| `CycleZField` | string | 循环安全高度（带 Z 前缀） |
| `IsFirstHole` | bool (1/0) | 当前是否为第一孔 |

**关于去重字段：**

去重字段（`XField`, `YField`, `ZField`, `AField`, `CField`, `FField`）
与普通字段的区别：如果当前值与上一行相同，去重字段返回空字符串。

| CLS 数据 | `X` 输出 | `XField` 输出 |
|----------|---------|--------------|
| X100.0 | `100.0000` | `X100.0000` |
| X100.0（重复） | `100.0000` | ``（空） |
| X200.0 | `200.0000` | `X200.0000` |

这样 `G1 {{XField}} {{YField}} {{ZField}}` 可以自动跳过重复轴。

---

## 6. AC 选解逻辑

### 6.1 运动学模型（AC 双摆头）

AC 双摆头运动学：**A 绕 X 轴旋转，C 绕 Z 轴旋转**。

输入刀轴向量 `(I, J, K)`：

```
s = √(I² + J²)
A = -atan2(s, K)      → A ∈ [-90°, 90°]
C = atan2(-I, J)       → C ∈ [-360°, 360°]
```

**等效解：** `(A, C)` 与 `(-A, C+180°)` 代表同一刀轴方向。

### 6.2 常规选解流程（不勾选层间重置）

**第一个 IJK 点**（`SelectInitialAcBranch`）：

在 `(A, C)` 与 `(-A, C+180°)` 中选择离 `(A=0, C=0)` 最近的解。
A 的权重是 C 的两倍，优先保证 A 接近 0°。

**后续 IJK 点**（`MakeContinuousAc`）：

```
1. 对两个等效解分别做 C 的周期连续化
   → 使 C 值落在 (lastC - 180°, lastC + 180°] 区间
2. 在 [C-360, C, C+360] 三候选中选离 lastC 最近的
   → 候选范围放宽到 [-540°, 540°]，避免边界误判
3. 比较两个等效解的 C 与 lastC 的距离
   → 选距离更小的解（A 随之确定）
4. 将 C 归一化到 [-360°, 360°]
```

**关键修复**：候选 C 边界 `[-540°, 540°]` 确保当 `lastC ≈ 360°` 时，
最优候选 360.5° 不会被误丢弃（旧边界 `[-360°, 360°]` 会误判）。

### 6.3 层间重置选解流程（勾选层间AC重置）

**适用场景：** 五轴 CLS 包含多个工序层（jindao → qiexue → tuidao 循环），
同一方向组的 IJK 在各层间几乎一致，但 qiexue 段的 C 漂移会通过
"上一行"传导到下一层 jindao，引起 A/C 符号翻转。

**核心思路：** 相邻层的 jindao 如果 IJK 相近（同方向组），
第二层 jindao 不跟上一行 GOTO 连续，而是锚定到上一层 jindao 的参考 AC。

**层识别：**
- `jindao` / `PAINT/COLOR,186` → JinDao
- `qiexue` / `PAINT/COLOR,31` → QieXue
- `tuidao` / `PAINT/COLOR,37` → TuiDao

**选解三分支：**

```
每层第一个 Jindao 的 IJK 运动（建层参考）：
  → 与上一层参考 AC 比对
  → 偏差 < 1° → 同方向组 → SelectClosestToRef(上层参考)
  → 偏差 ≥ 1° → 新方向组 → SelectInitialAcBranch（独立选解）

每层第一个 QieXue 的 IJK 运动（qiexue 初始化）：
  → 直接 SelectClosestToRef(本层 jindao 参考)
  → 不从上一行 GOTO 连续

其余 IJK 运动（常规连续）：
  → MakeContinuousAc（正常跟上一行）
```

**SelectClosestToRef 符号约束：**

```
A 符号与参考不一致 → +2000 惩罚
C 符号与参考不一致 → +1000 惩罚
C 周期覆盖所有等效解（无边界限制）
选总代价最小的候选
```

**与常规逻辑对比：**

| 项目 | 常规 | 层间重置 |
|------|------|----------|
| jindao 首点 | MakeContinuousAc（跟上行） | 与上层 jindao 比对，同组锚定，异组独立 |
| qiexue 首点 | MakeContinuousAc（跟上行） | 锚定本层 jindao 参考 |
| C 周期 | [-540, 540] 三个候选 | 无边界限制，所有周期等效解 |
| A 符号约束 | 无 | 优先保证与参考一致 |
| C 符号约束 | 无 | 次优先保证与参考一致 |

---

## 7. 轴模式判断

系统通过分析 CLS 中所有 IJK 向量自动判断轴模式：

```
刀轴无 IJK 数据 ⇒ 三轴
刀轴固定且 A/C ≠ 0 ⇒ 3+2 定位
刀轴变化 ⇒ 五轴联动
```

如果 CLS 包含 `TOOL PATH/...END-OF-PATH` 分段，
会对每个刀轨段单独判断轴模式，避免不同工序的 3+2 角度不同被误判为五轴。

---

## 8. 孔循环

### 8.1 支持的孔循环类型

| NX CLS 语法 | 循环类别 | 循环子类型 |
|-------------|----------|------------|
| `CYCLE/DRILL,params...` | DRILL | — |
| `CYCLE/DRILL,DEEP,params...` | DRILL | DEEP |
| `CYCLE/DRILL,BRKCHP,params...` | DRILL | BRKCHP |
| `CYCLE/BORE,params...` | BORE | — |
| `CYCLE/BORE,BACK,params...` | BORE | BACK |
| `CYCLE/TAP,params...` | TAP | — |

### 8.2 输出逻辑

1. **CYCLE_START**：输出循环定义
2. **CYCLE_FIRST_HOLE**：定位到第一孔位置，MCALL 定义循环
3. **CYCLE_HOLE**：后续孔位（仅输出变化的 XY）
4. **CYCLE_DRILL_END**：MCALL 取消循环

默认生成的 Siemens 循环为 CYCLE81（钻孔）。
可在模板中替换为 CYCLE82/83/84 等。

---

## 9. 3+2 模式

当 `EnableThreePlusTwoRotation = false`（默认）时，3+2 刀轨按五轴方式输出（每行带 A/C）。

> 当前 UI 中 `EnableThreePlusTwoRotation` 为常量 `false`，
> 如需开启 3+2 坐标旋转 + CYCLE800，需修改
> `MainWindow.xaml.cs` 中的 `EnableThreePlusTwoRotation` 为 `true`。

---

## 10. 常见问题排查

### 10.1 C 轴角度超出 [-360, 360]

**现象：** NC 代码中出现 `C360.5` 或 `C-361.2` 等。

**原因：** 边界归位异常。`MakeContinuousAc` 末尾已做归一化处理：
`if (cDeg > 360.0) cDeg -= 360.0;`

如果仍然出现，检查是否为层间重置功能产生的中间值被直接输出。

### 10.2 A 轴正负号翻转

**现象：** 同一刀轴方向的两层，AC 符号相反。

**原因1（常规模式）：** 上一层 qiexue 末尾 C 漂移到接近 180°，
`MakeContinuousAc` 选到了 `(-A, C+180°)` 解。

→ **方案：** 勾选"层间 AC 重置"。

**原因2（层间重置模式）：** 当前层 IJK 与上层参考偏差 > 1°，
被判定为"新方向组"，触发了独立选解。

→ **方案：** 检查 `sameGroupTolerance = 1.0` 是否过小，
或确认 IJK 是否确实有微小差异。

### 10.3 模板不输出

**现象：** Preview 后输出结果为空或缺少某段。

**检查：**
1. 模板中是否存在对应的 `[SECTION]`
2. 模板段名是否与事件名匹配（优先 `EVENT_XXX`，回退到 `XXX`）
3. 模板中的变量名是否与上下文一致（注意大小写不敏感）

### 10.4 PAINT/COLOR 不被识别

**现象：** CLS 中有 `PAINT/COLOR,186` 但层间重置不工作。

**检查：**
1. 确保"层间AC重置"已勾选
2. PAINT/COLOR 行是否被其他解析规则提前消费了？
3. 看 `TryParseProcessPhase` 中 `line.StartsWith("PAINT/COLOR,")`
   检测是否被大小写问题绕过

---

## 11. 项目结构

```
FwxPostProcessing/
├── PostProcessor.Core/           # 核心库
│   ├── IR/                      # 中间表示
│   │   ├── Blocks.cs            # 12 种 IR 块
│   │   └── ToolpathProgram.cs   # 程序容器
│   ├── Parsing/
│   │   └── ClsParser.cs         # CLS 解析器（状态机）
│   ├── Kinematics/
│   │   └── AcHeadKinematics.cs  # AC 双摆头运动学解算
│   ├── Processing/
│   │   ├── PostProcessorEngine.cs  # 统一入口
│   │   ├── PostProcessorRequest.cs # 请求参数
│   │   └── PostProcessorResult.cs  # 输出结果
│   └── Templating/
│       ├── PostOptions.cs            # 选项开关
│       ├── OutputState.cs            # 输出状态缓存
│       ├── TemplateDefinition.cs     # 模板加载
│       ├── TemplateRenderer.cs       # 模板渲染引擎
│       ├── TemplateContextFactory.cs  # 上下文构建 + AC 选解
│       ├── TemplatePostProcessor.cs  # 模板驱动生成器
│       ├── OutputLineProcessor.cs    # 去重输出辅助
│       ├── LineNumbering.cs          # N 行号
│       ├── AxisMode.cs               # 轴模式枚举
│       └── Templates/
│           └── Siemens_AC_TRAORI.tpl # 默认模板
├── PostProcessor.Wpf/            # WPF 桌面程序
│   ├── MainWindow.xaml           # UI 界面
│   └── MainWindow.xaml.cs        # 事件处理
├── Samples/                      # 示例文件
│   ├── output1.cls               # 样例 CLS
│   └── NC.MPF                    # 样例 NC 输出
└── docs/
    └── FwxPostProcessing使用手册.md  # 本文档
```
