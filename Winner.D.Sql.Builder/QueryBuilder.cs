using System.Linq.Expressions;
using Dapper;
using Winner.D.Sql.Builder.Helpers;

namespace Winner.D.Sql.Builder
{
    public class QueryBuilder<TDto, TableMap>
    {
        private readonly SqlBuilder _builder = new();
        private readonly List<string> _from = new();
        private readonly List<string> _joins = new();
        private readonly List<string> _selects = new();
        private readonly Dictionary<Type, string> _aliases = new();

        // paging state
        private int? _offset;
        private int? _fetch;

        /// <summary>
        ///  Top => Select TOP 10, a,b from Table as t
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        public QueryBuilder<TDto, TableMap> Top(int number)
        {
            _selects.Add($"TOP {number}");
            return this;
        }

        // FROM
            /// <summary>
            /// Same select a,b,b from Table as t
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="tableExpr"></param>
            /// <param name="nolock">Support SQL Server</param>
            /// <returns></returns>
        public QueryBuilder<TDto, TableMap> From<T>(Expression<Func<TableMap, T>> tableExpr, bool nolock = false)
        {
            string tableName = TableNameHelper.GetTableName<T>();
            string alias = TableNameHelper.GetAliasFromTableMap(tableExpr) ?? TableNameHelper.GetAlias<T>();
            string nolockSql = nolock ? " WITH (NOLOCK)" : "";
            _from.Add($"{tableName} {alias}{nolockSql}");
            _aliases[typeof(T)] = alias;
            return this;
        }

        public QueryBuilder<TDto, TableMap> InnerJoin<T>(Expression<Func<TableMap, T>> tableExpr,
                                          Expression<Func<TableMap, bool>> onExpr,
                                          bool nolock = false)
        {
            string onSql = BuildJoin(tableExpr, onExpr, nolock);
            if (!_joins.Any(x => x == "/**innerjoin**/"))
            {
                _joins.Add("/**innerjoin**/");
            }

            _builder.InnerJoin(onSql);
            return this;
        }

        public QueryBuilder<TDto, TableMap> LeftJoin<T>(Expression<Func<TableMap, T>> tableExpr,
                                          Expression<Func<TableMap, bool>> onExpr,
                                          bool nolock = false)
        {
            string onSql = BuildJoin(tableExpr, onExpr, nolock);
            if (!_joins.Any(x => x == "/**leftjoin**/"))
            {
                _joins.Add("/**leftjoin**/");
            }

            _builder.LeftJoin(onSql);
            return this;
        }

