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
