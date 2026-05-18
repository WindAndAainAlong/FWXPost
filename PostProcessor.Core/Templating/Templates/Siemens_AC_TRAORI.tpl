[EVENT_START_PROGRAM]
; PROGRAM START
; NOTE: 
DEF REAL _camtolerance
DEF REAL _X_HOME, _Y_HOME, _Z_HOME, _A_HOME, _C_HOME
_camtolerance=0.06
_X_HOME=999999.9
_Y_HOME=999999.9
_Z_HOME=999999.9
_A_HOME=0
_C_HOME=0
G40 G17 G710 G94 G90 G60 G601 FNORM
TRAFOOF


[EVENT_END_PROGRAM]
; PROGRAM END
M30

[EVENT_TOOL_CHANGE]
{{ToolCall}}
M6
TRAORI
G54

[EVENT_SPINDLE]
S{{SpindleRpm}} M3


[EVENT_START_PATH]
; START PATH {{PathName}}

[EVENT_END_PATH]
; END PATH {{PathName}}
; 回零/结束
TRAFOOF
SUPA G0 Z=_Z_HOME D0
SUPA G0 X=_X_HOME Y=_Y_HOME A=_A_HOME C=_C_HOME D0
CYCLE832()
M5

[EVENT_ROTARY_SETUP]
A{{A}} C{{C}}
CYCLE800(0,"",0,0,0,0,0,{{A}},0,{{C}},0,0,0,0,0)

[EVENT_PHASE]
; PHASE {{PhaseText}}

[EVENT_PHASE_JINDAO]
; 进刀

[EVENT_PHASE_QIEXUE]
; 切削

[EVENT_PHASE_TUIDAO]
; 退刀

[LINEAR]
G1 {{XField}} {{YField}} {{ZField}} {{AField}} {{CField}} {{FField}}

[RAPID]
G0 {{XField}} {{YField}} {{ZField}} {{AField}} {{CField}}

[ARC_CW]
G2 {{XField}} {{YField}} {{ZField}} I{{ArcI}} J{{ArcJ}} {{AField}} {{CField}} {{FField}}

[ARC_CCW]
G3 {{XField}} {{YField}} {{ZField}} I{{ArcI}} J{{ArcJ}} {{AField}} {{CField}} {{FField}}

