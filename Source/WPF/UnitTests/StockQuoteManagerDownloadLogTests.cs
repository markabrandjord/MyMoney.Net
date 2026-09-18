using System;
using System.IO;
using NUnit.Framework;
using Walkabout.StockQuotes;

namespace Walkabout.Tests
{
    /// <summary>
    /// Covers DownloadLog's pure-logic members (GetInfo/OnQuoteAvailable/OnQuoteNotFound/
    /// Load/Save) - one of the four classes the final whole-branch review of the
    /// business-layer subsystem migration found had zero test coverage (Task 10).
    /// StockQuoteManager's own network-calling methods (UpdateQuotes, BeginUpdateStockSplits,
    /// BeginDownloadHistory, GetCachedHistory, TestApiKeyAsync) are explicitly out of scope per
    /// the task brief - no mocking seam exists over that HTTP boundary today, so they are not
    /// tested here.
    /// </summary>
    [TestFixture]
    public class StockQuoteManagerDownloadLogTests
    {
        [Test]
        public void GetInfo_UnknownSymbol_ReturnsNull()
        {
            var log = new DownloadLog();

            Assert.IsNull(log.GetInfo("MSFT"));
        }

        [Test]
        public void OnQuoteNotFound_RecordsInfoRetrievableByGetInfo()
        {
            var log = new DownloadLog();

            log.OnQuoteNotFound("BADSYM");

            DownloadInfo info = log.GetInfo("BADSYM");
            Assert.IsNotNull(info);
            Assert.AreEqual("BADSYM", info.Symbol);
            Assert.IsTrue(info.NotFound);
            Assert.AreEqual(DateTime.Today, info.Downloaded);
        }

        [Test]
        public void OnQuoteAvailable_SymbolNeverRecordedBefore_ReturnsFalse()
        {
            // Real behavior verified by reading OnQuoteAvailable's implementation: it calls
            // ConcurrentDictionary.TryUpdate(symbol, quote, existing), which - per
            // ConcurrentDictionary's documented contract - returns false whenever the key isn't
            // already present, regardless of the comparand. Nothing else ever adds an entry to
            // that dictionary, so a brand-new symbol can never succeed here. This looks like a
            // dead/no-op code path in production, but the task is to verify and document actual
            // behavior, not the behavior the brief assumed - so this test pins what really
            // happens rather than asserting the intended-but-unreachable "true" result.
            var log = new DownloadLog();
            var quote = new StockQuote { Symbol = "MSFT", Close = 100m, Date = DateTime.Today };

            bool result = log.OnQuoteAvailable(quote);

            Assert.IsFalse(result);
        }

        [TestCase(null)]
        [TestCase("")]
        public void Load_NullOrEmptyFolder_ReturnsNewEmptyLogAndReportsIsNew(string folder)
        {
            (DownloadLog log, bool isNew) = DownloadLog.Load(folder);

            Assert.IsTrue(isNew);
            Assert.IsNotNull(log);
            Assert.AreEqual(0, log.Downloaded.Count);
        }

        [Test]
        public void Save_Then_Load_RoundTripsDownloadedEntries()
        {
            string dir = Path.Combine(Path.GetTempPath(), "DownloadLogTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                (DownloadLog log, bool isNew) = DownloadLog.Load(dir);
                Assert.IsTrue(isNew, "no DownloadLog.xml exists yet in a brand new temp folder");

                log.OnQuoteNotFound("BADSYM");
                log.Save(dir);

                (DownloadLog reloaded, bool isNewReload) = DownloadLog.Load(dir);

                Assert.IsFalse(isNewReload);
                DownloadInfo info = reloaded.GetInfo("BADSYM");
                Assert.IsNotNull(info);
                Assert.IsTrue(info.NotFound);
                Assert.AreEqual(dir, reloaded.Folder);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Load_CorruptedFile_ReturnsNewEmptyLogInsteadOfThrowing()
        {
            string dir = Path.Combine(Path.GetTempPath(), "DownloadLogTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "DownloadLog.xml"), "this is not valid xml <<<");

                (DownloadLog log, bool isNew) = DownloadLog.Load(dir);

                Assert.IsTrue(isNew, "a corrupted log file should be treated as if it didn't exist");
                Assert.IsNotNull(log);
                Assert.AreEqual(0, log.Downloaded.Count);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
