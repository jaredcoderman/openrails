using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Orts.Formats.Msts;

namespace TdbDump
{
    public class TrackBuilder
    {
        private float _x = 0;
        private float _z = 0;
        private int _tileX = -12842;
        private int _tileZ = 14734;
        private int _nextNodeID = 1;
        private float _ay = -2.7f;

        private List<TrackNode> _nodes;
        private Dictionary<uint, TrackPrimitive> _primitives;
        public IReadOnlyCollection<TrackPrimitive> Primitives => _primitives.Values;

        private void InitializePrimitives()
        {
            string json = File.ReadAllText("primitives.json");

            PrimitiveFile file = JsonConvert.DeserializeObject<PrimitiveFile>(json);
            if (file == null)
                return;

            _primitives = new Dictionary<uint, TrackPrimitive>();

            uint sectionIndex = 40001;

            foreach (var primitive in file.Segments)
            {
                primitive.SectionIndex = sectionIndex;
                _primitives.Add(sectionIndex, primitive);
                sectionIndex++;
            }
        }

        public TrackBuilder()
        {
            _nodes = new List<TrackNode>();
            _primitives = new Dictionary<uint, TrackPrimitive>();

           InitializePrimitives();
        }

        public void AddStraight(uint sectionIndex)
        {
            TrackNode node = new TrackNode();

            node.Id = _nextNodeID;
            node.Section = new TrVectorSection();
            int relativeTileX = (int)Math.Floor((double)(_x + 1024) / 2048);
            int relativeTileZ = (int)Math.Floor((double)(_z + 1024) / 2048);
            node.Section.TileX = _tileX + relativeTileX;
            node.Section.TileZ = _tileZ + relativeTileZ;
            node.Section.X = _x - relativeTileX * 2048f;
            node.Section.Z = _z - relativeTileZ * 2048f;
            node.Section.AY = _ay;
            node.Section.SectionIndex = sectionIndex;

            _nodes.Add(node);

            // For straights: Length field contains the distance
            float length = _primitives[sectionIndex].Length;

            _x += length * (float)Math.Sin(_ay);
            _z += length * (float)Math.Cos(_ay);

            _nextNodeID++;
        }

        public void AddCurve(uint sectionIndex)
        {
            TrackPrimitive primitive = _primitives[sectionIndex];

            TrackNode node = new TrackNode();

            node.Id = _nextNodeID;
            node.Section = new TrVectorSection();
            int relativeTileX = (int)Math.Floor((double)(_x + 1024) / 2048);
            int relativeTileZ = (int)Math.Floor((double)(_z + 1024) / 2048);
            node.Section.TileX = _tileX + relativeTileX;
            node.Section.TileZ = _tileZ + relativeTileZ;
            node.Section.X = _x - relativeTileX * 2048f;
            node.Section.Z = _z - relativeTileZ * 2048f;

            node.Section.AY = _ay;
            node.Section.SectionIndex = sectionIndex;

            float dx =
                primitive.LocalEndX * (float)Math.Cos(_ay) +
                primitive.LocalEndZ * (float)Math.Sin(_ay);

            float dz =
               -primitive.LocalEndX * (float)Math.Sin(_ay) +
                primitive.LocalEndZ * (float)Math.Cos(_ay);

            _x += dx;
            _z += dz;

            _ay += primitive.SignedAngle;

            _nodes.Add(node);
            _nextNodeID++;
        }

        public void AddRightCurve()
        {
            AddCurve(40003);
        }

        public void AddLeftCurve()
        {
            AddCurve(40004);
        }

