using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TdbDump
{
    public class TrackNode
    {
        public int Id { get; set; }

        public TrVectorSection Section { get; set; }

        public List<TrPin> Pins { get; private set; }

        public TrackNode()
        {
            Pins = new List<TrPin>();
        }
    }

    public class TrVectorSection
    {
        public uint SectionIndex { get; set; }
        public uint ShapeIndex { get; set; } = 0;
        public string WFNameX { get; set; } = "0";
        public string WFNameZ { get; set; } = "0";
        public int WorldFileUiD { get; set; } = 0;
        public int Flag1 { get; set; } = 0;
        public int Flag2 { get; set; } = 0;
        public int TileX { get; set; } = 0;
        public int TileZ { get; set; } = 0;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public float AX { get; set; } = 0;
        public float AY { get; set; } = 0;
        public float AZ { get; set; } = 0;
        public string ToTdbString()
        {
            return string.Format(
                "{0} {1} {2} {3} {4} {5} {6} 00 {7} {8} {9} {10} {11} {12} {13} {14}",
                SectionIndex,
                ShapeIndex,
                WFNameX,
                WFNameZ,
                WorldFileUiD,
                Flag1,
                Flag2,
                TileX,
                TileZ,
                X,
                Y,
                Z,
                AX,
                AY,
                AZ
            );
        }
    }

    public class TrPin
    {
        public int Node { get; set; }
        public int Pin { get; set; }

        public TrPin(int node, int pin)
        {
            Node = node;
            Pin = pin;
        }
    }
}
