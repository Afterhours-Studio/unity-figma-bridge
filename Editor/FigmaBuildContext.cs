using System.Collections.Generic;

namespace Afterhours.FigmaBridge.Editor
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
