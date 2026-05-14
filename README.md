# FwxPostProcessing

NX CLS to Siemens NC post-processing platform with template-driven output.

## Features
- 3-axis / 3+2 / 5-axis mode detection
- AC head kinematics (A around X, C around Z)
- Template event system (`EVENT_*` sections)
- Multi-path CLS support (`TOOL PATH` / `END-OF-PATH`)
- Hole cycle support (`CYCLE/* ... CYCLE/OFF`)
- WPF preview and save workflow

## Projects
- `PostProcessor.Core`
- `PostProcessor.Wpf`
- `Samples`

## Quick Start
1. Open `PostProcessor.Wpf`.
2. Select CLS input file(s) or folder.
3. Select template (`.tpl`).
4. Click `Preview`.
5. Click `Save`.

## Notes
- Output style is controlled by template sections in:
  `PostProcessor.Core/Templating/Templates/Siemens_AC_TRAORI.tpl`
- Core pipeline entry:
  `PostProcessor.Core/Processing/PostProcessorEngine.cs`
