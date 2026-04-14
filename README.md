# Unity Figma Bridge

Import Figma designs into Unity as native UI. Supports frames, auto layout, fonts, image fills, and server-rendered vectors.

## Install

Open **Window > Package Manager**, click **+** > **Add package from git URL**, paste:

```
https://github.com/Afterhours-Studio/unity-figma-bridge.git
```

To lock a specific version:

```
https://github.com/Afterhours-Studio/unity-figma-bridge.git#v1.0.4
```

To always use the latest from main:

```
https://github.com/Afterhours-Studio/unity-figma-bridge.git#main
```

Requires **Unity 2021.3+** and **TextMeshPro** (import TMP Essentials if not already in your project).

## Quick Start

1. Open **Figma Bridge > Open Window**
2. Enter your Figma Personal Access Token ([Figma Developer Settings](https://www.figma.com/developers/api#authentication))
3. Go to the **Settings** tab, paste your Figma document or page URL
4. Go to the **Import** tab, pick a section and sync depth, click **Preview Document**
5. Select which frames to import and click **Sync**
6. Switch to the **Build** tab to build synced frames into prefabs

## Editor Window

Open via **Figma Bridge > Open Window**. The window has four tabs:

| Tab | Purpose |
|-----|---------|
| **Import** | Token input, document preview, section/depth selection, per-frame sync |
| **Build** | List of synced frames; build individual frames into Unity prefabs |
| **Settings** | Import options, output path, text rendering, canvas size |
| **Log** | Real-time import and build progress, errors |

## Settings

| Setting | Description |
|---------|-------------|
| **Figma URL** | Document, page, or frame URL (supports `?node-id=`) |
| **Scene Path** | Runtime scene path - created automatically if it does not exist |
| **Output Path** | Root folder for imported assets (default `Assets/Figma`) |
| **Canvas Size** | Reference resolution for `CanvasScaler` (default 1080 × 2400) |
| **Render Scale** | Scale multiplier for server-rendered images |
| **Auto Layout** | Convert Figma Auto Layout to Unity Layout Groups |
| **Google Fonts** | Download missing fonts from Google Fonts automatically |
| **Skip Text Images** | Never server-render TEXT nodes - always use TMP/Text (recommended) |
| **Text Mode** | `Auto` (TMP if available), `TextMeshPro`, or `LegacyText` |
| **Export Only** | Only import nodes marked for Export in Figma |
| **Select Pages** | Import only selected pages instead of all |
| **Sync Depth** | Layer depth limit: 0 = full, 1 = top-level only, N = descend N levels |
| **Smart Naming** | Format node names to `snake_case` / `PascalCase`; `[Tags]` stripped automatically |
| **Auto 9-Slice** | Auto-detect rounded rectangles and set `Image.Type.Sliced` with borders from `cornerRadius` |
| **Char Spacing** | `Fixed`: constant TMP character spacing offset (default −0.7). `Auto`: read from Figma node |

## How It Works

### Import → Sync

Each **Sync** call targets a single frame. The importer:

1. Fetches the Figma document for the selected frame
2. Resolves fonts - downloads from Google Fonts if enabled
3. Downloads image fills and server-rendered vectors (SVG, complex shapes)
4. Walks the node tree and creates GameObjects, applying layout and text properties
5. Saves output assets (sprites, fonts, TMP material presets) and writes a `.synced` marker

### Build

The **Build** tab lists every frame that has been synced at least once. Clicking **Build** on a frame:

1. Loads the cached Figma document for that frame
2. Resolves the font map scoped to that frame only
3. Generates the prefab hierarchy and saves it under the frame's output folder

### Figma → Unity mapping

| Figma | Unity |
|-------|-------|
| Frame | Prefab + RectTransform hierarchy |
| Image fill | Downloaded PNG sprite on `Image` |
| Linear / radial gradient fill | `FigmaImage` with SDF gradient shader |
| Vector / SVG | Server-rendered PNG on `Image` |
| Text (no stroke) | TextMeshPro component |
| Text (with stroke) | TextMeshPro + named outline material preset |
| Text overflow TRUNCATE | TMP `Ellipsis` overflow + normal wrap |
| Auto Layout (H/V) | `HorizontalLayoutGroup` / `VerticalLayoutGroup` |
| Auto Layout (Grid) | `GridLayoutGroup` with `FixedColumnCount` |
| Auto Layout (Wrap) | `GridLayoutGroup` with `Flexible` constraint |
| Constraints | RectTransform anchors (LEFT, RIGHT, CENTER, stretch, scale) |
| Ellipse / Star | `FigmaImage` with SDF shape |
| `[Button]` tag in name | Unity `Button` component added |
| `[RectMask2D]` tag in name | Unity `RectMask2D` component added |
| `[9Slice]` / `[9Slice:N]` tag | `Image.Type.Sliced` with auto or explicit border |
| `SafeArea` name | `SafeArea` component added |

### Folder Structure

Assets are organized by section and frame name:

```
Assets/Figma/
  Sections/
    Output #1/
      Loading Scene/
        ImageFills/
        Renders/
      Main Menu/
        ...
  Fonts/
  FontMaterialPresets/
```

## Text Rendering

Text nodes are rendered as TMP components by default (`SkipTextImages` on, `TextMode` Auto).

- **Font matching** - font assets are matched by family name and weight. Missing fonts are downloaded from Google Fonts if enabled.
- **Outline materials** - text nodes with a Figma stroke generate a named TMP material preset (e.g. `Baloo_400_SDF_Outline_0417`), stored in `FontMaterialPresets/`. The outline renders fully outside the glyph using `FaceDilate = OutlineWidth`.

## Dependencies

Installed automatically via Package Manager:

- TextMeshPro 2.0.1
- Newtonsoft JSON 2.0.1-preview.1

## Credits

**h1dr0n** - Development, architecture, and maintenance

## License

MIT