        public List<TrackNode> Build()
        {
            // Create list combining end nodes and vector nodes in proper order
            var allNodes = new List<object>();  // Can be TrackNode or TrEndNode
            
            // Add first end node (before all vector nodes)
            if (_nodes.Count > 0)
            {
                var firstVectorNode = _nodes[0];
                var firstEndNode = new TrEndNode
                {
                    Id = 1,  // Start with ID 1
                    TileX = firstVectorNode.Section.TileX,
                    TileZ = firstVectorNode.Section.TileZ,
                    X = firstVectorNode.Section.X,
                    Y = firstVectorNode.Section.Y,
                    Z = firstVectorNode.Section.Z,
                    AX = 0,
                    AY = firstVectorNode.Section.AY,
                    AZ = 0
                };
                firstEndNode.Pins.Add(new TrPin(firstVectorNode.Id, 1));  // Output to first vector node
                allNodes.Add(firstEndNode);
            }
            
            // Add all vector nodes with proper pin linking
            for (int i = 0; i < _nodes.Count; i++)
            {
                TrackNode node = _nodes[i];
                
                // Add input pin (from previous node, if not first)
                if (i > 0)
                {
                    node.Pins.Add(new TrPin(_nodes[i - 1].Id, 0));
                }
                else
                {
                    // First vector node connects to end node
                    node.Pins.Add(new TrPin(1, 0));  // Connect to first end node with ID 1
                }
                
                // Add output pin (to next node, if not last)
                if (i < _nodes.Count - 1)
                {
                    node.Pins.Add(new TrPin(_nodes[i + 1].Id, 1));
                }
                else
                {
                    // Last vector node connects to end node (will be added next)
                    node.Pins.Add(new TrPin(_nextNodeID, 1));
                }
                
                allNodes.Add(node);
            }
            
            // Add last end node (after all vector nodes)
            if (_nodes.Count > 0)
            {
                var lastVectorNode = _nodes[_nodes.Count - 1];
                var lastEndNode = new TrEndNode
                {
                    Id = _nextNodeID,
                    TileX = lastVectorNode.Section.TileX,
                    TileZ = lastVectorNode.Section.TileZ,
                    X = lastVectorNode.Section.X,
                    Y = lastVectorNode.Section.Y,
                    Z = lastVectorNode.Section.Z,
                    AX = 0,
                    AY = lastVectorNode.Section.AY,
                    AZ = 0
                };
                lastEndNode.Pins.Add(new TrPin(lastVectorNode.Id, 0));  // Input from last vector node
                allNodes.Add(lastEndNode);
            }
            
            // Return only the vector nodes (the TrEndNodes will be handled separately in Program.cs)
            return _nodes;
        }

        // New method to get all nodes (vector + end nodes) for writing
        public List<object> BuildAllNodes()
        {
            // Call Build() first to set up pins
            Build();
            
            var allNodes = new List<object>();
            
            // Add first end node (ID 1)
            if (_nodes.Count > 0)
            {
                var firstVectorNode = _nodes[0];
                var firstEndNode = new TrEndNode
                {
                    Id = 1,
                    TileX = firstVectorNode.Section.TileX,
                    TileZ = firstVectorNode.Section.TileZ,
                    X = firstVectorNode.Section.X,
                    Y = firstVectorNode.Section.Y,
                    Z = firstVectorNode.Section.Z,
                    AX = 0,
                    AY = firstVectorNode.Section.AY,
                    AZ = 0
                };
                firstEndNode.Pins.Add(new TrPin(2, 1));  // Points to first vector node (now ID 2)
                allNodes.Add(firstEndNode);
            }
            
            // Add all vector nodes with renumbered IDs (starting from 2)
            for (int i = 0; i < _nodes.Count; i++)
            {
                TrackNode node = _nodes[i];
                int newId = i + 2;  // Renumber to start from 2
                node.Id = newId;
                
                // Clear old pins and add new ones with corrected IDs
                node.Pins.Clear();
                
                // Add input pin (from previous node or from end node)
                if (i == 0)
                {
                    node.Pins.Add(new TrPin(1, 0));  // First vector node connects to end node (ID 1)
                }
                else
                {
                    node.Pins.Add(new TrPin(_nodes[i - 1].Id, 0));
                }
                
                // Add output pin (to next vector node or to end node)
                if (i < _nodes.Count - 1)
                {
                    node.Pins.Add(new TrPin(_nodes[i + 1].Id + 1, 1));  // +1 because _nodes[i+1] still has old ID
                }
                else
                {
                    node.Pins.Add(new TrPin(_nodes.Count + 2, 1));  // Last vector node connects to final end node
                }
                
                allNodes.Add(node);
            }
            
            // Add last end node
            if (_nodes.Count > 0)
            {
                var lastVectorNode = _nodes[_nodes.Count - 1];
                var lastEndNode = new TrEndNode
                {
                    Id = _nodes.Count + 2,
                    TileX = lastVectorNode.Section.TileX,
                    TileZ = lastVectorNode.Section.TileZ,
                    X = lastVectorNode.Section.X,
                    Y = lastVectorNode.Section.Y,
                    Z = lastVectorNode.Section.Z,
                    AX = 0,
                    AY = lastVectorNode.Section.AY,
                    AZ = 0
                };
                lastEndNode.Pins.Add(new TrPin(lastVectorNode.Id, 0));  // Points to last vector node
                allNodes.Add(lastEndNode);
            }
            
            return allNodes;
        }
    }
}