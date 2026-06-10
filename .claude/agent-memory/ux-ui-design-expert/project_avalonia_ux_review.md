---
name: avalonia-ux-review
description: Context for UX/UI critiques of the toolBax Avalonia screens — design-token inventory, the no-spacing/typography-token gap, and the stale-screenshot caveat.
metadata:
  type: project
---

UX review of the toolBax Avalonia app (POST Builder + Query Builder) was requested 2026-06-09 after the user called the UX "garbage". Fixes must work WITHIN the existing token system (`avalonia/toolBax.App/Themes/Tokens.axaml`), not a rebrand.

**Why:** the screens read as dense raw-data dumps with weak hierarchy; the user wants tasteful, concrete AXAML-level fixes plus reusable design rules and headless-testable design-taste assertions.

**How to apply:**
- Token system has COLOR tokens (Text0-3, Layer1-4, Mica, Accent+variants, Ok/Warn/Err/Info + Bg variants, Stroke/Stroke2/Divider/SubtleHover) and SHAPE tokens (CornerRadiusControl/Card/Pill) — but NO spacing or typography tokens. Spacing/font-size are hardcoded inline on every view. The biggest reusable win is adding a spacing scale + type-ramp resources (sizes/weights are already specified in `docs/design_handoff_avalonia/design-tokens.md` §Typography and §Shape & spacing — encode them as resources).
- STALE-SCREENSHOT CAVEAT: the two screenshots in `artifacts/screenshots/` predate commits `eb1bfdb` (Query field-list scaling: bounded MaxHeight=190 virtualizing ListBox + search + select-all/clear) and `10be406` ($expand joins). They also predate the POST Builder field-DataGrid. So the two headline anti-patterns (POST wall-of-"X is mandatory" sentences; unbounded Query field WrapPanel) are ALREADY fixed in source. Always review current `.axaml`, not the screenshots. The remaining live issue is the Joins ($expand) WrapPanel (still unbounded/unsearchable, `QueryBuilderView.axaml` ~line 110).
- Headless render tests live in `avalonia/toolBax.App.Tests/*ViewRenderTests.cs` (Avalonia.Headless.XUnit, `GetVisualDescendants()` + `Dispatcher.UIThread.RunJobs()`). They currently assert presence of controls only — they're the natural home for structural "design-taste" assertions (bounded list heights, token usage, no per-field validation spam).
