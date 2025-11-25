using Dapper;
using System.Linq.Expressions;
using Winner.D.Sql.Builder.Enum;
using Winner.D.Sql.Builder.Helpers;

namespace Winner.D.Sql.Builder
{
    public class QueryBuilder<TDto, TableMap>
    {
        private readonly SqlBuilder _builder = new();
        private readonly List<string> _from = new();
        private readonly List<string> _joins = new();
        private readonly List<string> _selects = new();
        private readonly List<string> _orBuilder = new();
        private readonly Dictionary<Type, string> _aliases = new();

        private long _paramCounter;
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

            _orBuilder.Add($"OR {sql}");
            return this;
        }

        private void WhereIn<TValue>(Expression<Func<TableMap, TValue>> expr, IEnumerable<TValue> values, WD_QueryInType queryInType)
        {
            if (values == null || !values.Any()) return;

            string column = GetColumnSqlFromExpression(expr.Body, _aliases);
            string valueList = FormatValuesForSql(values);
            string type = queryInType == WD_QueryInType.NOT_IN ? "NOT IN" : "IN";
            if (!string.IsNullOrEmpty(column) && !string.IsNullOrEmpty(valueList))
            {
                string sql = $"{column} {type} ({valueList})";
                _builder.Where(sql);
            }

            return;
        }

        public QueryBuilder<TDto, TableMap> In<TValue>(Expression<Func<TableMap, TValue>> expr, IEnumerable<TValue> values)
        {
            WhereIn(expr, values, WD_QueryInType.IN);
            return this;
        }

        public QueryBuilder<TDto, TableMap> NotIn<TValue>(Expression<Func<TableMap, TValue>> expr, IEnumerable<TValue> values)
        {
            WhereIn(expr, values, WD_QueryInType.NOT_IN);
            return this;
        }

        public QueryBuilder<TDto, TableMap> Like<TValue>(Expression<Func<TableMap, TValue>> expr, 
            string pattern, bool caseInsensitive = false)
        {
            if (string.IsNullOrEmpty(pattern)) return this;

            // If user didn't provide SQL wildcards, treat as contains
            if (!pattern.Contains("%") && !pattern.Contains("*"))
            {
                pattern = $"%{pattern}%";
            }
            pattern = pattern.Replace('*', '%');

            string column = GetColumnSqlFromExpression(expr.Body, _aliases);
            long id = Interlocked.Increment(ref _paramCounter);
            string paramName = "@p" + id;
            _builder.AddParameters(new KeyValuePair<string, object?>(paramName, pattern));

            string sql = caseInsensitive
                ? $"LOWER({column}) LIKE LOWER({paramName})"
                : $"{column} LIKE {paramName}";

            _builder.Where(sql);
            return this;
        }

        /// <summary>
        /// Adds a full-text search predicate. Uses CONTAINS by default; set freeText = true to use FREETEXT.
        /// Requires full-text index configured on the target column in SQL Server.
        /// </summary>
        public QueryBuilder<TDto, TableMap> FullTextSearch<TValue>(Expression<Func<TableMap, TValue>> expr, 
            string searchTerm, WD_QueryFullTextType queryFullTextType)
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) return this;

            string column = GetColumnSqlFromExpression(expr.Body, _aliases);
            long id = Interlocked.Increment(ref _paramCounter);
            string paramName = "@p" + id;
            _builder.AddParameters(new KeyValuePair<string, object?>(paramName, searchTerm));

            string sql = queryFullTextType == WD_QueryFullTextType.Freetext
                ? $"FREETEXT({column}, {paramName})"
                : $"CONTAINS({column}, {paramName})";

            _builder.Where(sql);
            return this;
        }

        public QueryBuilder<TDto, TableMap> CustomAndQueryString(string sql)
        {
            _builder.Where(sql);
            return this;
        }

        public QueryBuilder<TDto, TableMap> CustomOrQueryString(string sql)
        {
            _orBuilder.Add($"OR {sql}");           
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
            string or = string.Join(" ", _orBuilder);

            string paging = _offset.HasValue && _fetch.HasValue
                ? $"OFFSET @offset ROWS FETCH NEXT @fetch ROWS ONLY"
                : "";

            return _builder.AddTemplate($@"
            SELECT {select}
            FROM {from}
            {join}
            /**where**/ {or} /**orderby**/
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
        private string GetColumnSqlFromExpression(Expression expr, Dictionary<Type, string> aliases)
        {
            // Single member: x => x.Entity.Property or x => ((object)x.Entity.Property)
            if (expr is MemberExpression me) return ParseMemberExpression(me, aliases);
            if (expr is UnaryExpression ue && ue.Operand is MemberExpression me2) return ParseMemberExpression(me2, aliases);

            // Anonymous object: x => new { x.A, x.B }
            if (expr is NewExpression ne)
            {
                var cols = ne.Arguments.Select(arg =>
                {
                    if (arg is MemberExpression ame) return ParseMemberExpression(ame, aliases);
                    if (arg is UnaryExpression aue && aue.Operand is MemberExpression ame2) return ParseMemberExpression(ame2, aliases);
                    throw new NotSupportedException("Unsupported expression inside anonymous object. Use simple member accesses.");
                }).ToList();

                if (cols.Count == 0) throw new NotSupportedException("No columns found in expression.");
                if (cols.Count == 1) return cols[0];
                return "(" + string.Join(", ", cols) + ")";
            }

            // MemberInitExpression: x => new DTO { Prop1 = x.A, Prop2 = x.B }
            if (expr is MemberInitExpression mi)
            {
                var cols = mi.Bindings.OfType<MemberAssignment>().Select(binding =>
                {
                    var bexp = binding.Expression;
                    if (bexp is MemberExpression bme) return ParseMemberExpression(bme, aliases);
                    if (bexp is UnaryExpression bue && bue.Operand is MemberExpression bme2) return ParseMemberExpression(bme2, aliases);
                    throw new NotSupportedException("Unsupported MemberInit expression. Use simple member accesses.");
                }).ToList();

                if (cols.Count == 0) throw new NotSupportedException("No columns found in member init expression.");
                if (cols.Count == 1) return cols[0];
                return "(" + string.Join(", ", cols) + ")";
            }

            throw new NotSupportedException("Expression must be a member access or an anonymous/new object with member accesses (e.g. x => x.Entity.Prop OR x => new { x.Prop1, x.Prop2 }).");
        }

        private string FormatValuesForSql<TValue>(IEnumerable<TValue> values)
        {
            if (values == null) return string.Empty;
            var list = new List<string>();
            foreach (var v in values)
            {
                long id = Interlocked.Increment(ref _paramCounter);
                string paramName = "@p" + id;
                _builder.AddParameters(new KeyValuePair<string, object?>(paramName, v));
                list.Add(paramName);
            }
            return string.Join(", ", list);
        }

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
            long id = Interlocked.Increment(ref _paramCounter);
            string paramName = "@p" + id;
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
