using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Walkabout.StockQuotes;
using Walkabout.Data;
using Walkabout.Utilities;

namespace Walkabout.Importers
{
    class CsvImportController
    {
        private readonly IImportProgressReporter progressReporter;
        private readonly IBusinessLayerUiCallback uiCallback;
        private MyMoney myMoney;
        private string databaseDir;
        private StockQuoteCache cache;

        public CsvImportController(IImportProgressReporter progressReporter, IBusinessLayerUiCallback uiCallback, MyMoney money, string databaseDir, StockQuoteCache cache)
        {
            this.progressReporter = progressReporter;
            this.uiCallback = uiCallback;
            this.myMoney = money;
            this.databaseDir = databaseDir;
            this.cache = cache;
        }

        public async Task<int> ImportCsv(string fileName)
        {
            int total = 0;
            try
            {
                DownloadData last = null;
                var entries = new ThreadSafeObservableCollection<DownloadData>();
                this.progressReporter.SetEntries(entries);

                var csv = CsvDocument.Load(fileName);
                if (csv.Headers.Contains("Account Number"))
                {
                    CsvMap map = null;
                    var grouped = CsvTransactionImporter.GroupCsvByAccount(this.myMoney, csv, this.uiCallback);
                    foreach (var key in grouped.Keys)
                    {
                        var data = new DownloadData(null, key);
                        entries.Add(data);
                        var doc = grouped[key];
                        var result = await this.ImportCsvForAccount(doc, key, data, map);
                        var count = result.Item1;
                        if (map == null)
                        {
                            map = result.Item2;
                        }
                        if (count > 0)
                        {
                            data.Message = $"Downloaded {count} new transactions";
                            await this.myMoney.Rebalance(key);
                            last = data;
                        }
                        data.Success = true;
                        total += count;
                    }
                }
                else
                {
                    string prompt = "Please select Account to import the CSV transactions into";
                    var acct = this.uiCallback?.PickAccount(this.myMoney, null, prompt);
                    var data = new DownloadData(null, acct);
                    entries.Add(data);
                    var result = await this.ImportCsvForAccount(csv, acct, data);
                    var count = result.Item1;
                    if (count > 0 && acct != null)
                    {
                        data.Message = $"Downloaded {count} new transactions";
                        last = data;
                        await this.myMoney.Rebalance(acct);
                    }
                    total += count;
                }

                if (last != null)
                {
                    this.progressReporter.SelectEntry(last);
                }

            }
            catch (UserCanceledException)
            {
            }
            catch (Exception ex)
            {
                // this.log.Error("Import Error", ex);
                this.uiCallback?.ShowError(ex.Message, "Import Error");
            }
            return total;
        }

        private async Task<Tuple<int, CsvMap>> ImportCsvForAccount(CsvDocument csv, Account acct, DownloadData data, CsvMap defaultMap = null)
        {
            int count = 0;
            CsvMap map = null;
            // load existing csv map if we have one.
            map = this.LoadMap(acct, defaultMap);
            var fields = acct.Type == AccountType.Brokerage || acct.Type == AccountType.Retirement ?
                CsvTransactionImporter.BrokerageAccountFields :
                CsvTransactionImporter.BankAccountFields;

            var importer = new CsvTransactionImporter(this.myMoney, acct, map, data, fields, this.cache, this.uiCallback);
            count = importer.Import(csv);
            await importer.Commit();
            map.Save();
            return new Tuple<int, CsvMap>(count, map);
        }

        private CsvMap LoadMap(Account a, CsvMap defaultMap)
        {
            CsvMap map = null;
            if (!string.IsNullOrEmpty(this.databaseDir))
            {
                var dir = Path.Combine(this.databaseDir, "CsvMaps");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var filename = Path.Combine(dir, a.Id + ".xml");
                map = CsvMap.Load(filename);
            }
            else
            {
                map = new CsvMap();
            }
            if (defaultMap != null && map.Fields == null)
            {
                map.CopyFrom(defaultMap);
            }
            return map;
        }

    }
}
