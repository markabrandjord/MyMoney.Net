using Microsoft.Data.SqlClient;

namespace Walkabout.Data
{
    /// <summary>
    /// A SqlServerDatabase variant that performs Payees CRUD exclusively
    /// through stored procedures, matching the grants given to the
    /// MyMoneyUser login (see Database/SqlScripts/Access/Payees_AccessProcs.sql).
    /// Other tables are not yet overridden -- see the "Known Limitation"
    /// section of the plan that introduced this class.
    /// </summary>
    public class SqlServerStoredProcDatabase : SqlServerDatabase
    {
        /// <summary>
        /// Sole reason this exists: tests need to point at an ad hoc dev
        /// connection string without going through DataEngineConfig/
        /// DataEngineCredentialStore. Production callers (Task 10) leave
        /// this null and rely on the inherited Server/DatabasePath/UserId/
        /// Password properties instead.
        /// </summary>
        public string ConnectionStringOverride { get; set; }

        protected override string GetConnectionString(bool includeDatabase)
        {
            if (!string.IsNullOrEmpty(this.ConnectionStringOverride))
            {
                return this.ConnectionStringOverride;
            }
            return base.GetConnectionString(includeDatabase);
        }

        public override void ReadPayees(Payees payees, MyMoney money)
        {
            payees.Clear();
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.Payees_SelectAll", connection) { CommandType = System.Data.CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    payees.BeginUpdate(false);
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        Payee p = payees.AddPayee(id);
                        p.Name = reader.IsDBNull(1) ? null : reader.GetString(1);
                        p.OnUpdated();
                    }
                    payees.EndUpdate();
                }
            }
            payees.FireChangeEvent(payees, payees, null, ChangeType.Reloaded);
        }

        public override void UpdatePayees(Payees payees)
        {
            if (payees.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (Payee p in payees)
                {
                    if (p.IsChanged)
                    {
                        ExecutePayeeProc(connection, "dbo.Payees_Update", p);
                    }
                    else if (p.IsInserted)
                    {
                        ExecutePayeeProc(connection, "dbo.Payees_Insert", p);
                    }
                    else if (p.IsDeleted)
                    {
                        using (var command = new SqlCommand("dbo.Payees_Delete", connection) { CommandType = System.Data.CommandType.StoredProcedure })
                        {
                            command.Parameters.AddWithValue("@Id", p.Id);
                            command.ExecuteNonQuery();
                        }
                    }
                }
            }

            foreach (Payee p in payees)
            {
                if (!p.IsDeleted)
                {
                    p.OnUpdated();
                }
            }
            payees.RemoveDeleted();
        }

        private static void ExecutePayeeProc(SqlConnection connection, string procName, Payee p)
        {
            using (var command = new SqlCommand(procName, connection) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                command.Parameters.AddWithValue("@Id", p.Id);
                command.Parameters.AddWithValue("@Name", (object)p.Name ?? System.DBNull.Value);
                command.ExecuteNonQuery();
            }
        }
    }
}
