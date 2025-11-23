using System.Data;

namespace Dapper.Sql.Builder.Helpers
{
    public static class TransactionHelper
    {
        public static async Task WithTransactionAsync(IDbConnection conn, Func<IDbTransaction, Task> action)
        {
            if (conn.State != ConnectionState.Open) conn.Open();
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
        }
    }
}
