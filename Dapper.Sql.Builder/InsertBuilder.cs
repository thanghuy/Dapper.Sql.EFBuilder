using System.Data;

namespace Dapper.Sql.Builder
{
    public class InsertBuilder<T>
    {
        private readonly T _entity;
        private readonly string _tableName;
        public DynamicParameters Params { get; } = new();

        public InsertBuilder(T entity, string tableName = null)
        {
            _entity = entity;
            _tableName = tableName ?? typeof(T).GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.TableAttribute), false)
                             .Cast<System.ComponentModel.DataAnnotations.Schema.TableAttribute>()
                             .FirstOrDefault()?.Name ?? typeof(T).Name;
        }

        public string BuildSql()
        {
            var props = typeof(T).GetProperties().Where(p => p.CanRead).ToList();
            var cols = string.Join(", ", props.Select(p => p.Name));
            var vals = string.Join(", ", props.Select(p => {
                var name = "@" + p.Name;
                Params.Add(name, p.GetValue(_entity));
                return name;
            }));
            return $"INSERT INTO {_tableName} ({cols}) VALUES ({vals})";
        }

        public async Task<int> ExecuteAsync(IDbConnection conn, IDbTransaction tran = null)
        {
            return await conn.ExecuteAsync(BuildSql(), Params, tran);
        }
    }
}
