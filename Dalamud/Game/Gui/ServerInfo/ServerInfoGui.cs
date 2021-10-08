using System;
using System.Collections.Generic;
using System.Linq;

using Dalamud.IoC;
using Dalamud.IoC.Internal;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Serilog;

namespace Dalamud.Game.Gui.ServerInfo
{
    /// <summary>
    /// This class handles interacting with the native "Server Info" UI element.
    /// </summary>
    [PluginInterface]
    [InterfaceVersion("1.0")]
    public unsafe class ServerInfoGui : IDisposable
    {
        /// <summary>
        /// The amount of padding between Server Info UI elements.
        /// </summary>
        internal int ElementPadding = 30;

        private readonly GameGui gameGui;
        private readonly List<ServerInfoGuiData> elementList;

        internal ServerInfoGui(GameGui gameGui)
        {
            this.gameGui = gameGui;
            Service<Framework>.Get().Update += this.Update;
            this.elementList = new List<ServerInfoGuiData>();
        }

        /// <summary>
        /// Sets the given text on a text node in the Server Info in-game UI element. If a node does not
        /// exist with the given ID, it will be created. If a node does exist with the given ID, its text
        /// will be updated. Node IDs must be greater than 1000 in order to prevent collision with game IDs.
        /// </summary>
        /// <param name="nodeId">The unique node ID for this text element.</param>
        /// <param name="text">The text to display.</param>
        public void SetText(uint nodeId, string text)
        {
            if (nodeId < 1000)
            {
                Log.Error("Attempted to add a node with an ID less than 1000. " +
                          "This is disallowed for game compatibility. " +
                          "Please make your node ID larger than 1000.");
            }

            var index = -1;
            for (int i = 0; i < this.elementList.Count; i++)
            {
                if (this.elementList[i].NodeId == nodeId)
                    index = i;
            }

            if (index == -1)
            {
                var node = this.MakeNode(nodeId);
                var data = new ServerInfoGuiData
                {
                    TextNode = node,
                    NodeId = node->AtkResNode.NodeID,
                    Text = text,
                    Dirty = true,
                    Added = false,
                    ShouldBeRemoved = false,
                };

                this.elementList.Add(data);
            }
            else
            {
                var data = this.elementList[index];
                if (data.NodeId == 0 || data.TextNode == null)
                {
                    Log.Debug($"Failed to set text for node ID {nodeId} as either it was not found, or its node was null.");
                    return;
                }

                data.Dirty = true;
                data.Text = text;
                this.elementList[index] = data;
            }
        }

        /// <summary>
        /// Removes the node with the given unique ID from the Server Info in-game UI element.
        /// </summary>
        /// <param name="nodeId">The unique node ID of the node to remove.</param>
        public void RemoveText(uint nodeId)
        {
            var index = -1;
            for (int i = 0; i < this.elementList.Count; i++)
            {
                if (this.elementList[i].NodeId == nodeId)
                    index = i;
            }

            if (index == -1)
            {
                Log.Error($"Node ID {nodeId} could not be found to be removed.");
                return;
            }

            var data = this.elementList[index];
            data.ShouldBeRemoved = true;
            this.elementList[index] = data;
        }

        private AtkUnitBase* GetDtr()
        {
            return (AtkUnitBase*)this.gameGui.GetAddonByName("_DTR", 1).ToPointer();
        }

        private void Update(Framework unused)
        {
            var dtr = this.GetDtr();
            if (dtr == null) return;

            foreach (var data in this.elementList.Where(d => d.ShouldBeRemoved))
            {
                this.RemoveNode(data.TextNode);
            }

            this.elementList.RemoveAll(d => d.ShouldBeRemoved);

            // The collision node on the DTR element is always the width of its content
            var collisionNode = dtr->UldManager.NodeList[1];
            var runningXPos = collisionNode->X;

            for (int i = 0; i < this.elementList.Count; i++)
            {
                var data = this.elementList[i];

                if (data.Dirty && data.Added && data.TextNode != null)
                {
                    var node = data.TextNode;
                    node->SetText(data.Text);
                    ushort w = 0, h = 0;
                    node->GetTextDrawSize(&w, &h, node->NodeText.StringPtr);
                    node->AtkResNode.SetWidth(w);
                    data.Dirty = false;
                }

                if (!data.Added)
                {
                    data.Added = this.AddNode(data.TextNode);
                }

                runningXPos -= data.TextNode->AtkResNode.Width + this.ElementPadding;
                data.TextNode->AtkResNode.SetPositionFloat(runningXPos, 2);
                // runningXPos -= (ushort)(data.TextNode->AtkResNode.Width + elementPadding);

                this.elementList[i] = data;
            }
        }

