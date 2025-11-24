using Dapper;
using System.Data;

namespace Winner.D.Sql.Builder
{
    public class UpdateBuilder<T>
    {
        private readonly T _entity;
        private readonly string _tableName;
        private readonly string _keyColumn;
        public DynamicParameters Params { get; } = new();

        public UpdateBuilder(T entity, string keyColumn = "Id", string tableName = null)
        {
            _entity = entity;
            _keyColumn = keyColumn;
            _tableName = tableName ?? typeof(T).GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.TableAttribute), false)
                             .Cast<System.ComponentModel.DataAnnotations.Schema.TableAttribute>()
                             .FirstOrDefault()?.Name ?? typeof(T).Name;
        }

        public string BuildSql()
        {
            var props = typeof(T).GetProperties().Where(p => p.Name != _keyColumn).ToList();
            var sets = string.Join(", ", props.Select(p => {
                var name = "@" + p.Name;
                Params.Add(name, p.GetValue(_entity));
                return $"{p.Name}={name}";
            }));
            var keyVal = typeof(T).GetProperty(_keyColumn).GetValue(_entity);
            Params.Add("@key", keyVal);
            return $"UPDATE {_tableName} SET {sets} WHERE {_keyColumn}=@key";
        }

        public async Task<int> ExecuteAsync(IDbConnection conn, IDbTransaction tran = null)
        {
            return await conn.ExecuteAsync(BuildSql(), Params, tran);
        }
    }
}
