// COPYRIGHT 2025 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

using Orts.Parsers.Msts;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace Tests.Orts.Parsers.Msts
{
    public class STFWriterTests : IDisposable
    {
        private string tempFile;

        public STFWriterTests()
        {
            // Create a temporary file path for testing
            tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.stf");
        }

        public void Dispose()
        {
            // Clean up temporary file after each test
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public void Constructor_CreatesFileWithHeader()
        {
            // Arrange & Act
            using (var writer = new STFWriter(tempFile))
            {
            }

            // Assert
            Assert.True(File.Exists(tempFile));
            string content = File.ReadAllText(tempFile);
            Assert.StartsWith("SIMISA@@@@@@@@@@JINX0T0t______", content);
        }

        [Fact]
        public void WriteBlockStart_WritesLabelAndOpeningParen()
        {
            // Arrange
            using (var writer = new STFWriter(tempFile))
            {
                // Act
                writer.WriteBlockStart("TestBlock");
            }

            // Assert
            string content = File.ReadAllText(tempFile);
            Assert.Contains("TestBlock (", content);
        }

        [Fact]
        public void WriteBlockEnd_WritesClosingParen()
        {
            // Arrange
            using (var writer = new STFWriter(tempFile))
            {
                // Act
                writer.WriteBlockStart("TestBlock");
                writer.WriteBlockEnd();
            }

            // Assert
            string content = File.ReadAllText(tempFile);
            Assert.Contains("TestBlock (", content);
            Assert.Contains(")", content);
        }

        [Fact]
        public void WriteProperty_WritesPropertyValue()
        {
            // Arrange
            using (var writer = new STFWriter(tempFile))
            {
                // Act
                writer.WriteProperty("PropertyName", "PropertyValue");
            }

            // Assert
            string content = File.ReadAllText(tempFile);
            Assert.Contains("PropertyName PropertyValue", content);
        }

        [Fact]
        public void NestedBlocks_IndentsProperly()
        {
            // Arrange
            using (var writer = new STFWriter(tempFile))
            {
                // Act
                writer.WriteBlockStart("Outer");
                writer.WriteBlockStart("Inner");
                writer.WriteBlockEnd();
                writer.WriteBlockEnd();
            }

            // Assert
            string content = File.ReadAllText(tempFile);
            var lines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            
            // Find the lines containing Outer and Inner
            int outerIndex = -1;
            int innerIndex = -1;
            
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("Outer"))
                    outerIndex = i;
                if (lines[i].Contains("Inner"))
                    innerIndex = i;
            }

            Assert.True(outerIndex >= 0, "Outer block not found");
            Assert.True(innerIndex >= 0, "Inner block not found");
            
            // Inner block should be indented more than outer block
            Assert.True(lines[innerIndex].Length > lines[outerIndex].Length || 
                       lines[innerIndex].StartsWith("\t"), 
                       "Inner block should be indented");
        }
    }
}