        private bool AddNode(AtkTextNode* node)
        {
            var dtr = this.GetDtr();
            if (dtr == null || dtr->RootNode == null || node == null) return false;

            var lastChild = dtr->RootNode->ChildNode;
            while (lastChild->PrevSiblingNode != null) lastChild = lastChild->PrevSiblingNode;
            Log.Debug($"Found last sibling: {(ulong)lastChild:X}");
            lastChild->PrevSiblingNode = (AtkResNode*)node;
            node->AtkResNode.ParentNode = lastChild->ParentNode;
            node->AtkResNode.NextSiblingNode = lastChild;

            dtr->RootNode->ChildCount = (ushort)(dtr->RootNode->ChildCount + 1);
            Log.Debug("Set last sibling of DTR and updated child count");

            dtr->UldManager.UpdateDrawNodeList();
            Log.Debug("Updated node draw list");
            return true;
        }

        private bool RemoveNode(AtkTextNode* node)
        {
            var dtr = this.GetDtr();
            if (dtr == null || dtr->RootNode == null || node == null) return false;

            var tmpPrevNode = node->AtkResNode.PrevSiblingNode;
            var tmpNextNode = node->AtkResNode.NextSiblingNode;

            // if (tmpNextNode != null)
            tmpNextNode->PrevSiblingNode = tmpPrevNode;
            if (tmpPrevNode != null)
                tmpPrevNode->NextSiblingNode = tmpNextNode;
            node->AtkResNode.Destroy(true);

            dtr->RootNode->ChildCount = (ushort)(dtr->RootNode->ChildCount - 1);
            Log.Debug("Set last sibling of DTR and updated child count");
            dtr->UldManager.UpdateDrawNodeList();
            Log.Debug("Updated node draw list");
            return true;
        }

        private AtkTextNode* MakeNode(uint nodeId)
        {
            var newTextNode = (AtkTextNode*)IMemorySpace.GetUISpace()->Malloc((ulong)sizeof(AtkTextNode), 8);
            if (newTextNode == null)
            {
                Log.Debug("Failed to allocate memory for text node");
                return null;
            }

            IMemorySpace.Memset(newTextNode, 0, (ulong)sizeof(AtkTextNode));
            newTextNode->Ctor();

            newTextNode->AtkResNode.NodeID = nodeId;
            newTextNode->AtkResNode.Type = NodeType.Text;
            newTextNode->AtkResNode.Flags = (short)(NodeFlags.AnchorLeft | NodeFlags.AnchorTop);
            newTextNode->AtkResNode.DrawFlags = 12;
            newTextNode->AtkResNode.SetWidth(22);
            newTextNode->AtkResNode.SetHeight(22);
            newTextNode->AtkResNode.SetPositionFloat(-200, 2);

            newTextNode->LineSpacing = 12;
            newTextNode->AlignmentFontType = 5;
            newTextNode->FontSize = 14;
            newTextNode->TextFlags = (byte)TextFlags.Edge;
            newTextNode->TextFlags2 = 0;

            newTextNode->SetText(" ");

            newTextNode->TextColor.R = 255;
            newTextNode->TextColor.G = 255;
            newTextNode->TextColor.B = 255;
            newTextNode->TextColor.A = 255;

            newTextNode->EdgeColor.R = 142;
            newTextNode->EdgeColor.G = 106;
            newTextNode->EdgeColor.B = 12;
            newTextNode->EdgeColor.A = 255;

            return newTextNode;
        }

        public void Dispose()
        {
            for (int i = 0; i < this.elementList.Count; i++)
                this.RemoveNode(this.elementList[i].TextNode);
            this.elementList.Clear();
            Service<Framework>.Get().Update -= this.Update;
        }
    }
}
