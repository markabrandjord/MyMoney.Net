using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using Walkabout.StockQuotes;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    /// <summary>
    /// Extracts and deserializes the embedded sample-data resources
    /// (SampleData.xml, SampleStockQuotes.zip) without any WPF dependency.
    /// The resources are embedded in whichever assembly the caller passes
    /// in (MyMoney.csproj today) -- this class doesn't assume which
    /// assembly that is, so it works the same way from a headless test as
    /// from the WPF-hosted SampleDatabase wizard.
    /// </summary>
    public static class SampleDataLoader
    {
        public static (SampleData Data, Dictionary<string, StockQuoteHistory> Quotes) LoadEmbeddedSampleData(
            Assembly resourceAssembly, string tempDir)
        {
            Directory.CreateDirectory(tempDir);

            string xmlPath = Path.Combine(tempDir, "SampleData.xml");
            ProcessHelper.ExtractEmbeddedResourceAsFile(resourceAssembly, "Walkabout.Database.SampleData.xml", xmlPath);

            string zipPath = Path.Combine(tempDir, "SampleStockQuotes.zip");
            ProcessHelper.ExtractEmbeddedResourceAsFile(resourceAssembly, "Walkabout.Database.SampleStockQuotes.zip", zipPath);

            string quoteFolder = Path.Combine(tempDir, "StockQuotes");
            if (Directory.Exists(quoteFolder))
            {
                Directory.Delete(quoteFolder, true);
            }
            ZipFile.ExtractToDirectory(zipPath, tempDir);

            SampleData data;
            XmlSerializer serializer = new XmlSerializer(typeof(SampleData));
            using (XmlReader reader = XmlReader.Create(xmlPath))
            {
                data = (SampleData)serializer.Deserialize(reader);
            }

            Dictionary<string, StockQuoteHistory> quotes = new Dictionary<string, StockQuoteHistory>();
            foreach (SampleSecurity ss in data.Securities)
            {
                StockQuoteHistory history = StockQuoteHistory.Load(quoteFolder, ss.Symbol);
                if (history != null)
                {
                    quotes[ss.Symbol] = history;
                }
            }

            return (data, quotes);
        }
    }
}
