using Test.Program;
using Winner.D.Sql.Builder;
using Winner.D.Sql.Builder.Enum;


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
    .In(t => t.User.Id, new[] { 1, 2, 3, 4, 5 })
    .In(t => t.Role.Name, new[] { "admin", "user" })
    .NotIn(t => t.User.Id, new[] { 6, 7, 8 })
    .Like(t => t.User.Name, "%John%")
    .FullTextSearch(t => t.User.Name, "developer", WD_QueryFullTextType.Freetext)
    .FullTextSearch(t => t.Role.Name, "developer", WD_QueryFullTextType.Contains)
    .OrderByAsc(t => t.User.Name)
    .OrderByDesc(t => t.User.Name)
    .Paginate(1, 10);

if (1 == 1)
{
    qb.Where(t => t.User.IsActived == true);
}


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