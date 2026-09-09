using FML.Api.Auth;
using FML.Common.Auth;

namespace FML.Tests;

public class AuthTests
{
    [Fact]
    public async Task Login_succeeds_with_the_right_password_and_fails_otherwise()
    {
        using var ctx = new FmlTestContext();
        await ctx.Auth.CreateUserAsync(new CreateUserRequest("planner", "planner@fml.local", "s3cret", UserRole.Planner));

        var ok = await ctx.Auth.LoginAsync(new LoginRequest("planner", "s3cret"));
        var bad = await ctx.Auth.LoginAsync(new LoginRequest("planner", "wrong"));

        Assert.NotNull(ok);
        Assert.Equal(UserRole.Planner, ok!.Role);
        Assert.False(string.IsNullOrWhiteSpace(ok.Token));
        Assert.True(ok.ExpiresUtc > DateTime.UtcNow);
        Assert.Null(bad);
    }

    [Fact]
    public async Task Passwords_are_never_stored_in_clear_text()
    {
        using var ctx = new FmlTestContext();
        var user = await ctx.Auth.CreateUserAsync(
            new CreateUserRequest("tech", "tech@fml.local", "s3cret", UserRole.Technician));

        Assert.NotEqual("s3cret", user.PasswordHash);
        Assert.Equal(AuthService.HashPassword("s3cret"), user.PasswordHash);
    }

    [Fact]
    public async Task Deactivated_technicians_are_not_offered_for_assignment()
    {
        using var ctx = new FmlTestContext();
        var technician = await ctx.CreateTechnicianAsync("tech.lars");

        Assert.Equal(technician.Id, (await ctx.Auth.FindAvailableTechnicianAsync())?.Id);

        Assert.True(await ctx.Auth.DeactivateAsync(technician.Id));

        Assert.Null(await ctx.Auth.FindAvailableTechnicianAsync());
        Assert.Null(await ctx.Auth.LoginAsync(new LoginRequest("tech.lars", "pw")));
    }
}
