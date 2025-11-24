using System.Data;

namespace Winner.D.Sql.Builder.Helpers
{
    public static class TransactionHelper
    {
        public static async Task WithTransactionAsync(IDbConnection conn, Func<IDbTransaction, Task> action)
        {
            bool shouldCloseConnection = conn.State != ConnectionState.Open;
            if (shouldCloseConnection)
            {
                conn.Open();
            }

            using var tran = conn.BeginTransaction();

            try
            {
                await action(tran);
                tran.Commit();
            }
            catch
            {
                tran.Rollback();
                throw;
            }
            finally
            {
                if (shouldCloseConnection && conn.State == ConnectionState.Open)
                {
                    conn.Close();
                }
            }
        }
    }
}
