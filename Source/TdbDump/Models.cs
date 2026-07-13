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

        public List<TrVectorSection> Sections { get; set; }

        public List<TrPin> Pins { get; private set; }

        public TrackNode()
        {
            Sections = new List<TrVectorSection>();
            Pins = new List<TrPin>();
        }
    }

    public class TrEndNode
    {
        public int Id { get; set; }
        public int TileX { get; set; }
        public int TileZ { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float AX { get; set; } = 0;
        public float AY { get; set; } = 0;
        public float AZ { get; set; } = 0;
        public List<TrPin> Pins { get; private set; }

        public TrEndNode()
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
        public float Y { get; set; } = 1000;
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

    public class PrimitiveFile
    {
        public List<TrackPrimitive> Segments { get; set; } = new List<TrackPrimitive>();
    }

    public class TrackPrimitive
    {
        public uint SectionIndex { get; set; }
        public string Type { get; set; } = "";
        public bool IsCurve => Type == "curve";
        public float Length { get; set; }
        public float Radius { get; set; }
        public float Angle { get; set; }
        public bool Clockwise { get; set; }
        public float param1 { get; set; }
        public float param2 { get; set; }
        public float SignedAngle
        {
            get
            {
                return Clockwise ? -Angle : Angle;
            }
        }

        public float LocalEndX
        {
            get
            {
                if (!IsCurve)
                    return 0;

                float sign = Clockwise ? -1f : 1f;
                return Radius * sign * (1f - (float)Math.Cos(Angle));
            }
        }

        public float LocalEndZ
        {
            get
            {
                if (!IsCurve)
                    return Length;

                return Radius * (float)Math.Sin(Angle);
            }
        }
    }

    public class DynamicTrack
    {
        public uint UiD { get; set; }
        public uint SectionIdx { get; set; }
        public uint Elevation { get; set; }
        public uint CollideFlags { get; set; }
        public uint StaticFlags { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Qx { get; set; }
        public float Qy { get; set; }
        public float Qz { get; set; }
        public float Qw { get; set; }
        public uint VdbId { get; set; }
        public List<TrackPrimitive> TrackSections { get; set; } = new List<TrackPrimitive>();
        public int TileX { get; set; }
        public int TileZ { get; set; }

        public static List<DynamicTrack> MakeDynamicTrackObjects(
            List<TrackNode> nodes,
            IReadOnlyCollection<TrackPrimitive> primitives)
        {
            var primitiveLookup = new Dictionary<uint, TrackPrimitive>();

            foreach (var primitive in primitives)
                primitiveLookup[primitive.SectionIndex] = primitive;

            var dynamicTracks = new List<DynamicTrack>();

            // Create one DynamicTrack per node
            for (int i = 0; i < nodes.Count; i++)
            {
                DynamicTrack track = new DynamicTrack();

                var nodeSection = nodes[i].Section;
                if (nodeSection == null)
                    continue;

                track.X = -nodeSection.X;
                track.Y = nodeSection.Y;
                track.Z = nodeSection.Z;

                float qx, qy, qz, qw;

                ConvertEulerToQuaternion(
                    nodeSection.AY,
                    nodeSection.AX,
                    out qx,
                    out qy,
                    out qz,
                    out qw);

                track.Qx = qx;
                track.Qy = qy;
                track.Qz = qz;
                track.Qw = qw;
                track.UiD = (uint)nodeSection.WorldFileUiD;
                track.SectionIdx = nodeSection.SectionIndex;
                track.TileX = nodeSection.TileX;
                track.TileZ = nodeSection.TileZ;
                track.VdbId = 0;
                track.CollideFlags = 0;
                track.StaticFlags = 0;
                track.Elevation = 0;

                // Add the actual primitive section
                if (primitiveLookup.TryGetValue(nodeSection.SectionIndex, out var primitive))
                {
                    track.TrackSections.Add(new TrackPrimitive
                    {
                        SectionIndex = primitive.SectionIndex,
                        Type = primitive.Type,
                        Length = primitive.Length,
                        Radius = primitive.Radius,
                        Angle = primitive.Angle,
                        Clockwise = primitive.Clockwise,
                        param1 = primitive.param1,
                        param2 = primitive.param2
                    });
                }

                // Pad with 4 empty sections (param1=0, param2=0)
                while (track.TrackSections.Count < 5)
                {
                    track.TrackSections.Add(new TrackPrimitive
                    {
                        SectionIndex = 0,
                        Type = "straight",
                        Length = 0,
                        Radius = 0,
                        Angle = 0,
                        Clockwise = false,
                        param1 = 0,
                        param2 = 0
                    });
                }

                dynamicTracks.Add(track);
            }

            return dynamicTracks;
        }

        private static void ConvertEulerToQuaternion(float heading, float bank,
            out float qx, out float qy, out float qz, out float qw)
        {
            float a1 = heading;
            float a2 = 0f;
            float a3 = bank;

            float C1 = (float)Math.Cos(a1);
            float S1 = (float)Math.Sin(a1);
            float C2 = (float)Math.Cos(a2);
            float S2 = (float)Math.Sin(a2);
            float C3 = (float)Math.Cos(a3);
            float S3 = (float)Math.Sin(a3);

            float w = (float)Math.Sqrt(1.0 + C1 * C2 + C1 * C3 - S1 * S2 * S3 + C2 * C3) / 2.0f;

            if (Math.Abs(w) < 0.000005)
            {
                qx = 0.0f;
                qy = -1.0f;
                qz = 0.0f;
                qw = 0.0f;
            }
            else
            {
                qx = (float)(-(C2 * S3 + C1 * S3 + S1 * S2 * C3) / (4.0 * w));
                qy = (float)(-(S1 * C2 + S1 * C3 + C1 * S2 * S3) / (4.0 * w));
                qz = (float)(-(-S1 * S3 + C1 * S2 * C3 + S2) / (4.0 * w));
                qw = w;
            }
        }
    }
}
