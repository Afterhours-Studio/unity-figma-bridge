using System;

namespace UnityFigmaBridge.Editor.Utils
{
    [Serializable]
    internal sealed class FrameRecord
    {
        public string id;
        public string name;
        public string sectionName;
        public string pageName;

        public FrameRecord() { }

        public FrameRecord(string id, string name, string sectionName, string pageName)
        {
            this.id = id;
            this.name = name;
            this.sectionName = sectionName;
            this.pageName = pageName;
        }
    }
}
