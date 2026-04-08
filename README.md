# Unity Figma Bridge

Import Figma designs into Unity as native UI. Supports frames, components, auto layout, prototype flows, fonts, and server-rendered vectors.

## Install

Open **Window > Package Manager**, click **+** > **Add package from git URL**, paste:

```
https://github.com/Afterhours-Studio/unity-figma-bridge.git
```

To lock a specific version:

```
https://github.com/Afterhours-Studio/unity-figma-bridge.git#v1.0.0
```

To always use the latest from main:

```
https://github.com/Afterhours-Studio/unity-figma-bridge.git#main
```

Requires **Unity 2021.3+** and **TextMeshPro** (import TMP Essentials if not already in project).

## Quick Start

1. Open **Figma Bridge > Open Window**
2. Paste your Figma Personal Access Token (get one from [Figma Developer Settings](https://www.figma.com/developers/api#authentication))
3. Go to **Settings** tab, paste your Figma document/page URL
4. Go to **Import** tab, select section and depth, click **Preview Document**
5. Choose which frames to import, click **Sync**

## Editor Window

The unified editor window (**Figma Bridge > Open Window**) has three tabs:

| Tab | Purpose |
|-----|---------|
| **Import** | Token, document info, section/depth selection, preview & sync |
| **Settings** | Import options, output path, auto layout, export mode |
| **Log** | Real-time import progress and error log |

### Import Options

| Setting | Description |
|---------|-------------|
| **Figma URL** | Document or page URL (supports `?node-id=` for specific pages) |
| **Section** | Filter import to a specific Figma section |
| **Sync Depth** | 0 = full, 1-5 = limit layer depth |
| **Export Only** | Only import nodes marked for Export in Figma |
| **Auto Layout** | Convert Figma auto layout to Unity Layout Groups |
| **Output Path** | Where to save imported assets (default: `Assets/Figma`) |

## How It Works

| Figma | Unity |
|-------|-------|
| Frame (on page/section) | Screen prefab + panel in scene |
| Component | Prefab in Components folder |
| Component Instance | Instantiated prefab with overrides |
| Image Fill | Downloaded PNG sprite |
| Vector/SVG | Server-rendered PNG |
| Text | TextMeshPro with matched font |
| Auto Layout | Horizontal/Vertical Layout Group |
| Prototype links | Button with transition |

### Folder Structure

Assets are organized by section and frame:

```
Assets/Figma/
  Output #1/
    Loading Scene/
      ImageFills/
      Renders/
      Screens/
    Main Menu/
      ...
  Components/
  Fonts/
  Pages/
```

## Binding Behaviours

MonoBehaviours are auto-bound to screens/components by name matching:

- A `MonoBehaviour` named `PlayScreen` auto-attaches to a frame named `PlayScreen`
- Serialized fields match child object names (depth 2)
- Use `[BindFigmaButtonPress("ButtonName")]` to bind button clicks
- Objects named `Button` or with prototype links get a `Button` component
- Objects named `SafeArea` get a safe area component

## Dependencies

Imported automatically:

- TextMeshPro 2.0.1
- Newtonsoft JSON 2.0.1

## Credits

**h1dr0n** - Development, architecture, and maintenance

Built on:
- [Inigo Quilez' 2D SDF Functions](https://iquilezles.org/articles/distfunctions2d/)
- [krzys-h's UnityWebRequestAwaiter](https://gist.github.com/krzys-h/9062552e33dd7bd7fe4a6c12db109a1a)
- [Jonathan Neal's Google Fonts list](https://github.com/jonathantneal/google-fonts-complete)

## License

MIT
