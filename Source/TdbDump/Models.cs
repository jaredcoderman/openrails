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
        public float Y { get; set; } = TerrainStamper.FlatTerrainY;
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

    /// <summary>
    /// 3-way switch topology. Pins are ordered as OR expects: 1 in + 2 out
    /// (trpins header "1 2"), stem then main then diverging.
    /// </summary>
    public class TrJunctionNode
    {
        public int Id { get; set; }
        public uint ShapeIndex { get; set; } = 0;
        public int TileX { get; set; }
        public int TileZ { get; set; }
        public float X { get; set; }
        /// <summary>Matches stamped flat TILES elevation (see TerrainStamper.FlatTerrainY).</summary>
        public float Y { get; set; } = TerrainStamper.FlatTerrainY;
        public float Z { get; set; }
        public float AX { get; set; } = 0;
        public float AY { get; set; } = 0;
        public float AZ { get; set; } = 0;
        public List<TrPin> Pins { get; private set; }

        public TrJunctionNode()
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
        /// <summary>Matches stamped flat TILES elevation (see TerrainStamper.FlatTerrainY).</summary>
        public float Y { get; set; } = TerrainStamper.FlatTerrainY;
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

    /// <summary>
    /// Multi-feature network export from extract_bbox_network.py.
    /// </summary>
    public class NetworkLocalFile
    {
        public NetworkCrs Crs { get; set; }
        public List<NetworkFeature> Features { get; set; } = new List<NetworkFeature>();
    }

    public class NetworkCrs
    {
        public int Epsg { get; set; }
        public bool FlipX { get; set; }
    }

    public class NetworkFeature
    {
        public int ObjectId { get; set; }
        public string Error { get; set; }
        public NetworkPoint Start { get; set; }
        public NetworkPoint End { get; set; }
        public List<TrackPrimitive> Primitives { get; set; } = new List<TrackPrimitive>();

        [Newtonsoft.Json.JsonProperty("points_local")]
        public List<List<float>> PointsLocal { get; set; }
    }

    public class NetworkPoint
    {
        public float X { get; set; }
        public float Z { get; set; }
        public float Ay { get; set; }
    }

    public class FeatureChain
    {
        public int ObjectId { get; set; }
        public List<TrackNode> Sections { get; set; } = new List<TrackNode>();

        /// <summary>True polyline endpoints from the network JSON (pre-reconstruction).</summary>
        public float GeoStartX { get; set; }
        public float GeoStartZ { get; set; }
        public float GeoEndX { get; set; }
        public float GeoEndZ { get; set; }
        /// <summary>Travel heading at the geo start / into the geo end.</summary>
        public float GeoStartAy { get; set; }
        public float GeoEndAy { get; set; }

        public float StartX { get; set; }
        public float StartZ { get; set; }
        public float StartAy { get; set; }
        public float EndX { get; set; }
        public float EndZ { get; set; }
        public float EndAy { get; set; }

        /// <summary>Assigned TDB vector-node id during BuildAllNodes.</summary>
        public int VectorNodeId { get; set; }
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

        /// <summary>
        /// Absolute start pose in the shared local meter frame, when present.
        /// </summary>
        public NetworkPoint Start { get; set; }
        public float SignedAngle
        {
            get
            {
                // Straights store length in this tsection field; only curves
                // use the Open Rails right-hand angle sign convention.
                if (!IsCurve)
                    return Length;

                return Clockwise ? Angle : -Angle;
            }
        }

        public float LocalEndX
        {
            get
            {
                if (!IsCurve)
                    return 0;

                // Match OR right-hand convention: clockwise curves displace
                // toward +X in the section-local frame when heading is +Z.
                float sign = Clockwise ? 1f : -1f;
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
        public float Y { get; set; } = TerrainStamper.FlatTerrainY;
        public float Z { get; set; }
        public float Qx { get; set; }
        public float Qy { get; set; }
        public float Qz { get; set; }
        public float Qw { get; set; }
        public uint VdbId { get; set; }
        public List<TrackPrimitive> TrackSections { get; set; } = new List<TrackPrimitive>();
        public int TileX { get; set; }
        public int TileZ { get; set; }

        /// <summary>
        /// One DynTrack per TDB TrVectorSection, same tile / UiD / SectionIndex
        /// so OR's WFName+UiD lookup finds the mesh for that section.
        /// </summary>
        public static List<DynamicTrack> MakeDynamicTrackObjects(
            IReadOnlyList<FeatureChain> chains,
            IReadOnlyCollection<TrackPrimitive> primitives)
        {
            var primitiveLookup = new Dictionary<uint, TrackPrimitive>();
            foreach (var primitive in primitives)
                primitiveLookup[primitive.SectionIndex] = primitive;

            var dynamicTracks = new List<DynamicTrack>();

            // One DynTrack per section at that section's reconstructed start.
            // Packing multiple sections into one DynTrack makes OR chain them
            // with its own math, which disagrees with our poses.
            foreach (var chain in chains)
            {
                if (chain.Sections == null)
                    continue;

                foreach (var sectionNode in chain.Sections)
                {
                    var nodeSection = sectionNode.Section;
                    if (nodeSection == null)
                        continue;

                    if (!primitiveLookup.TryGetValue(nodeSection.SectionIndex, out var primitive))
                    {
                        throw new InvalidOperationException(
                            "TDB section index " + nodeSection.SectionIndex
                            + " has no TrackPrimitive for DynTrack (oid "
                            + chain.ObjectId + ").");
                    }

                    var track = new DynamicTrack
                    {
                        // Same tile-local X/Z as the TDB TrVectorSection. WorldWriter
                        // writes them as Position; OR/TSRE only flip Z (and Qz) on load.
                        X = nodeSection.X,
                        Y = nodeSection.Y,
                        Z = nodeSection.Z,
                        UiD = (uint)nodeSection.WorldFileUiD,
                        SectionIdx = nodeSection.SectionIndex,
                        TileX = nodeSection.TileX,
                        TileZ = nodeSection.TileZ,
                        VdbId = 0,
                        CollideFlags = 0,
                        StaticFlags = 0,
                        Elevation = 0,
                    };

                    // Same MSTS SetAngles(heading) convention as TrackObj. Do not
                    // add π here — OR maps MSTS→XNA with Z flips so DynTrack
                    // Forward (−Z_xna) already matches TDB travel (+Z_msts), and
                    // TSRE applies its own π when building the GL matrix.
                    ConvertEulerToQuaternion(
                        nodeSection.AY,
                        nodeSection.AX,
                        out float qx,
                        out float qy,
                        out float qz,
                        out float qw);
                    track.Qx = qx;
                    track.Qy = qy;
                    track.Qz = qz;
                    track.Qw = qw;

                    track.TrackSections.Add(new TrackPrimitive
                    {
                        SectionIndex = primitive.SectionIndex,
                        Type = primitive.Type,
                        Length = primitive.Length,
                        Radius = primitive.Radius,
                        Angle = primitive.Angle,
                        Clockwise = primitive.Clockwise,
                        param1 = primitive.param1,
                        param2 = primitive.param2,
                    });

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
                            param2 = 0,
                        });
                    }

                    dynamicTracks.Add(track);
                }
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
