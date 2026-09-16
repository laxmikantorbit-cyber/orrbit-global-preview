using BusinessOS.Api.Commerce;
using BusinessOS.Crm;

namespace BusinessOS.Api.Crm;

public static class FreeTestingPublicCrmTeamEndpoints
{
    public static readonly Guid DemoTenantId=Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly CrmTeamMember[] Seed=
    [
        new(Guid.Parse("11111111-aaaa-1111-1111-111111111111"),DemoTenantId,"CRM Owner","owner@crm.demo",null,CrmRoleCode.Owner),
        new(Guid.Parse("11111111-bbbb-1111-1111-111111111111"),DemoTenantId,"Sales Manager","manager@crm.demo",null,CrmRoleCode.SalesManager),
        new(Guid.Parse("11111111-cccc-1111-1111-111111111111"),DemoTenantId,"Sales Executive","sales@crm.demo",null,CrmRoleCode.SalesExecutive),
        new(Guid.Parse("11111111-dddd-1111-1111-111111111111"),DemoTenantId,"Telecaller","telecaller@crm.demo",null,CrmRoleCode.Telecaller)
    ];

    public static IEndpointRouteBuilder MapFreeTestingPublicCrmTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/testing/public/crm");
        group.MapGet("/session",(HttpContext context,IConfiguration config,IHostEnvironment env)=>
        {
            if(!Enabled(config,env))return Disabled();
            var member=CrmFreeTestingAccessMiddleware.Current(context);
            return Results.Ok(new CrmSessionResponse(ToResponse(member),CrmFreeTestingAccessMiddleware.CanViewAllOwnedRecords(member)));
        });
        group.MapGet("/roles",(IConfiguration config,IHostEnvironment env)=>
        {
            if(!Enabled(config,env))return Disabled();
            var roles=Enum.GetValues<CrmRoleCode>().Select(role=>new CrmRoleResponse(role.ToString(),CrmRolePolicy.Permissions(role).Select(x=>x.ToString()).ToArray())).ToArray();
            return Results.Ok(new{roles});
        });
        group.MapGet("/team",async(IConfiguration config,IHostEnvironment env,ICrmTeamRepository repo,CancellationToken ct)=>
        {
            if(!Enabled(config,env))return Disabled(); await EnsureSeedAsync(repo,ct); var items=await repo.ListAsync(DemoTenantId,ct); return Results.Ok(new{members=items.Select(ToResponse).ToArray()});
        });
        group.MapPost("/team",async(CreateCrmTeamMemberRequest request,IConfiguration config,IHostEnvironment env,ICrmTeamRepository repo,CancellationToken ct)=>
        {
            if(!Enabled(config,env))return Disabled(); if(!Enum.TryParse<CrmRoleCode>(request.Role,true,out var role))return Results.BadRequest(new ErrorResponse("Valid CRM role is required."));
            try{var member=new CrmTeamMember(Guid.NewGuid(),DemoTenantId,request.DisplayName,request.Email,request.MobileNumber,role);await repo.AddAsync(member,ct);return Results.Ok(ToResponse(member));}catch(Exception ex) when(ex is ArgumentException or InvalidOperationException){return Results.BadRequest(new ErrorResponse(ex.Message));}
        });
        group.MapPost("/team/{memberId:guid}/role",async(Guid memberId,ChangeCrmTeamRoleRequest request,IConfiguration config,IHostEnvironment env,ICrmTeamRepository repo,CancellationToken ct)=>
        {
            if(!Enabled(config,env))return Disabled(); var member=await repo.GetAsync(DemoTenantId,memberId,ct);if(member is null)return Results.NotFound(new ErrorResponse("Team member not found."));if(!Enum.TryParse<CrmRoleCode>(request.Role,true,out var role))return Results.BadRequest(new ErrorResponse("Valid CRM role is required."));member.ChangeRole(role);await repo.SaveAsync(member,ct);return Results.Ok(ToResponse(member));
        });
        group.MapPost("/team/{memberId:guid}/status",async(Guid memberId,ChangeCrmTeamStatusRequest request,IConfiguration config,IHostEnvironment env,ICrmTeamRepository repo,CancellationToken ct)=>
        {
            if(!Enabled(config,env))return Disabled();var member=await repo.GetAsync(DemoTenantId,memberId,ct);if(member is null)return Results.NotFound(new ErrorResponse("Team member not found."));member.SetActive(request.Active);await repo.SaveAsync(member,ct);return Results.Ok(ToResponse(member));
        });
        return app;
    }

    public static async Task<CrmTeamMember?> GetActiveMemberAsync(ICrmTeamRepository repo,Guid? memberId,CancellationToken ct)
    {
        if(!memberId.HasValue)return null; await EnsureSeedAsync(repo,ct); var member=await repo.GetAsync(DemoTenantId,memberId.Value,ct); return member is {Active:true}?member:null;
    }
    private static async Task EnsureSeedAsync(ICrmTeamRepository repo,CancellationToken ct){if((await repo.ListAsync(DemoTenantId,ct)).Count>0)return;foreach(var member in Seed)try{await repo.AddAsync(member,ct);}catch(InvalidOperationException){}}
    private static bool Enabled(IConfiguration c,IHostEnvironment e)=>!e.IsProduction()&&string.Equals(c["BusinessOS:DeploymentMode"],"FreeTesting",StringComparison.OrdinalIgnoreCase);
    private static IResult Disabled()=>Results.NotFound(new ErrorResponse("Public CRM staging is not enabled."));
    private static CrmTeamMemberResponse ToResponse(CrmTeamMember x)=>new(x.Id,x.DisplayName,x.Email,x.MobileNumber,x.Role.ToString(),x.Active,x.CreatedAtUtc,CrmRolePolicy.Permissions(x.Role).Select(p=>p.ToString()).ToArray());
}

public sealed record CreateCrmTeamMemberRequest(string DisplayName,string Email,string? MobileNumber,string Role);
public sealed record ChangeCrmTeamRoleRequest(string Role);
public sealed record ChangeCrmTeamStatusRequest(bool Active);
public sealed record CrmRoleResponse(string Role,IReadOnlyList<string> Permissions);
public sealed record CrmTeamMemberResponse(Guid Id,string DisplayName,string Email,string? MobileNumber,string Role,bool Active,DateTimeOffset CreatedAtUtc,IReadOnlyList<string> Permissions);
public sealed record CrmSessionResponse(CrmTeamMemberResponse Member,bool CanViewAllOwnedRecords);
