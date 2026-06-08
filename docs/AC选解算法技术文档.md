# AC 选解算法技术文档

## 1. 问题背景

AC 双摆头机床（A 绕 X 轴，C 绕 Z 轴）的刀轴向量 `(I, J, K)` 到旋转角度 `(A, C)` 
的转换存在**等效解**问题：`(A, C)` 与 `(-A, C+180°)` 代表同一刀轴方向。

选解的核心挑战：在每个 IJK 点，从两个等效解中选择能保持 C 轴连续、避免大幅跳变的解。

## 2. 运动学解算

### 2.1 AC 角度计算

输入单位刀轴向量 `(I, J, K)`：

```
s = √(I² + J²)
A_rad = -atan2(s, K)
C_rad = atan2(-I, J)

A_deg = A_rad × 180°/π
C_deg = C_rad × 180°/π
```

### 2.2 A 折返处理

当 `|A| > 90°` 时，进行折返：

```
若 A < -90°: A = -180° - A, C += 180°
若 A > +90°: A = +180° - A, C += 180°
```

折返后 A 始终在 `[-90°, 90°]` 范围内。

### 2.3 C 归一化

```
C = C % 360，使 C ∈ [-360°, 360°]
```

## 3. 常规选解方法

### 3.1 首个点：SelectInitialAcBranch

当无历史 C 值时，在 `(A_raw, C_raw)` 与 `(-A_raw, C_raw+180°)` 
中选择离参考姿态 `(A=0, C=0)` 最近的点。

```
候选1：A1 =  A_raw,  C1 = NormWithin(C_raw, 0)
候选2：A2 = -A_raw,  C2 = NormWithin(C_raw+180°, 0)

cost = w_A × |A - 0| + w_C × |C - 0|
其中 w_A = 2, w_C = 1  (A 权重更高)

选 cost 较小的候选
```

### 3.2 后续点：MakeContinuousAc

当存在 `lastC` 时，选择与上一行 C 最接近的等效解。

**输入：** `(A_raw, C_raw)` 和 `lastC`

**步骤 1：C 周期连续化**（MakeContinuousC）

```
将 C_raw 加减 360° 的整数倍，使结果落在 (lastC - 180°, lastC + 180°]
```

**步骤 2：候选归一**（NormalizeCWithinRange）

```
在 [C-360°, C, C+360°] 三个候选中，选离 lastC 最近且在 [-540°, 540°] 内的
```

边界 `[-540°, 540°]` 的说明：

当 `lastC` 接近 ±360° 时（如 lastC=360°），
最优候选可能是 `C_raw+360°=360.5°`（仅超 0.5°），
若用 `[-360°, 360°]` 会误丢弃，退而求其次选到错解。
放宽到 `[-540°, 540°]` 可多容纳一次 360° 环绕，而实际输出在步骤 4 归一化回行程范围。

**步骤 3：等效解选优**

```
C1 = NormWithin(MakeCont(C_raw, lastC), lastC)       // 解1 (A_raw, ...)
C2 = NormWithin(MakeCont(C_raw+180°, lastC), lastC)  // 解2 (-A_raw, ...)

若 |C2 - lastC| < |C1 - lastC| → 选解2 (A=-A_raw, C=C2)
否则                      → 选解1 (A= A_raw, C=C1)
```

**步骤 4：边界归一化**

```
若 C > 360°  → C -= 360°
若 C < -360° → C += 360°
```

### 3.3 示例

```
上一点：A=20.748, C=360.000 (lastC=360)
新 IJK：(0.00289, -0.3316, 0.9434) → raw: A=-19.36, C=-179.5
```

| 等效解 | A | raw C | MakeContinuousC | 候选 C | delta vs lastC(360) |
|--------|---|-------|-----------------|--------|---------------------|
| 1 | -19.36 | -179.5 | +360=180.5 | 180.5 | 179.5 |
| 2 | +19.36 | -179.5+180=0.5 | +360=**360.5** | **360.5** | **0.5** ✓ |

选中解2：`A=19.36, C=360.5`，归一化 → `C=0.5`

输出：`A19.364 C0.5`

## 4. 层间重置选解方法

### 4.1 问题场景

五轴 CLS 文件常按"工序层"组织：

```
jindao → qiexue → tuidao
jindao → qiexue → tuidao
jindao → qiexue → tuidao
...
```

各层 jindao 的 IJK 几乎一致（同方向组），
但 qiexue 段的刀轴会在加工轨迹上连续变化，
C 值可能漂移 200° 以上。qiexue 末尾的 C 值通过
MakeContinuousAc 的 "上一行" 传导到下一层 jindao，
导致 A 正负号翻转、C 跳变 180°。

### 4.2 核心思想

**"层间锚定"而非"行间连续"：**

- 识别工序层边界（PAINT/COLOR 或拼音关键词）
- 每层 jindao 的第一个 IJK 点与**同方向的上一层 jindao 参考**比对
- 同方向组 → 锚定到上层参考（保持 A/C 符号）
- 新方向组 → 独立选解
- 每层 qiexue 的第一个 IJK 点锚定到本层 jindao 参考

### 4.3 层识别

| 标记 | 识别阶段 |
|------|----------|
| `PAINT/COLOR,186` 或 `jindao` / `进刀` | JinDao |
| `PAINT/COLOR,31` 或 `qiexue` / `切削` | QieXue |
| `PAINT/COLOR,37` 或 `tuidao` / `退刀` | TuiDao |

### 4.4 状态变量

