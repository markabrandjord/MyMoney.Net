using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Xml;
using System.Xml.Serialization;
using Walkabout.Data;
using Walkabout.Dialogs;
using Walkabout.StockQuotes;

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
            var (data, quotes) = SampleDataLoader.LoadEmbeddedSampleData(System.Reflection.Assembly.GetExecutingAssembly(), temp);

            string extractedSampleDataPath = Path.Combine(temp, "SampleData.xml");
            SampleDatabaseOptions options = new SampleDatabaseOptions();
            options.Owner = Application.Current.MainWindow;
            options.SampleData = extractedSampleDataPath;
            if (options.ShowDialog() == false)
            {
                return;
            }

            if (options.SampleData != extractedSampleDataPath)
            {
                // The dialog's "browse" button lets the user point at a custom
                // template file instead of the embedded one -- reload from there.
                XmlSerializer serializer = new XmlSerializer(typeof(SampleData));
                using (XmlReader reader = XmlReader.Create(options.SampleData))
                {
                    data = (SampleData)serializer.Deserialize(reader);
                }
            }

            string quoteFolder = Path.Combine(temp, "StockQuotes");
            foreach (var file in Directory.GetFiles(quoteFolder))
            {
                var target = Path.Combine(this.stockQuotePath, Path.GetFileName(file));
                if (!File.Exists(target))
                {
                    File.Copy(file, target, true);
                }
            }

            foreach (var kvp in quotes)
            {
                this.manager.DownloadLog.AddHistory(kvp.Value);
            }

            new SampleDataGenerator(this.money, quotes).Create(data, options.Inflation, options.Years, options.Employer, options.PayCheck);
        }

        public void Export(string path)
        {
            new SampleDataGenerator(this.money, new Dictionary<string, StockQuoteHistory>()).Export(path);
        }
    }
}
