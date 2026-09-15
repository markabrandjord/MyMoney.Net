using System;
using Microsoft.Data.SqlClient;

namespace Walkabout.Data
{
    public static class SaBootstrapConnection
    {
        private const string SaAccountName = "sa";

        /// <summary>
        /// Connects as 'sa'. Checks the credentials file for a previously
        /// saved 'sa' password first and uses it without prompting if it
        /// still works; otherwise prompts interactively (Retry/Cancel on
        /// failure per RetryLoop) and, on success, saves the password to
        /// the credentials file so later runs on this machine don't
        /// re-prompt. See the spec's "sa Handling" section for why this
        /// account is persisted here, unlike the rest of this codebase's
        /// general practice for admin credentials.
        /// </summary>
        public static bool TryConnect(string server, out string password)
        {
            var credentialStore = new DataEngineCredentialStore(DataEngineCredentialStore.GetDefaultPath());
            var stored = credentialStore.Load();

            if (stored.TryGetValue(SaAccountName, out DataEngineCredential saved) && TryOpenConnection(server, saved.Password))
            {
                password = saved.Password;
                return true;
            }

            string capturedPassword = null;
            bool result = RetryLoop.Run(
                tryAction: () =>
                {
                    Console.Write("Enter 'sa' password: ");
                    capturedPassword = ReadPasswordFromConsole();
                    return TryOpenConnection(server, capturedPassword);
                },
                promptOnFailure: () =>
                {
                    Console.Write("Retry connecting as 'sa'? [R]etry / [C]ancel: ");
                    string input = Console.ReadLine();
                    return string.Equals(input?.Trim(), "R", StringComparison.OrdinalIgnoreCase)
                        ? RetryLoop.PromptResult.Retry
                        : RetryLoop.PromptResult.Cancel;
                });

            if (result)
            {
                stored[SaAccountName] = new DataEngineCredential { UserId = SaAccountName, Password = capturedPassword };
                credentialStore.Save(stored);
            }

            password = result ? capturedPassword : null;
            return result;
        }

        private static bool TryOpenConnection(string server, string password)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                UserID = SaAccountName,
                Password = password,
                InitialCatalog = "master",
                ConnectTimeout = 10
            };

            try
            {
                using (var connection = new SqlConnection(builder.ConnectionString))
                {
                    connection.Open();
                }
                return true;
            }
            catch (SqlException ex)
            {
                Console.WriteLine($"Could not connect as 'sa': {ex.Message}");
                Console.WriteLine("Verify the 'sa' account is enabled on the server and the password is correct.");
                return false;
            }
        }

        private static string ReadPasswordFromConsole()
        {
            var input = new System.Text.StringBuilder();
            ConsoleKeyInfo key;
            while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
            {
                if (key.Key == ConsoleKey.Backspace && input.Length > 0)
                {
                    input.Length--;
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    input.Append(key.KeyChar);
                }
            }
            Console.WriteLine();
            return input.ToString();
        }
    }
}
