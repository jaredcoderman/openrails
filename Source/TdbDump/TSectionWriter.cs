using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Orts.Formats.Msts;
using Orts.Parsers.Msts;

namespace TdbDump
{
    class TSectionWriter
    {

        public static void UpdateTSectionDat(
            STFWriter writer,
            TrackPrimitive[] primitives
            ) 
        {   
            writer.WriteBlockStart("TrackSections", primitives.Length);
            foreach (TrackPrimitive primitive in primitives) {
                WriteTrackSection(writer, primitive);
            }
            writer.WriteBlockEnd();

            // Junction nodes reference a TrackShape for MainRoute / clearance.
            // Provide a minimal shape 0 so OR doesn't warn and default-route works.
            writer.WriteBlockStart("TrackShapes", 1);
            writer.WriteBlockStart("TrackShape", 0);
            writer.WriteProperty("FileName", "null.s");
            writer.WriteProperty("NumPaths", 2);
            writer.WriteProperty("MainRoute", 0);
            writer.WriteProperty("ClearanceDist", 0);
            writer.WriteBlockEnd();
            writer.WriteBlockEnd();
        }

        public static void WriteTrackSection(
            STFWriter writer,
            TrackPrimitive primitive
            )
        {
            writer.WriteBlockStart("TrackSection");
            writer.WriteNoLabel(string.Format("SectionCurve ( {0} ) {1} {2} {3}",
             primitive.IsCurve ? 1 : 0,
             primitive.SectionIndex, 
             primitive.SignedAngle, 
             primitive.Radius));
            writer.WriteBlockEnd();
        }
    }
}