| 变量 | 类型 | 说明 |
|------|------|------|
| LayerIndex | int | 当前层序号（0=未进入层，每遇 jindao +1） |
| NeedsSaveLayerRef | bool | 下一 jindao IJK 需保存为层参考 |
| NeedsQieXueInit | bool | 下一 qiexue IJK 需从层参考初始化 |
| CurrentLayerRefA | double? | 上一层/当前层 jindao 的参考 A |
| CurrentLayerRefC | double? | 上一层/当前层 jindao 的参考 C |

### 4.5 状态驱动

**ProcessPhaseBlock 处理时更新状态：**

```
遇 JinDao → LayerIndex++, NeedsSaveLayerRef = true
遇 QieXue → NeedsQieXueInit = true
遇 TuiDao → 无操作
```

### 4.6 选解三分支（ResolveFiveAxisAc）

```
┌─ NeedsSaveLayerRef && isJinDaoMotion？
│   ├─ YES → 【分支1：建层参考】
│   └─ NO  → 继续 ↓
│
├─ NeedsQieXueInit？
│   ├─ YES → 【分支2：qiexue 初始化】
│   └─ NO  → 【分支3：常规连续】
```

#### 分支1：建层参考

```
与 CurrentLayerRef 比对：
  rawC_near = NormalizeCPeriodic(C_raw, CurrentLayerRefC)
  isSameGroup = |A_raw - CurrentLayerRefA| < 1°
                && |rawC_near - CurrentLayerRefC| < 1°

若 CurrentLayerRef 为空（首次）或 isSameGroup == false：
  → SelectInitialAcBranch           // 独立选解，建立新参考点

若 isSameGroup == true：
  → SelectClosestToRef(CurrentLayerRefA, CurrentLayerRefC)
                                    // 锚定到上层参考

更新：
  CurrentLayerRefA = 选出的A
  CurrentLayerRefC = 选出的C
  NeedsSaveLayerRef = false
```

#### 分支2：qiexue 初始化

```
SelectClosestToRef(CurrentLayerRefA, CurrentLayerRefC)
  // 不从上一行 GOTO 连续，锚定到本层 jindao 参考
NeedsQieXueInit = false
```

#### 分支3：常规连续

```
若 LastC.HasValue → MakeContinuousAc
否则 → SelectInitialAcBranch
```

### 4.7 SelectClosestToRef 符号约束算法

```
输入：(A_raw, C_raw) 与参考值 (refA, refC)

候选1：(A = A_raw,            C = NormPeriodic(C_raw, refC))
候选2：(A = -A_raw,           C = NormPeriodic(C_raw+180°, refC))

A 符号匹配惩罚 = 2000（A 不一致时加）
C 符号匹配惩罚 = 1000（C 不一致时加）

cost = 惩罚 + |C - refC|

选 cost 较小的候选
```

### 4.8 NormalizeCPeriodic 与 NormalizeCWithinRange 的区别

| 方法 | 范围 | 用途 |
|------|------|------|
| `NormalizeCWithinRange` | `[C-360, C, C+360]` 三候选，边界 `[-540,540]` | 常规选解 |
| `NormalizeCPeriodic` | 无边界限制，所有 `C±360n` 等价解 | 层间重置 **选 Closest To Ref** |

`NormalizeCPeriodic` 的数学实现：

```
diff = angle - target
diff -= floor(diff / 360 + 0.5) × 360
return target + diff
```

等效于 `target + Math.IEEERemainder(angle - target, 360.0)`，
但 IEEERemainder 仅返回 `(-180°, 180°]` 区间；
而 `NormalizeCPeriodic` 通过取整绕数找到任意跨度下的**最近周期等价角**，
不受 ±540° 边界限制。

## 5. 3+2 模式选解

在 3+2 模式（刀轴固定但非 Z 方向）下，AC 只在首次锁轴时输出一次：

```
if (!state.AxisLocked):
    首次：SelectInitialAcBranch（向 A0/C0 选解）
    → 输出 A/C + CYCLE800
    → 标记 AxisLocked = true
后续：不输出 A/C（已锁轴）
```

其间每行运动做 **AC 逆旋转**（RotateByAcInverse），
将刀轴系坐标转回机床坐标系：

```
绕 C(-Z) 旋转 → 绕 A(-X) 旋转
```

## 6. AC 逆旋转算法

```
输入：机床坐标 (x, y, z)，旋转角度 (a_deg, c_deg)

绕 C（Z 轴逆旋转）：
  c_rad = -c_deg × π / 180
  x₁ = x × cos(c_rad) - y × sin(c_rad)
  y₁ = x × sin(c_rad) + y × cos(c_rad)
  z₁ = z

绕 A（X 轴逆旋转）：
  a_rad = -a_deg × π / 180
  y₂ = y₁ × cos(a_rad) - z₁ × sin(a_rad)
  z₂ = y₁ × sin(a_rad) + z₁ × cos(a_rad)

输出：(x₁, y₂, z₂)
```

圆弧 I/J 向量同样按此旋转。

## 7. 三种模式的选解总对比

| 特性 | 3 轴 | 3+2 | 五轴联动 |
|------|------|-----|----------|
| 刀轴 IJK | 无或 Z 向 | 固定非 Z | 连续变化 |
| AC 解算 | 不输出 | 首次锁轴时输出一次 | 每行输出 |
| 坐标旋转 | 无 | 需做 AC 逆旋转 | 无 |
| 选解方法 | — | SelectInitialAcBranch | MakeContinuousAc / 层间重置 |
