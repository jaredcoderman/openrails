using System;
using System.IO;
using System.Text;

namespace Orts.Parsers.Msts
{
    /// <summary>
    /// Writes STF (SIMISA Text Format) files used by MSTS/Open Rails.
    /// </summary>
    public class STFWriter : IDisposable
    {
        private StreamWriter writer;
        private int indentLevel = 0;
        private const string IndentString = "\t";

        /// <summary>
        /// Constructor - opens a file for writing.
        /// </summary>
        public STFWriter(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            writer = new StreamWriter(filePath, false, Encoding.Unicode);
            WriteHeader();
        }

        /// <summary>
        /// Write the SIMISA header that all STF files need.
        /// </summary>
        private void WriteHeader()
        {
            writer.WriteLine("SIMISA@@@@@@@@@@JINX0T0t______");
            writer.WriteLine();
        }

        /// <summary>
        /// Write an opening block with a label.
        /// </summary>
        public void WriteBlockStart(string label)
        {
            WriteLine($"{label} (");
            indentLevel++;
        }

        /// <summary>
        /// Write an opening block with a label and an integer parameter.
        /// Example: tracknodes ( 10000
        /// </summary>
        public void WriteBlockStart(string label, int parameter)
        {
            WriteLine($"{label} ( {parameter}");
            indentLevel++;
        }

        /// <summary>
        /// Write an opening block with a label and a string parameter.
        /// Example: tracknode ( "MyTrack"
        /// </summary>
        public void WriteBlockStart(string label, string parameter)
        {
            WriteLine($"{label} ( {parameter}");
            indentLevel++;
        }

        /// <summary>
        /// Write a closing block.
        /// </summary>
        public void WriteBlockEnd()
        {
            indentLevel--;
            WriteLine(")");
        }

        /// <summary>
        /// Write a line with proper indentation.
        /// </summary>
        private void WriteLine(string text)
        {
            string indent = new string('\t', indentLevel);
            writer.WriteLine(indent + text);
        }

        /// <summary>
        /// Write a key-value pair like: Name ( "value" )
        /// </summary>
        public void WriteProperty(string key, string value)
        {
            WriteLine($"{key} ( {value} )");
        }

        /// <summary>
        /// Write a number like: Index ( 42 )
        /// </summary>
        public void WriteProperty(string key, int value)
        {
            WriteLine($"{key} ( {value} )");
        }


        /// <summary>
        /// Write a number like: Index ( 42 )
        /// </summary>
        /// <param name="value"></param>
        public void WriteNoLabel(string value)
        {
            WriteLine(value);
        }

        /// <summary>
        /// Close and flush the file.
        /// </summary>
        public void Close()
        {
            writer.Close();
        }

        /// <summary>
        /// Dispose implementation for using statements.
        /// </summary>
        public void Dispose()
        {
            writer?.Dispose();
        }
    }
}
