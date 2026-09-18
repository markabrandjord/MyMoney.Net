using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    public class CsvStore : IDatabase
    {
        private readonly string fileName;
        private readonly IEnumerable rows;

        public CsvStore(string fileName, IEnumerable rows)
        {
            this.fileName = fileName;
            this.rows = rows;
        }

        public IDataLayerUiCallback UiCallback { get; set; }

        public virtual bool SupportsUserLogin => false;

        public virtual string Server { get; set; }

        public virtual string DatabasePath { get { return this.fileName; } }

        public virtual string ConnectionString { get { return null; } }

        public virtual string BackupPath { get { return null; } } // todo

        public virtual DbFlavor DbFlavor { get { return Data.DbFlavor.Xml; } } // BugBug:

        public virtual string UserId { get; set; }

        public virtual string Password { get; set; }

        public virtual bool Exists
        {
            get
            {
                return File.Exists(this.fileName);
            }
        }

        public virtual void Create()
        {
        }

        public virtual void Disconnect()
        {

        }
        public virtual void Delete()
        {
            if (this.Exists)
            {
                File.Delete(this.fileName);
            }
        }

        public bool UpgradeRequired
        {
            get
            {
                return false;
            }
        }

        public void Upgrade()
        {
        }

        public MyMoney Load(IStatusService status)
        {
            throw new NotImplementedException();
        }

        public void SaveOne<T>(T root) where T : PersistentObject, IAggregateRoot
        {
            throw new NotImplementedException("CsvStore does not support SaveOne - it is a write-only export format.");
        }

        public void SaveTransfer(Transaction from, Transaction to)
        {
            throw new NotImplementedException("CsvStore does not support SaveTransfer - it is a write-only export format.");
        }

        public void SaveBatch(IEnumerable<PersistentObject> roots)
        {
            throw new NotImplementedException("CsvStore does not support SaveBatch - it is a write-only export format.");
        }

        public void Save(MyMoney money)
        {
            StreamWriter writer = null;
            try
            {
                writer = new StreamWriter(this.fileName);
                CsvTransactionFormat.WriteTransactionHeader(writer, CsvTransactionFormat.OptionalColumnFlags.None);

                if (this.rows != null)
                {
                    foreach (Transaction t in this.rows)
                    {
                        if (t != null)
                        {
                            CsvTransactionFormat.WriteTransaction(writer, t, CsvTransactionFormat.OptionalColumnFlags.None);
                        }
                    }
                }
            }
            finally
            {
                if (writer != null)
                {
                    writer.Close();
                    writer = null;
                }
            }
        }

        public virtual void Backup(string path)
        {
            this.UiCallback?.ShowWarning("XML Backup is not implemented", null);
        }

        public virtual string GetLog()
        {
            return "";
        }

        public virtual DataSet QueryDataSet(string cmd)
        {
            return new DataSet();
        }


        /// <summary>
        /// Import the Transactions & accounts in the given file and return the first account.
        /// </summary>
        /// <param name="file"></param>
        /// <param name="count"></param>
        /// <returns></returns>

        public static async Task<int> ImportCsv(MyMoney myMoney, Account acct, string file)
        {
            var uri = new Uri(file);
            var csvReader = new CsvReader(4096);
            await csvReader.OpenAsync(uri, Encoding.UTF8, null);
            csvReader.Delimiter = ',';
            int total = 0;
            while (csvReader.Read())
            {
                if (csvReader.FieldCount != 3)
                {
                    throw new NotSupportedException("Invalid CSV format expecting 3 columns [Date, Payee, Amount]");
                }

                var field1Date = csvReader[0];
                var field2Payee = csvReader[1];
                var field3Amount = csvReader[2];
                if (total == 0)
                {
                    if (field1Date != "Date" || field2Payee != "Payee" || field3Amount != "Amount")
                    {
                        throw new NotSupportedException("Invalid CSV format The fist row is expected to be the header [Date, Payee, Amount]");
                    }
                }
                else
                {
                    var dateTokens = field1Date.Split('-');

                    if (dateTokens.Length != 3)
                    {
                        throw new NotSupportedException("Invalid CSV format The Date needs to be specified in ISO8601 YYYY-MM-DD format : " + field1Date);
                    }

                    if (dateTokens[0].Length != 4)
                    {
                        throw new NotSupportedException("Invalid CSV format The Date Year must be 4 digits : " + field1Date);
                    }

                    Transaction t = myMoney.Transactions.NewTransaction(acct);

                    t.Id = -1;
                    t.Date = DateTime.Parse(field1Date);
                    t.Payee = t.Payee = myMoney.Payees.FindPayee(field2Payee, true);
                    t.Amount = decimal.Parse(field3Amount);

                    myMoney.Transactions.Add(t);
                }
                total++;
            }
            csvReader.Close();

            return total;
        }
    }
}
