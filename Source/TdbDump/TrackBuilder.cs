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
        private int _tileX = 0;
        private int _tileZ = 0;
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
            node.Section.TileX = (int)Math.Floor((double)(_x + 1024) / 2048);
            node.Section.TileZ = (int)Math.Floor((double)(_z + 1024) / 2048);
            node.Section.X = _x - node.Section.TileX * 2048f;
            node.Section.Z = _z - node.Section.TileZ * 2048f;
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
            node.Section.TileX = (int)Math.Floor((double)(_x + 1024) / 2048);
            node.Section.TileZ = (int)Math.Floor((double)(_z + 1024) / 2048);
            node.Section.X = _x - node.Section.TileX * 2048f;
            node.Section.Z = _z - node.Section.TileZ * 2048f;

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
    }
}