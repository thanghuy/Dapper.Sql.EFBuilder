using Dapper.Sql.Builder;
using Test.Program;


var qb = new QueryBuilder<UserRoleDto, DBSocialMapTable>()
    .From(t => t.User, nolock: true)
    .InnerJoin(t => t.Role, t => t.Role.Id == t.User.RoleId, nolock: true)
    .Select(t => new UserRoleDto
    {
        UserId = t.User.Id,
        UserName = t.User.Name,
        RoleName = t.Role.Name
    })
    .Where(t => t.User.Id == 10)
    .And(t => t.Role.Id == 1 || t.Role.Name == "a")
    .Or(t => t.Role.Id == 1 || t.Role.Name == "a");


var template = qb.Build();
string sql = template.RawSql;
var param = template.Parameters;


Console.WriteLine(sql);