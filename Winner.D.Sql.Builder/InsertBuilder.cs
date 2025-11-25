using Dapper;
using System.Data;
using Winner.D.Sql.Builder.Helpers;

namespace Winner.D.Sql.Builder
{
    public class InsertBuilder<T, TableMap>
    {
        private readonly SqlBuilder _builder = new();
        private readonly T? _entity;
        private readonly List<T>? _entities;
        private readonly string _tableName;
        private long _paramCounter;
        private readonly string _alias;

        public InsertBuilder(T entity)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
            _tableName = TableNameHelper.GetTableName<T>();
            string? alias = null;
            try
            {
                var mapObj = Activator.CreateInstance(typeof(TableMap));
                if (mapObj != null)
                {
                    alias = TableNameHelper.GetAliasFromTableMap((TableMap)mapObj);
                }
            }
            catch { }
            _alias = alias ?? TableNameHelper.GetAlias<T>();
        }

        public InsertBuilder(IEnumerable<T> entities)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            _entities = entities.ToList();
            if (_entities.Count == 0) throw new ArgumentException("Collection must contain at least one item.", nameof(entities));
            _tableName = TableNameHelper.GetTableName<T>();
            string? alias = null;
            try
            {
                var mapObj = Activator.CreateInstance(typeof(TableMap));
                if (mapObj != null)
                {
                    alias = TableNameHelper.GetAliasFromTableMap((TableMap)mapObj);
                }
            }
            catch { }
            _alias = alias ?? TableNameHelper.GetAlias<T>();
        }

        /// <summary>
        /// Builds an INSERT template. Supports single entity or a collection (batch insert).
        /// Parameter names are generated uniquely to avoid collisions.
        /// </summary>
        public SqlBuilder.Template BuildSql()
        {
            var props = typeof(T).GetProperties().Where(p => p.CanRead).ToList();
            if (!props.Any()) throw new InvalidOperationException("Type has no readable properties.");

            var cols = string.Join(", ", props.Select(p => p.Name));

            // Single entity
            if (_entities == null)
            {
                var vals = string.Join(", ", props.Select(p =>
                {
                    var value = p.GetValue(_entity);
                    long id = Interlocked.Increment(ref _paramCounter);
                    var name = "@p" + id;
                    _builder.AddParameters(new KeyValuePair<string, object?>(name, value));
                    return name;
                }));

                var temp = $"INSERT INTO {_tableName} ({cols}) VALUES ({vals})";
                return _builder.AddTemplate(temp);
            }

            // Batch insert: build a VALUES list with one tuple per entity
            var rowPlaceholders = new List<string>();
            foreach (var item in _entities)
            {
                var rowParams = new List<string>();
                foreach (var p in props)
                {
                    var value = p.GetValue(item);
                    long id = Interlocked.Increment(ref _paramCounter);
                    var name = "@p" + id;
                    _builder.AddParameters(new KeyValuePair<string, object?>(name, value));
                    rowParams.Add(name);
                }
                rowPlaceholders.Add("(" + string.Join(", ", rowParams) + ")");
            }

            var valsBatch = string.Join(", ", rowPlaceholders);
            var tempBatch = $"INSERT INTO {_tableName} ({cols}) VALUES {valsBatch}";
            return _builder.AddTemplate(tempBatch);
        }

        private string BuildSqlString() 
        {
            var props = typeof(T).GetProperties().Where(p => p.CanRead).ToList();
            if (!props.Any()) throw new InvalidOperationException("Type has no readable properties.");

            var cols = string.Join(", ", props.Select(p => p.Name));

            // Single entity
            if (_entities == null)
            {
                var vals = string.Join(", ", props.Select(p =>
                {
                    var value = p.GetValue(_entity);
                    long id = Interlocked.Increment(ref _paramCounter);
                    var name = "@p" + id;
                    _builder.AddParameters(new KeyValuePair<string, object?>(name, value));
                    return name;
                }));

                return $"INSERT INTO {_tableName} ({cols}) VALUES ({vals})";
            }

            // Batch insert: build a VALUES list with one tuple per entity
            var rowPlaceholders = new List<string>();
            foreach (var item in _entities)
            {
                var rowParams = new List<string>();
                foreach (var p in props)
                {
                    var value = p.GetValue(item);
                    long id = Interlocked.Increment(ref _paramCounter);
                    var name = "@p" + id;
                    _builder.AddParameters(new KeyValuePair<string, object?>(name, value));
                    rowParams.Add(name);
                }
                rowPlaceholders.Add("(" + string.Join(", ", rowParams) + ")");
            }

            var valsBatch = string.Join(", ", rowPlaceholders);
            return $"INSERT INTO {_tableName} ({cols}) VALUES {valsBatch}";
        }

        /// <summary>
        /// Builds an IF NOT EXISTS (...) INSERT (...) statement.
        /// Caller supplies the WHERE clause that identifies the "one" to find.
        /// Example: FindOneInsert("Id = @id", new { id = 123 })
        /// </summary>
        public SqlBuilder.Template FindOneInsert(string whereSql, object? whereParams = null)
        {
            if (whereParams != null)
            {
                _builder.AddParameters(whereParams);
            }

            _builder.Where(whereSql);

            return _builder.AddTemplate($@"
                IF NOT EXISTS(SELECT 1 FROM {_tableName} /**where**/ )
                BEGIN
                    {BuildSqlString()}
                END
                ");
        }
    }
}