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
            return _nodes;
        }

        public List<object> BuildAllNodes()
        {
            var allNodes = new List<object>();
            if (_nodes.Count == 0)
                return allNodes;

            // Each dynamic-track section must reference its matching object in
            // the world file. These UiDs are independent of TDB node IDs.
            for (int i = 0; i < _nodes.Count; i++)
            {
                var section = _nodes[i].Section;
                section.WFNameX = section.TileX.ToString();
                section.WFNameZ = section.TileZ.ToString();
                section.WorldFileUiD = i + 1;
            }

            // MapViewer only handles single-section vector nodes when their
            // linked node has a UiD. Vector nodes normally do not, so retain
            // the whole continuous route in one multi-section vector node.
            const int startEndId = 1;
            const int vectorId = 2;
            const int finalEndId = 3;

            var first = _nodes[0].Section;
            var startEnd = new TrEndNode
            {
                Id = startEndId,
                TileX = first.TileX,
                TileZ = first.TileZ,
                X = first.X,
                Y = first.Y,
                Z = first.Z,
                AY = first.AY,
            };
            startEnd.Pins.Add(new TrPin(vectorId, 1));
            allNodes.Add(startEnd);

            var vector = new TrackNode
            {
                Id = vectorId,
                Section = first,
                Sections = new List<TrVectorSection>(_nodes.ConvertAll(node => node.Section)),
            };
            vector.Pins.Add(new TrPin(startEndId, 1));
            vector.Pins.Add(new TrPin(finalEndId, 1));
            allNodes.Add(vector);

            int finalTileXOffset = (int)Math.Floor((_x + 1024f) / 2048f);
            int finalTileZOffset = (int)Math.Floor((_z + 1024f) / 2048f);
            var end = new TrEndNode
            {
                Id = finalEndId,
                TileX = _tileX + finalTileXOffset,
                TileZ = _tileZ + finalTileZOffset,
                X = _x - finalTileXOffset * 2048f,
                Y = first.Y,
                Z = _z - finalTileZOffset * 2048f,
                AY = _ay,
            };
            end.Pins.Add(new TrPin(vectorId, 0));
            allNodes.Add(end);

            return allNodes;
        }
    }
}