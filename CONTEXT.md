# VR.HDRFix — Project Context

Working notes for ongoing changes. This file describes how the project works now, why the algorithm is built the way it is, and what is still open. For the user-facing description, see `README.md`.

---

## 1. Purpose

NVIDIA's capture overlay (Alt+F1 with HDR on) saves HDR screenshots as **JPEG XR (`.jxr`)**, linear scRGB float. This Windows Service watches folders for new `*.jxr` files and writes `<name>-sdr.jpg` (or `.png`) versions that look good on SDR screens, so they can be shared with friends who don't have HDR displays.

It started as a .NET rewrite of [bvibber/hdrfix](https://github.com/bvibber/hdrfix) (Rust). The tone mapping has since been redesigned (see §4); only the general idea and the Oklab math remain from the original.

---

## 2. Tech stack

| Item | Value |
|---|---|
| Runtime | .NET 10 (`net10.0-windows`), nullable enabled |
| Hosting | Generic Host + `UseWindowsService` (service name `HDRFixService`) |
| Image I/O | WPF imaging (wraps Windows WIC): `WmpBitmapDecoder` for JXR, `JpegBitmapEncoder` / `PngBitmapEncoder` |
| Logging | Serilog file sink: `<exe dir>/logs/`, daily rolling, 7 days kept |
| Config | `appsettings.json`, hot-reloaded through `IOptionsMonitor<Settings>` |
| Publish | `FolderProfile.pubxml` → `D:\Programs\VR.HDRFix`, win-x64, framework-dependent, single-file |
| Install | `Scripts/install.bat` / `uninstall.bat` (`sc create HDRFixService`, auto start, restart on failure) |
| Tests | `VR.HDRFix.Tests` (xUnit): math, tone mapper, settings, output paths |
| Manual tool | `VR.HDRFix.Tester`: CLI converter that uses the same pipeline |

---

## 3. Layout

```
VR.HDRFix/
  Program.cs                Bootstrap: Serilog, host, BelowNormal process priority
  HdrWorker.cs              Watchers → queue → consumers; retries, startup scan, self-healing watchers
  Configs/Settings.cs       Service settings (folders, retries, scan, concurrency)
  Configs/HdrFixSettings.cs Conversion settings + Sanitized() (clamps every value to a safe range)
  Enums/OutputFormat.cs     Jpeg | Png
  Helpers/HdrPipeline.cs    ConvertFile: Decode → ToneMapper → Render → Encode (temp file + move)
  Helpers/ToneMapper.cs     Per-image exposure + highlight shoulder + gamut mapping
  Helpers/HdrMath.cs        Oklab, shoulder curve, gamut mapping, exact sRGB encode (LUT)
  Helpers/OutputPaths.cs    Output path rules (mirror sub-folders), folder normalization
  Helpers/StringConstants.cs
  Models/HdrImage.cs        Decoded image (float RGBA scRGB)
  Models/ImageStats.cs      Percentile analysis (sampled, ≤ 500k pixels)
VR.HDRFix.Tester/           CLI: Tester [file|folder] [outDir] [--force] [--png] [--white=N] [--exposure=S] [--saturation=X] [--contrast=X]
VR.HDRFix.Tests/            Unit tests
```

---

## 4. The conversion algorithm (and why)

### What was wrong before
Measured on real captures (RDR2, Apex, Cyberpunk, 1080p, `Rgba128Float`):
- Games put **paper white at about 200–400 nits** (scRGB 2.5–5), and peaks at 400–1200 nits. The old code treated scRGB 1.0 (80 nits) as white and squeezed everything through a fixed Hable curve, so midtones ended up in the highlight region.
- Hable's built-in "overbright" blend **desaturates everything above about 160 nits toward grey**. In Apex that's a third of the image, which is the main cause of the "washed out" look.
- Up to **6% of pixels are outside the sRGB gamut** (negative channels, Cyberpunk neon). They were hard-clipped, which shifts hue.
- The sRGB encode had a formula bug (inherited from the Rust tool): white came out as 246, not 255. Values were also truncated instead of rounded.

### Current pipeline (`ToneMapper.Map`, per pixel)
1. **Exposure:** divide by the per-image *white point* W (nits) → 1.0 = SDR white.
   - Auto (default): `W = clamp(max(P99(luminance) / 2^AutoExposureBias, LogAverage / MaxSceneKey), MinWhitePointNits, MaxWhitePointNits)`, with defaults P99, bias 0.25 stops, key 0.1, range 80–400 nits. The scene-key term only affects high-key scenes such as sunlit Apex sand. Without it, most of the image ends up in the shoulder and looks bleached.
   - Fixed: `WhitePointNits > 0`.
   - `Exposure` (stops) is applied on top in both modes.
2. **Contrast:** filmic power curve in log space around mid-grey 0.18, `Y' = 0.18 · (Y/0.18)^Contrast` (default 1.2), applied to luminance as a common RGB factor so hue is kept. The shoulder peak goes through the same curve, so it still lands on 1.0.
3. **Saturation:** Oklab chroma × `Saturation` (default 1.25). This compensates for SDR looking less colorful than the same scene in HDR.
4. **Highlight shoulder** on max(R,G,B), scaling all channels together so hue is kept. It is the identity below `ShoulderStart` (0.7), then an extended-Reinhard roll-off that is C1-continuous at the knee and reaches exactly 1.0 at the image peak (P99.9 of max RGB).
5. **Path to white:** `blend = 1 − 1/(HighlightDesaturation · (max − mapped) + 1)`, the same idea as the Khronos PBR Neutral tone mapper. Only strongly compressed highlights (sun, lamps) fade toward white.
6. **Gamut mapping:** any out-of-range color gets its Oklab chroma reduced (binary search) at constant L and hue until it fits in sRGB.
7. **Encode:** exact sRGB transfer through a 16k-entry LUT, rounded.

### How the defaults were chosen
Side-by-side renders of all 9 test captures:
- I compared max-RGB against luminance-based shoulders. Max-RGB looked more natural and never pushes colors out of range.
- I compared white at P99, at P98, and at P99 + 0.5 stop. P99 + 0.5 stop matched the old version's overall brightness while fixing haze and colors. Plain P99 made RDR2 too dark.
- I chose a saturation boost of 1.1, which stays subtle.

Second round (feedback: "still too bright, too white, colors washed"). I measured the outputs with the scratch harness (mean and median Oklab L, share of near-white pixels, mean chroma). Only about 1–2% of pixels were actually near white. The real problem was **flat contrast and low chroma**. HDR game grades keep shadows lifted and the curve close to linear, because the display has headroom; the SDR grade of the same game uses a filmic curve with a toe. Colors also look more saturated at HDR brightness (Hunt effect). Changes:
- Added `Contrast` 1.2. 1.25 crushed the RDR2 shadows too much.
- Raised `Saturation` from 1.1 to 1.25.
- Lowered the bias from 0.5 to 0.25 stops.
- Added `MaxSceneKey` 0.1. Apex had keys of 0.13 and 0.19; every other capture is at or below 0.05.
- Lowered `HighlightDesaturation` from 0.15 to 0.05, so neon keeps its color.

The auto white points on the test set are now: Apex 267–400, Cyberpunk 290–343, RDR2 80–234 nits.

### Why not a NuGet package
- I didn't find a .NET package that does display-referred HDR→SDR mapping for game captures.
- OpenCvSharp's tonemappers (Drago, Reinhard, Mantiuk) target photographic HDR merges.
- Magick.NET has no JXR support.
- Direct2D's HDR Tone Map effect (via Vortice) would work, but it adds a D3D/GPU dependency to a headless service and offers less control.
- The math is about 200 lines and fully unit-tested.

---

## 5. Service behavior (`HdrWorker`)

- **Events only enqueue work.** One `FileSystemWatcher` per folder (recursive, `*.jxr`, Created/Changed/Renamed, 64 KB buffer) writes into an unbounded `Channel`. A `_pending` set deduplicates entries.
- **Consumers:** `MaxConcurrentConversions` (default 1). Each conversion uses up to `MaxThreadsPerConversion` threads (0 = half the cores). The process runs at **BelowNormal** priority so the game being captured isn't slowed down.
- **Per file:**
  - skip if the output already exists,
  - wait until the file size is stable,
  - convert,
  - on **any** error, retry `Retries` × `DelayMs`.
- **Atomic output:** the image is written to `<out>.tmp`, then moved into place. The output gets the source file's timestamps, which keeps galleries sorted by capture time.
- **Self-healing:**
  - watcher `Error` events, config changes and missing folders (checked every 30 s) all request a rebuild,
  - a 2 s monitor loop performs the rebuild, which collapses duplicate config-reload events into one,
  - after every rebuild, a **scan** queues unconverted files modified within `ScanMaxAgeHours` (24 h). The same scan runs on startup.
- **Memory:** about 33 MB float buffer per 1080p image (133 MB at 4K). The LOH is compacted when the queue empties. Idle working set is about 73 MB.

---

## 6. Config reference (`appsettings.json` → `Settings`)

| Key | Default | Meaning |
|---|---|---|
| WatchFolders | – | Folders watched recursively |
| OutputPath | – | Output root (sub-folders mirrored). Empty = next to the source |
| Retries / DelayMs | 10 / 500 | Retry policy per file |
| EnableLogging | true | File log in `logs/` |
| ScanMaxAgeHours | 24 | Startup/rebuild scan window; 0 = off |
| MaxConcurrentConversions | 1 | Parallel files |
| MaxThreadsPerConversion | 0 | 0 = half the logical CPUs |
| HdrFixSettings.WhitePointNits | 0 | 0 = auto per image; otherwise a fixed paper white in nits |
| …AutoWhitePercentile | 99 | Luminance percentile used for auto white |
| …AutoExposureBias | 0.25 | Stops brighter than the detected white |
| …MaxSceneKey | 0.1 | Max log-average luminance relative to white; raises white for bright scenes. 0 = off |
| …Min/MaxWhitePointNits | 80 / 400 | Clamp for auto white |
| …Exposure | 0 | Manual stops, always applied |
| …Saturation | 1.25 | Oklab chroma multiplier |
| …Contrast | 1.2 | Filmic contrast around mid-grey; 1.0 = linear |
| …ShoulderStart | 0.7 | Where highlight compression begins |
| …HighlightPercentile | 99.9 | Percentile (of max RGB) treated as the image peak |
| …HighlightDesaturation | 0.05 | How much compressed highlights fade to white |
| …OutputFormat / JpegQuality | Jpeg / 95 | `Jpeg` or `Png` |

All values are clamped by `HdrFixSettings.Sanitized()`, so a missing or broken key can no longer produce a white or NaN image.

**Tuning hints:**
- Image too dark → `AutoExposureBias` 0.5–0.75.
- Shadows too crushed → `Contrast` 1.1. Still flat → 1.3.
- Too bright → `AutoExposureBias` 0, or `MaxSceneKey` 0.08.
- Colors too strong → `Saturation` 1.1.
- Want blown light sources to fade to white more → `HighlightDesaturation` 0.15.

---

## 7. Open items / ideas

- **PQ (HDR10) input:** JXR `RGB101010` captures and HDR PNGs would need an ST.2084 decode plus a Rec.2020→709 matrix. The captures seen so far are all `Rgba128Float`, so this hasn't been needed.
- **JPEG chroma subsampling:** WPF's JPEG encoder uses 4:2:0, which slightly softens saturated red/blue edges (HUD text). Options: PNG output, or WIC's encoder options through COM interop or another encoder.
- **Delete or move** the `.jxr` after conversion (optional).
- **Embed EXIF** capture date and game name.
- **WPF in a service:** each thread-pool thread that touches WPF imaging keeps a Dispatcher. The number of threads is bounded, so this works fine in practice; direct WIC interop would remove the dependency.
- The service runs as **LocalSystem**: output files are owned by SYSTEM and mapped network drives are not visible.
