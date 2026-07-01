using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Orts.Formats.Msts;
using Orts.Parsers.Msts;

namespace TdbDump
{
    class TDBWriter
    {
        public static void WriteTrackNode(
            STFWriter writer,
            TrackNode node)
        {
            writer.WriteBlockStart("tracknode", node.Id);

            writer.WriteBlockStart("trvectornode");

            writer.WriteBlockStart("trvectorsections", 1);

            writer.WriteNoLabel(node.Section.ToTdbString());

            writer.WriteBlockEnd();

            writer.WriteBlockEnd();

            writer.WriteBlockStart("trpins", "1 1");
            writer.WriteProperty("TrPin", "1 1");
            writer.WriteProperty("TrPin", "1 1");


            writer.WriteBlockEnd();
            writer.WriteBlockEnd();


        }
    }
}