        // Pagination helpers
        public QueryBuilder<TDto, TableMap> Paginate(int page, int pageSize)
        {
            if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), "page must be >= 1");
            if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), "pageSize must be >= 1");

            int offset = (page - 1) * pageSize;
            _offset = offset;
            _fetch = pageSize;

            // ensure parameters are present in the final template Parameters
            _builder.AddParameters(new { offset, fetch = pageSize });
            return this;
        }

        public QueryBuilder<TDto, TableMap> SkipTake(int offset, int take)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (take < 1) throw new ArgumentOutOfRangeException(nameof(take));

            _offset = offset;
            _fetch = take;

            _builder.AddParameters(new { offset, fetch = take });
            return this;
        }

        public QueryBuilder<TDto, TableMap> RightJoin<T>(Expression<Func<TableMap, T>> tableExpr,
                                          Expression<Func<TableMap, bool>> onExpr,
                                          bool nolock = false)
        {
            string onSql = BuildJoin(tableExpr, onExpr, nolock);
            if (!_joins.Any(x => x == "/**rightjoin**/"))
            {
                _joins.Add("/**rightjoin**/");
            }

            _builder.RightJoin(onSql);
            return this;
        }

        // SELECT lambda
        public QueryBuilder<TDto, TableMap> Select(Expression<Func<TableMap, TDto>> expr)
        {
            if (expr.Body is MemberInitExpression init)
            {
                foreach (var binding in init.Bindings.OfType<MemberAssignment>())
                {
                    if (binding.Expression is MemberExpression me && me.Expression != null)
                    {
                        Type entityType = me.Expression.Type;
                        string alias = _aliases[entityType];
                        string column = me.Member.Name;
                        string dtoCol = binding.Member.Name;
                        _selects.Add($"{alias}.{column} AS {dtoCol}");
                    }
                }
            }
            return this;
        }

        // WHERE lambda
        public QueryBuilder<TDto, TableMap> Where(Expression<Func<TableMap, bool>> expr)
        {
            string sql = ParseExpression(expr.Body, _aliases);
            _builder.Where(sql);
            return this;
        }

        public QueryBuilder<TDto, TableMap> Or(Expression<Func<TableMap, bool>> expr)
        {
            string sql = ParseExpression(expr.Body, _aliases);
            _builder.OrWhere(sql);
            return this;
        }

        public QueryBuilder<TDto, TableMap> And(Expression<Func<TableMap, bool>> expr) => Where(expr);

        public QueryBuilder<TDto, TableMap> OrderByAsc<TProperty>(Expression<Func<TableMap, TProperty>> expr)
        {
            string sql = ParseOrderExpression(expr.Body, asc: true, _aliases);
            _builder.OrderBy(sql);
            return this;
        }

        public QueryBuilder<TDto, TableMap> OrderByDesc<TProperty>(Expression<Func<TableMap, TProperty>> expr)
        {
            string sql = ParseOrderExpression(expr.Body, asc: false, _aliases);
            _builder.OrderBy(sql);
            return this;
        }

        public SqlBuilder.Template Build()
        {
            string select = _selects.Any() ? string.Join(", ", _selects) : "*";
            string from = string.Join(", ", _from);
            string join = string.Join(" ", _joins);

            string paging = _offset.HasValue && _fetch.HasValue
                ? $"OFFSET @offset ROWS FETCH NEXT @fetch ROWS ONLY"
                : "";

            return _builder.AddTemplate($@"
            SELECT {select}
            FROM {from}
            {join}
            /**where**/ /**orderby**/
            {paging}");
        }

        public SqlBuilder.Template Count(string countExpression = "1", string alias = "Total")
        {
            string from = string.Join(", ", _from);
            string join = string.Join(" ", _joins);

            return _builder.AddTemplate($@"
            SELECT COUNT({countExpression}) AS {alias}
            FROM {from}
            {join}
            /**where**/
            ");
        }

        #region Private Methods
        private string BuildJoin<T>(Expression<Func<TableMap, T>> tableExpr,
                                          Expression<Func<TableMap, bool>> onExpr,
                                          bool nolock = false)
        {
            string tableName = TableNameHelper.GetTableName<T>();
            string alias = TableNameHelper.GetAliasFromTableMap(tableExpr) ?? TableNameHelper.GetAlias<T>();
            _aliases[typeof(T)] = alias;
            string nolockSql = nolock ? " WITH (NOLOCK)" : "";
            return $"{tableName} {alias}{nolockSql} ON {ParseExpression(onExpr.Body, _aliases)}";
        }

        // ---- Expression Parser  ----
        private string GetBinaryOperator(ExpressionType nodeType) => nodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "<>",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.AndAlso => "AND",
            ExpressionType.OrElse => "OR",
            // Thêm các toán tử khác (Add, Subtract, Multiply, Divide...) nếu cần
            _ => throw new NotSupportedException($"Unsupported binary operator: {nodeType}")
        };

        private string ParseExpression(Expression expr, Dictionary<Type, string> aliases)
        {
            return expr switch
            {
                BinaryExpression be => ParseBinaryExpression(be, aliases),
                MemberExpression me => ParseMemberExpression(me, aliases),
                ConstantExpression ce => ParseConstantExpression(ce),
                _ => throw new NotSupportedException($"Unsupported expression type: {expr.GetType().Name}")
            };
        }

        // 1. Xử lý BinaryExpression
        private string ParseBinaryExpression(BinaryExpression be, Dictionary<Type, string> aliases)
        {
            string left = ParseExpression(be.Left, aliases);
            string right = ParseExpression(be.Right, aliases);
            string op = GetBinaryOperator(be.NodeType);

            string result = $"{left} {op} {right}";

            if (be.Left is BinaryExpression || be.Right is BinaryExpression)
                result = $"({result})";

            return result;
        }

        private string ParseMemberExpression(MemberExpression me, Dictionary<Type, string> aliases)
        {
            if (me.Expression == null)
            {
                throw new NotSupportedException($"Unsupported MemberExpression: Static or null expression member '{me.Member.Name}'");
            }

            Type entityType = me.Expression.Type;
            if (!aliases.TryGetValue(entityType, out string? alias))
            {
                throw new KeyNotFoundException($"Alias not found for entity type: {entityType.Name}");
            }

            string column = me.Member.Name;
            return $"{alias}.{column}";
        }

        private string ParseConstantExpression(ConstantExpression ce)
        {
            string paramName = "@p" + ce.Value?.GetHashCode();
            _builder.AddParameters(new KeyValuePair<string, object?>(paramName, ce.Value));

            return paramName;
        }

        private string ParseOrderExpression(Expression expr, bool asc, Dictionary<Type, string> aliases)
        {
            return expr switch
            {
                MemberExpression me => $"{ParseMemberExpression(me, aliases)} {(asc ? "ASC" : "DESC")}",
                ConstantExpression => throw new NotSupportedException("Ordering by a constant value is not supported."),
                _ => throw new NotSupportedException($"Unsupported expression type for ORDER BY: {expr.GetType().Name}")
            };
        }
        #endregion
    }
}