; --------------------
; 孔循环（NX: CYCLE/* ... GOTO ... CYCLE/OFF）
; 段名规则（英文）：CYCLE_<FAMILY>_<VARIANT>_*
; 例如：CYCLE_DRILL_DEEP_FIRST_HOLE / CYCLE_BORE_BACK_END
; 说明：以下默认实现用“方式A”：Siemens MCALL + CYCLE81。
; 你可以在模板里针对不同循环覆盖为 CYCLE82/83/84/...。
; --------------------

[CYCLE_START]
; CYCLE START (GENERIC)

[CYCLE_FIRST_HOLE]
; CYCLE FIRST HOLE (GENERIC)
G0 {{XField}} {{YField}} {{CycleZField}} {{AField}} {{CField}}
{{FField}}
MCALL CYCLE81({{Cycle_RAPTO}},0,{{Cycle_RAPTO}},{{Cycle_FEDTO}})
{{XFieldForce}} {{YFieldForce}}

[CYCLE_HOLE]
; CYCLE HOLE (GENERIC)
{{XField}} {{YField}}

[CYCLE_END]
; CYCLE END (GENERIC)
MCALL

; --- DRILL ---
[CYCLE_DRILL_START]
; DRILL START

[CYCLE_DRILL_FIRST_HOLE]
; DRILL FIRST HOLE
G0 {{XField}} {{YField}} {{CycleZField}} {{AField}} {{CField}}
{{FField}}
MCALL CYCLE81({{Cycle_RAPTO}},0,{{Cycle_RAPTO}},{{Cycle_FEDTO}})
{{XFieldForce}} {{YFieldForce}}

[CYCLE_DRILL_HOLE]
; DRILL HOLE
{{XField}} {{YField}}

[CYCLE_DRILL_END]
; DRILL END
MCALL

; --- DRILL,DEEP (深孔) ---
[CYCLE_DRILL_DEEP_START]
; DRILL DEEP START

[CYCLE_DRILL_DEEP_FIRST_HOLE]
; DRILL DEEP FIRST HOLE
G0 {{XField}} {{YField}} {{CycleZField}} {{AField}} {{CField}}
{{FField}}
; TODO: 若机床要求深孔/枪钻循环，请在此替换为对应 CYCLE8x
MCALL CYCLE81({{Cycle_RAPTO}},0,{{Cycle_RAPTO}},{{Cycle_FEDTO}})
{{XFieldForce}} {{YFieldForce}}

[CYCLE_DRILL_DEEP_HOLE]
; DRILL DEEP HOLE
{{XField}} {{YField}}

[CYCLE_DRILL_DEEP_END]
; DRILL DEEP END
MCALL

; --- DRILL,BRKCHP (啄钻/断屑) ---
[CYCLE_DRILL_BRKCHP_START]
; DRILL BRKCHP START

[CYCLE_DRILL_BRKCHP_FIRST_HOLE]
; DRILL BRKCHP FIRST HOLE
G0 {{XField}} {{YField}} {{CycleZField}} {{AField}} {{CField}}
{{FField}}
; TODO: 若机床要求啄钻循环，请在此替换为对应 CYCLE8x（常见为 CYCLE83）
MCALL CYCLE81({{Cycle_RAPTO}},0,{{Cycle_RAPTO}},{{Cycle_FEDTO}})
{{XFieldForce}} {{YFieldForce}}

[CYCLE_DRILL_BRKCHP_HOLE]
; DRILL BRKCHP HOLE
{{XField}} {{YField}}

[CYCLE_DRILL_BRKCHP_END]
; DRILL BRKCHP END
MCALL

; --- BORE (镗孔) ---
[CYCLE_BORE_START]
; BORE START

[CYCLE_BORE_FIRST_HOLE]
; BORE FIRST HOLE
G0 {{XField}} {{YField}} {{CycleZField}} {{AField}} {{CField}}
{{FField}}
; TODO: 镗孔循环（按现场要求替换 CYCLE8x）
MCALL CYCLE81({{Cycle_RAPTO}},0,{{Cycle_RAPTO}},{{Cycle_FEDTO}})
{{XFieldForce}} {{YFieldForce}}

[CYCLE_BORE_HOLE]
; BORE HOLE
{{XField}} {{YField}}

[CYCLE_BORE_END]
; BORE END
MCALL

; --- BORE,BACK (背镗) ---
[CYCLE_BORE_BACK_START]
; BORE BACK START

[CYCLE_BORE_BACK_FIRST_HOLE]
; BORE BACK FIRST HOLE
G0 {{XField}} {{YField}} {{CycleZField}} {{AField}} {{CField}}
{{FField}}
; TODO: 背镗循环（按现场要求替换 CYCLE8x）
MCALL CYCLE81({{Cycle_RAPTO}},0,{{Cycle_RAPTO}},{{Cycle_FEDTO}})
{{XFieldForce}} {{YFieldForce}}

[CYCLE_BORE_BACK_HOLE]
; BORE BACK HOLE
{{XField}} {{YField}}

[CYCLE_BORE_BACK_END]
; BORE BACK END
MCALL

; --- TAP (攻丝) ---
[CYCLE_TAP_START]
; TAP START

[CYCLE_TAP_FIRST_HOLE]
; TAP FIRST HOLE
G0 {{XField}} {{YField}} {{CycleZField}} {{AField}} {{CField}}
{{FField}}
; TODO: 攻丝循环通常需要主轴/螺距等参数（后续可从 CLS 参数字典扩展）
MCALL CYCLE81({{Cycle_RAPTO}},0,{{Cycle_RAPTO}},{{Cycle_FEDTO}})
{{XFieldForce}} {{YFieldForce}}

[CYCLE_TAP_HOLE]
; TAP HOLE
{{XField}} {{YField}}

[CYCLE_TAP_END]
; TAP END
MCALL
