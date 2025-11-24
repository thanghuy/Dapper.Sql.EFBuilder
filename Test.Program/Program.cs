using Test.Program;
using Winner.D.Sql.Builder;


var qb = new QueryBuilder<UserRoleDto, DBSocialMapTable>()
    .Top(100)
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
    .Or(t => t.Role.Id == 1 || t.Role.Name == "a")
    .Or(t => t.Role.Id == 1 || t.Role.Name == "a")
    .OrderByAsc(t => t.User.Name)
    .OrderByDesc(t => t.User.Name)
    .Paginate(1, 10);


var template = qb.Build();
string sql = template.RawSql;
var param = template.Parameters;


Console.WriteLine(sql);
Console.WriteLine(qb.Count().RawSql);

var insertBuilder = new InsertBuilder<User>
(
    new User
    {
        Id = 1,
        Name = "John",
        RoleId = 2
    }
);

string insertSql = insertBuilder.BuildSql();
Console.WriteLine(insertSql);