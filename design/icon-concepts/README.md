# toolbAX app-icon concepts

Six candidate app icons (256×256 SVG), playing on the name's pun — **AX** as a tool/axe and as
Microsoft Dynamics **AX**. The **active** icon is **Keenedge** (`keenedge.svg`): a polished-steel
axe head with a cyan edge on a deep-indigo squircle.

The active icon is rasterized into `avalonia/toolBax.App/Assets/`:
- `icon.png` — the runtime window / taskbar icon (`avares:///Assets/icon.png`)
- `toolbax.ico` — the published `.exe` icon (multi-size: 16/24/32/48/64/128/256)

## Swapping the icon

Pick a different `*.svg` here, then re-rasterize with the bundled tool and rebuild:

```powershell
dotnet run --project design/icongen -- design/icon-concepts/<name>.svg out
copy out\toolbax.ico avalonia\toolBax.App\Assets\toolbax.ico
copy out\icon.png     avalonia\toolBax.App\Assets\icon.png
dotnet build avalonia/toolBax.slnx -c Release
```

`design/icongen` is a throwaway SkiaSharp + Svg.Skia tool (not part of either solution or CI). It
renders the SVG at every icon size and packs a PNG-in-ICO.

## Concepts (ranked by an independent design judge)

| Judge | File | Name | Note |
|------:|------|------|------|
| 9 | kerf.svg | Kerf | AX ligature that stays a bold A-chevron when shrunk; textbook Linear/Raycast restraint. Runner-up to ship. |
| 8 | honed.svg | Honed | Abstract faceted blade/prism — most "design-studio"; the AX pun is implicit. |
| 7 | keenedge.svg | **Keenedge (active)** | Precision axe — the literal pun; distinctive silhouette that still reads at 16px. |
| 5 | ax-toolbox.svg | AX Toolbox | Toolbox + hidden A-void + X; conceptually overstuffed, muddies when shrunk. |
| 5 | bridgehex.svg | Bridgehex | Dataverse hexagon + dual-write bridge; best concept fit, too detail-dense at 16px. |
| 4 | crossaxe.svg | CrossAxe | Crossed wrench + screwdriver; the clip-art trope, weakest small. |
