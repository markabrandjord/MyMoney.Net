using System;
using System.IO;

namespace Walkabout.Data
{
    /// <summary>
    /// Pure CSV formatting helpers for Transaction/Investment/LoanPaymentAggregation rows.
    /// Extracted out of MyMoney.Data's CsvStore (an IDatabase implementation) in Task 6:
    /// Exporters.cs (moving into MyMoney.Business as part of the Importers migration) used
    /// these same static methods for its own general "export a list of objects to .csv"
    /// feature, but MyMoney.Business cannot reference MyMoney.Data (MyMoney.Data already
    /// references MyMoney.Business; the reverse would be circular - see
    /// IBusinessLayerUiCallback.cs). These methods have no dependency on CsvStore's own
    /// IDatabase/file-backed state, so they move here and CsvStore.Save (still in
    /// MyMoney.Data) now calls this class instead of hosting them itself.
    /// </summary>
    public static class CsvTransactionFormat
    {
        [Flags]
        public enum OptionalColumnFlags : int
        {
            None = 0,
            InvestmentInfo = 1,
            SalesTax = 2,
            Currency = 4,
        }

        public static void WriteTransactionHeader(StreamWriter writer, OptionalColumnFlags optionalColumns)
        {
            writer.Write("\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\"",
                "Account", "Date", "Payee", "Category", "Amount");
            if (optionalColumns.HasFlag(OptionalColumnFlags.InvestmentInfo))
            {
                writer.Write(",\"{0}\",\"{1}\",\"{2}\",\"{3}\"",
                    "Activity", "Symbol", "Units", "UnitPrice");
            }
            if (optionalColumns.HasFlag(OptionalColumnFlags.SalesTax))
            {
                writer.Write(",\"{0}\"", "SalesTax");
            }
            if (optionalColumns.HasFlag(OptionalColumnFlags.Currency))
            {
                writer.Write(",\"{0}\"", "Currency");
            }
            writer.WriteLine(",\"{0}\"", "Memo");
        }


        public static void WriteTransaction(StreamWriter writer, Transaction t, OptionalColumnFlags optionalColumns)
        {
            string payee = t.PayeeName;
            if (t.Transfer != null)
            {
                payee = Transaction.GetTransferCaption(t.Transfer.Transaction.Account, t.Amount > 0);
            }
            // we put every column inside double quotes because some locale's use comma as a decimal separator.
            writer.Write("\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\"",
                CsvSafeString(t.AccountName),
                CsvSafeString(t.Date.ToShortDateString()),
                CsvSafeString(payee),
                CsvSafeString(t.CategoryName),
                CsvSafeString(t.Amount.ToString("C2")));

            if (optionalColumns.HasFlag(OptionalColumnFlags.InvestmentInfo))
            {
                string tradeType = "";
                if (t.InvestmentType != InvestmentType.None)
                {
                    tradeType = t.InvestmentType.ToString();
                }
                writer.Write(",\"{0}\",\"{1}\",\"{2}\",\"{3}\"",
                    tradeType,
                    CsvSafeString(t.InvestmentSecuritySymbol),
                    CsvSafeString(t.InvestmentUnits.ToString()),
                    CsvSafeString(t.InvestmentUnitPrice.ToString("C2")));
            }
            if (optionalColumns.HasFlag(OptionalColumnFlags.SalesTax))
            {
                writer.Write(",\"{0}\"", CsvSafeString(t.SalesTax.ToString("C2")));
            }
            if (optionalColumns.HasFlag(OptionalColumnFlags.Currency))
            {
                writer.Write(",\"{0}\"", CsvSafeString(t.GetAccountCurrency().Symbol));
            }
            writer.WriteLine(",\"{0}\"", CsvSafeString(t.Memo));
        }

        public static void WriteInvestmentHeader(StreamWriter writer)
        {
            writer.WriteLine("\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\",\"{5}\",\"{6}\",\"{7}\",\"{8}\",\"{9}\"",
                "Account", "Date", "Payee", "Category", "Activity", "Symbol", "Units", "UnitPrice", "Amount", "Memo");
        }

        public static void WriteInvestment(StreamWriter writer, Investment i)
        {
            Transaction t = i.Transaction;
            string payee = t.PayeeName;
            if (t.Transfer != null)
            {
                payee = Transaction.GetTransferCaption(t.Transfer.Transaction.Account, t.Amount > 0);
            }
            string tradeType = "";
            if (t.InvestmentType != InvestmentType.None)
            {
                tradeType = t.InvestmentType.ToString();
            }

            writer.WriteLine("\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\",\"{5}\",\"{6}\",\"{7}\",\"{8}\",\"{9}\"",
                CsvSafeString(t.AccountName),
                CsvSafeString(t.Date.ToShortDateString()),
                CsvSafeString(payee),
                CsvSafeString(t.CategoryName),
                tradeType,
                CsvSafeString(t.InvestmentSecuritySymbol),
                CsvSafeString(t.InvestmentUnits.ToString()),
                CsvSafeString(t.InvestmentUnitPrice.ToString("C2")),
                CsvSafeString(t.Amount.ToString("C2")),
                CsvSafeString(t.Memo));
        }

        public static void WriteLoanPaymentHeader(StreamWriter writer)
        {
            writer.WriteLine("Date,Account,Payment,Percentage,Principal,Interest,Balance");
        }

        public static void WriteLoanPayment(StreamWriter writer, LoanPaymentAggregation l)
        {
            writer.WriteLine("\"{0}\",\"{1}\",\"{2}\",\"{3}%\",\"{4}\",\"{5}\",\"{6}\"",
                CsvSafeString(l.Date.ToShortDateString()),
                CsvSafeString(l.AccountName),
                CsvSafeString(l.Payment.ToString("C2")),
                CsvSafeString(l.Percentage.ToString("N3")),
                CsvSafeString(l.Principal.ToString("C2")),
                CsvSafeString(l.Interest.ToString("C2")),
                CsvSafeString(l.Balance.ToString("C2"))
                );
        }

        public static string CsvSafeString(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }
            // everything is going inside double quotes, so we have to protect any existing double quotes
            // which is done by replacing them with 2 quotes like this "".
            s = s.Replace("\"", "\"\"");
            return s;
        }
    }
}
