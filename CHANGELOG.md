# Changelog

## [1.0.3] - 2026-04-10

### Added
- **9-Slice auto-detect** — rounded rectangles with `cornerRadius` and frames with a 3×3 Rectangle grid pattern (common button assets) are automatically set to `Image.Type.Sliced` with correct sprite borders.
- **`[9Slice]` / `[9Slice:N]` naming convention** — tag nodes in Figma to force 9-slice with auto or explicit border size. Supports `:` and `_` separators.
- **`AutoSlice9` setting** (default on) — toggle 9-slice auto-detection in Build settings.
- **Incremental sync** — content hash (SHA256) computed per frame; unchanged frames skip asset downloads on re-sync. `.synced` files upgraded from plain text to JSON manifest with `contentHash`, `documentVersion`, `lastModified`, `syncedAt`.
- **Sync timestamp in Build tab** — each frame row shows last sync time.
- **Server render dedup** — all server-rendered nodes (not just Export) are deduplicated by name, preventing redundant downloads of shared assets like buttons.
- **`StripConventionTags`** — `[Button]`, `[9Slice:N]`, `[RectMask2D]` tags are stripped from filenames when saving server-rendered images.
- **Outline width quantization** — TMP outline material presets are bucketed into Thin/Medium/Thick to reduce preset count.

### Fixed
- Server-rendered nodes used `absoluteRenderBounds` for size/position, which includes children's effects (shadows) and inflated the RectTransform. Now uses `node.size` and `absoluteBoundingBox`.
- `[9Slice:N]` explicit border was incorrectly padded +1px. Now only auto-detected `cornerRadius` borders get +1 padding.

## [1.0.2] - 2026-04-09

### Added
- **Grid auto-layout** — Figma `layoutMode: GRID` maps to `GridLayoutGroup` with `FixedColumnCount`, cell size from first child, and correct gap/padding.
- **Wrap auto-layout** — Figma horizontal frames with `layoutWrap: WRAP` map to `GridLayoutGroup` with `Flexible` constraint (auto-wrap based on container width).
- **Constraint support in clean build path** — Figma constraints (LEFT, RIGHT, CENTER, LEFT_RIGHT, SCALE) now applied to RectTransform anchors for all nodes, including server-rendered images.
- **SPACE_BETWEEN alignment** — vertical and horizontal auto-layout frames with `SPACE_BETWEEN` no longer crash; alignment maps to correct `TextAnchor`.
- **Log newest-first** — log tab now shows newest entries at the top.
- **Token cancel button** — cancel button during token input.
- **Settings card rename** — settings tab UI cleanup.
- **Import/Build tab split** — settings reorganized into separate Import and Build sections.
- **SmartNaming** — `FigmaNodeNaming` formats GameObject names to clean snake_case/PascalCase.

### Fixed
- Constraints were lost on nodes with server-rendered images (early return bypassed `ApplyFigmaConstraints`).
- GROUP parent nodes with null `size` field caused wrong anchor/position calculations — now falls back to `absoluteBoundingBox`.
- SCALE constraint produced messy fractional anchors — now treated as stretch (same as LEFT_RIGHT / TOP_BOTTOM).

### Removed
- `OnlyImportSelectedPages` setting (replaced by page selection in Import tab).

## [1.0.1] - 2026-04-09

### Added
- **TMP outline materials** — text nodes with a Figma stroke automatically generate named TMP material presets (e.g. `Baloo_400_SDF_Outline_0417`). Outline renders fully outside the glyph via `FaceDilate = OutlineWidth`.
- **Synced-frame markers** — a `.synced` marker is written per frame after each import. The Build tab only lists frames that have been synced at least once and refreshes automatically after sync.
- **Export dedup** — import skips nodes whose export asset already exists anywhere under the sections folder, preventing duplicates across frames and re-syncs.
- **`SkipTextImages` setting** (default on) — TEXT nodes are never server-rendered; always built as TMP/Text components.
- **`TextMode` setting** — `Auto` (TMP if available), `TextMeshPro`, or `LegacyText`.
- **`CanvasWidth` / `CanvasHeight` settings** (default 1080 × 2400) — applied to the scene `CanvasScaler` reference resolution.
- **`SyncDepth` setting** — limits layer descent depth (0 = unlimited).

### Fixed
- Text fill color was always white — now resolved correctly from `node.fills → node.style.fills → black`.
- `[Button]` and `[RectMask]` tags were silently skipped on server-rendered nodes.
- Build step downloaded fonts for the entire document — now scoped to the specific frame being built.
- Renamed Google Fonts (e.g. `Baloo` → `Baloo 2`) failed to resolve — fixed via prefix fallback.
- Assets downloaded for one frame were not reused when building a different frame.
- `google-fonts.json` was not found when the package was installed as a git URL — fixed via `AssetDatabase.FindAssets`.

---

## [1.0.0] - 2026-04-09

### Added
- **Import pipeline** — downloads assets only (server renders, image fills, fonts) for selected frames; no scene building. Document cached to `.figma-cache.json` for the Build step.
- **Build pipeline** — separate step that reads the cache and generates the prefab hierarchy in-scene.
- **Build tab** — lists importable frames from the cached document; build individual frames on demand.
- **Import tab** — token input, document preview, section filter, per-frame selection, sync button.
- **Settings tab** — document URL, output path, auto layout, export-only mode, page selection, render scale.
- **Log tab** — real-time import/build progress and errors.
- **Section filter** — restrict import to a named Figma section; sections auto-fetched from the API.
- **Frame selection** — preview all frames before syncing; existing assets flagged in the UI.
- **Sync depth** — configurable layer depth limit.
- **Export-only mode** — import only nodes marked for Export in Figma.
- **Page selection** — import specific pages instead of the whole document.
- **`FigmaImage` component** — renders image fills, server-rendered vectors, and SDF shapes (ellipse, star).
- **TMP text support** — text nodes built as TextMeshPro components with font matching and character spacing correction.
- **Google Fonts integration** — missing fonts downloaded automatically (opt-in).
- **Auto Layout** — Figma Auto Layout converted to Unity `HorizontalLayoutGroup` / `VerticalLayoutGroup`.
- **`SafeArea` component** — auto-attached to nodes named `SafeArea`.
- **`Button` component** — auto-attached to nodes tagged `[Button]` in their name.
- **Parallel downloads** — up to 8 concurrent asset downloads with progress reporting and retry logic.
- **Configurable output path** — assets organized under `Assets/Figma/Sections/{Section}/{Frame}/`.
