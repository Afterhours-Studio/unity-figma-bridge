using System.Collections.Generic;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Fonts;
using UnityFigmaBridge.Editor.Settings;

namespace UnityFigmaBridge.Editor
{
    /// <summary>
    /// Data passed through the build pipeline for a single frame
    /// </summary>
    public class FigmaBuildContext
    {
        public UnityFigmaBridgeSettings Settings;
        public FigmaFile SourceFile;
        public FigmaFontMap FontMap;
        public List<ServerRenderNodeData> ServerRenderNodes = new List<ServerRenderNodeData>();
    }
}
