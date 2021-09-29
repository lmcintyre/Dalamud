using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Dalamud.Game.Gui.ServerInfo
{
    internal unsafe struct ServerInfoGuiData
    {
        public AtkTextNode* TextNode;
        public uint NodeId;
        public string Text;
        public bool Dirty;
        public bool Added;
        public bool ShouldBeRemoved;

        public override bool Equals(object? obj)
        {
            if (obj is ServerInfoGuiData other)
                return this.TextNode == other.TextNode && this.NodeId == other.NodeId;
            return false;
        }
    }
}
