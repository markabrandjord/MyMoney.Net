using System;
using System.IO;

namespace Walkabout.Data
{
    public class Program
    {
        private static int Main(string[] args)
        {
            string server = args.Length > 0 ? args[0] : "localhost";
            string databaseName = args.Length > 1 ? args[1] : "MyMoney";

            // SqlScripts ships alongside the MyMoney assembly it's committed
            // under (Source/WPF/MyMoney/Database/SqlScripts); locate it
            // relative to this executable's own build output.
            string sqlScriptsRoot = Path.Combine(AppContext.BaseDirectory, "SqlScripts");
            if (!Directory.Exists(sqlScriptsRoot))
            {
                Console.WriteLine($"Could not find SqlScripts folder at {sqlScriptsRoot}.");
                return 1;
            }

            var runner = new BootstrapRunner(sqlScriptsRoot);
            bool success = runner.Run(server, databaseName);
            return success ? 0 : 1;
        }
    }
}
