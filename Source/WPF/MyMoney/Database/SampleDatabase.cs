using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Xml;
using System.Xml.Serialization;
using Walkabout.Data;
using Walkabout.Dialogs;
using Walkabout.StockQuotes;
using Walkabout.Utilities;

namespace Walkabout.Assistance
{
    /// <summary>
    /// WPF-facing half of sample-database generation: shows the options
    /// dialog, extracts the bundled sample-data and stock-quote-history
    /// resources, and hands the resolved inputs to
    /// Walkabout.Data.SampleDataGenerator (MyMoney.Business) for the actual
    /// account/transaction construction.
    /// </summary>
    public class SampleDatabase
    {
        private readonly MyMoney money;
        private readonly string stockQuotePath;
        private readonly StockQuoteManager manager;

        public SampleDatabase(MyMoney money, StockQuoteManager manager, string stockQuotePath)
        {
            this.manager = manager;
            this.money = money;
            this.stockQuotePath = stockQuotePath;
            if (string.IsNullOrEmpty(stockQuotePath))
            {
                throw new Exception("StockQuotePath cannot be empty. Have you created a database yet?");
            }
        }

        public void Create()
        {
            string temp = Path.Combine(Path.GetTempPath(), "MyMoney");
            Directory.CreateDirectory(temp);

            string path = Path.Combine(temp, "SampleData.xml");
            ProcessHelper.ExtractEmbeddedResourceAsFile("Walkabout.Database.SampleData.xml", path);

            SampleDatabaseOptions options = new SampleDatabaseOptions();
            options.Owner = Application.Current.MainWindow;
            options.SampleData = path;
            if (options.ShowDialog() == false)
            {
                return;
            }

            string zipPath = Path.Combine(temp, "SampleStockQuotes.zip");
            ProcessHelper.ExtractEmbeddedResourceAsFile("Walkabout.Database.SampleStockQuotes.zip", zipPath);

            string quoteFolder = Path.Combine(temp, "StockQuotes");
            if (Directory.Exists(quoteFolder))
            {
                Directory.Delete(quoteFolder, true);
            }
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, temp);
            foreach (var file in Directory.GetFiles(quoteFolder))
            {
                var target = Path.Combine(this.stockQuotePath, Path.GetFileName(file));
                if (!File.Exists(target))
                {
                    File.Copy(file, target, true);
                }
            }

            path = options.SampleData;
            SampleData data;
            XmlSerializer s = new XmlSerializer(typeof(SampleData));
            using (XmlReader reader = XmlReader.Create(path))
            {
                data = (SampleData)s.Deserialize(reader);
            }

            var quotes = new Dictionary<string, StockQuoteHistory>();
            foreach (SampleSecurity ss in data.Securities)
            {
                var history = StockQuoteHistory.Load(quoteFolder, ss.Symbol);
                if (history != null)
                {
                    quotes[ss.Symbol] = history;
                    this.manager.DownloadLog.AddHistory(history);
                }
            }

            new SampleDataGenerator(this.money, quotes).Create(data, options.Inflation, options.Years, options.Employer, options.PayCheck);
        }

        public void Export(string path)
        {
            new SampleDataGenerator(this.money, new Dictionary<string, StockQuoteHistory>()).Export(path);
        }
    }
}
