using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;

namespace Winner.D.Sql.Builder.Helpers
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

        public static string GetAliasFromTableMap<T>(T tableMap)
        {
            if (tableMap == null) return null;

            var type = tableMap.GetType();

            // Find all string properties that look like alias holders (end with "Alias"),
            // prefer public instance properties and return the first non-empty value.
            var aliasProps = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                 .Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("Alias"))
                                 .ToArray();

            // fallback to non-public if nothing public found
            if (aliasProps.Length == 0)
            {
                aliasProps = type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic)
                                 .Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("Alias"))
                                 .ToArray();
            }

            foreach (var prop in aliasProps)
            {
                var val = (string?)prop.GetValue(tableMap);
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val;
                }
            }

            return GetAlias<T>();
        }
    }
}
