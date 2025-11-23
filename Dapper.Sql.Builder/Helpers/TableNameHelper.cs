using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;

namespace Dapper.Sql.Builder.Helpers
{
    public static class TableNameHelper
    {
        public static string GetTableName<T>()
        {
            var tableAttr = typeof(T).GetCustomAttribute<TableAttribute>();
            return tableAttr != null ? tableAttr.Name : typeof(T).Name;
        }

        public static string GetAlias<T>()
        {
            return typeof(T).Name; // fallback
        }

        public static string? GetAliasFromTableMap<TableMap, T>(Expression<Func<TableMap, T>> expr)
        {
            if (expr.Body is MemberExpression me)
            {
                var aliasProp = me.Member.DeclaringType!.GetProperties()
                    .FirstOrDefault(p => p.Name == me.Member.Name + "Alias" && p.PropertyType == typeof(string));
                if (aliasProp != null)
                {
                    return (string?)aliasProp.GetValue(Activator.CreateInstance(me.Member.DeclaringType));
                }
            }
            return null;
        }
    }
}
