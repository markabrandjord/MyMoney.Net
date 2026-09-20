using System;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using NUnit.Framework;

namespace Walkabout.UITests.Basics
{
    [TestFixture]
    public class AttachmentsFlaUiTests
    {
        private (string ScratchPath, string RegisteredName, long AttachmentTransactionId) db;

        [SetUp]
        public void SetUp()
        {
            this.db = BasicsTestSetup.OpenFreshDatabaseWithAttachment("AttachmentsFlaUiTests Fixture");
        }

        [TearDown]
        public void TearDown()
        {
            BasicsTestSetup.CleanUpDatabaseWithAttachment(this.db.ScratchPath, this.db.RegisteredName);
        }

        [Test]
        public void DeletingAttachment_RemovesFileAndListEntry()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);
            OpenFixtureDatabase.SelectAccount(mainWindow, "Basics Checking");

            AutomationElement amcRow = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem).And(
                    cf.ByName("Transaction: AMC THEATRES 1234 on 1/5/2026 for -32.5"))),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(amcRow, Is.Not.Null, "Expected the AMC THEATRES 1234 transaction row.");
            amcRow.Patterns.SelectionItem.Pattern.Select();
            Wait.UntilInputIsProcessed();

            // The "A" (Attachment) column is TransactionsView.xaml's first column
            // (TransactionAttachmentColumn). Its read-only state (TransactionAttachmentIcon,
            // a Border) shows nothing at all when Transaction.HasAttachment is false - and that
            // flag only flips once AttachmentWatcher's background scan runs (Dispatcher.
            // BeginInvoke at Background priority - can be significantly delayed and, in this
            // environment, didn't visibly flip within 10s). That doesn't matter here: the
            // command this column's edit-mode Button invokes (CommandScanAttachment ->
            // AttachmentDialog.ScanAttachments -> Transaction setter -> LoadAttachments) does
            // its own synchronous Manager.GetAttachments(t) directory scan, independent of
            // HasAttachment - so the dialog finds the real file regardless of the icon's state.
            // Double-click to enter cell edit mode (same pattern as the Currencies Symbol combo
            // and the Splits Payment field - the interactive Button only exists in the edit
            // template); the double-click's own template swap makes the pre-edit cell handle
            // stale, so re-query the Button from the row, not from that handle.
            AutomationElement attachmentCell = Retry.WhileNull(
                () => amcRow.FindFirstDescendant(cf => cf.ByName("Item: Transaction: AMC THEATRES 1234 on 1/5/2026 for -32.5, Column Display Index: 0")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(attachmentCell, Is.Not.Null, "Attachment column cell not found.");
            attachmentCell.DoubleClick();
            Wait.UntilInputIsProcessed();

            AutomationElement scanButton = Retry.WhileNull(
                () => amcRow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button)),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(scanButton, Is.Not.Null, "Attachment column's edit-mode button never appeared after double-click.");
            scanButton.Patterns.Invoke.Pattern.Invoke();
            Wait.UntilInputIsProcessed();

            // AttachmentDialog is shown via dialog.Show() (a non-modal Window.Show(), not
            // ShowDialog()), so it will NOT appear in mainWindow.ModalWindows - it's a sibling
            // top-level window instead. Confirmed via screenshot that it genuinely renders on
            // screen (with the seeded attachment visible), yet BOTH
            // automation.GetDesktop().FindAllChildren() and the same call scoped by
            // ByProcessId(app.ProcessId) come back completely empty for it - a documented UIA
            // desktop-enumeration gap (FlaUI#57/#239, see the flaui-wpf-testing skill's
            // references/native-dialogs.md), not specific to native common dialogs. The
            // documented fallback - raw Win32 EnumWindows, bypassing UIA's desktop-children
            // enumeration entirely - finds it reliably instead.
            Window attachmentDialog = Retry.WhileNull(() =>
            {
                foreach (var w in Win32WindowFallback.FindTopLevelWindowsForProcess(BasicsAppSession.Automation, BasicsAppSession.App.ProcessId))
                {
                    if (w.Title == "Attachments")
                    {
                        return w;
                    }
                }
                return null;
            }, TimeSpan.FromSeconds(5)).Result;
            Assert.That(attachmentDialog, Is.Not.Null, "Attachments dialog did not appear.");

            AutomationElement attachmentImage = Retry.WhileNull(
                () => attachmentDialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Image)),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(attachmentImage, Is.Not.Null, "Expected the one seeded attachment's thumbnail (an Image control) in the dialog.");
            attachmentImage.Click();
            Wait.UntilInputIsProcessed();

            Keyboard.Type(VirtualKeyShort.DELETE);
            Wait.UntilInputIsProcessed();

            AutomationElement[] remaining = Retry.WhileNull(() =>
            {
                var images = attachmentDialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Image));
                return images.Length == 0 ? images : null;
            }, TimeSpan.FromSeconds(5)).Result ?? attachmentDialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Image));

            Assert.That(remaining, Is.Empty, "Expected the attachment list to be empty after deleting the only attachment.");

            string attachmentDirectory = Path.Combine(
                Path.GetDirectoryName(this.db.ScratchPath),
                Path.GetFileNameWithoutExtension(this.db.ScratchPath) + ".Attachments");
            string accountDirectory = Path.Combine(attachmentDirectory, "Basics Checking");
            string expectedFile = Path.Combine(accountDirectory, this.db.AttachmentTransactionId + ".txt");
            Assert.That(File.Exists(expectedFile), Is.False, "The underlying attachment file should no longer exist on disk.");
        }
    }
}
