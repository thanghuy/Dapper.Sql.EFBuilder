using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using Dapper;
using Dapper.Sql.Builder.Helpers;

namespace Dapper.Sql.Builder
{
    public class QueryBuilder<TDto, TableMap>
    {
        private readonly SqlBuilder _builder = new();
        private readonly List<string> _from = new();
        private readonly List<string> _joins = new();
        private readonly List<string> _selects = new();
        private readonly Dictionary<Type, string> _aliases = new();

        // FROM
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
                    if (binding.Expression is MemberExpression me)
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

        public QueryBuilder<TDto, TableMap> OrderBy(string sql)
        {
            _builder.OrderBy(sql);
            return this;
        }

        public SqlBuilder.Template Build()
        {
            string select = _selects.Any() ? string.Join(", ", _selects) : "*";
            string from = string.Join(", ", _from);
            string join = string.Join(" ", _joins);

            return _builder.AddTemplate($@"
            SELECT {select}
            FROM {from}
            {join}
            /**where**/ /**orderby**/
            ");
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

        // ---- Expression Parser đơn giản ----
        private string ParseExpression(Expression expr, Dictionary<Type, string> aliases)
        {
            if (expr is BinaryExpression be)
            {
                string left = ParseExpression(be.Left, aliases);
                string right = ParseExpression(be.Right, aliases);

                string op = be.NodeType switch
                {
                    ExpressionType.Equal => "=",
                    ExpressionType.NotEqual => "<>",
                    ExpressionType.GreaterThan => ">",
                    ExpressionType.GreaterThanOrEqual => ">=",
                    ExpressionType.LessThan => "<",
                    ExpressionType.LessThanOrEqual => "<=",
                    ExpressionType.AndAlso => "AND",
                    ExpressionType.OrElse => "OR",
                    _ => throw new NotSupportedException(be.NodeType.ToString())
                };

                string result = $"{left} {op} {right}";

                bool leftIsBinary = be.Left is BinaryExpression;
                bool rightIsBinary = be.Right is BinaryExpression;
                if (leftIsBinary || rightIsBinary)
                    result = $"({result})";

                return result;
            }
            else if (expr is MemberExpression me)
            {
                Type entityType = me.Expression!.Type;
                string alias = _aliases[entityType];
                string column = me.Member.Name;
                return $"{alias}.{column}";
            }
            else if (expr is ConstantExpression ce)
            {
                string paramName = "@p" + ce.Value?.GetHashCode();
                _builder.AddParameters(new { p = ce.Value });
                return paramName!;
            }
            else
            {
                throw new NotSupportedException($"Unsupported expression type: {expr.GetType().Name}");
            }
        }

    }
}
