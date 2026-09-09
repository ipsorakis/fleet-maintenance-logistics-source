using Microsoft.AspNetCore.Mvc;

namespace FML.Api.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;

    public AuthController(AuthService auth)
    {
        _auth = auth;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var response = await _auth.LoginAsync(request);
        return response is null ? Unauthorized() : Ok(response);
    }

    [HttpGet("users")]
    public async Task<ActionResult<IEnumerable<User>>> Users() =>
        Ok(await _auth.GetUsersAsync());

    [HttpGet("users/{id:int}")]
    public async Task<ActionResult<User>> Get(int id)
    {
        var user = await _auth.GetUserAsync(id);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost("users")]
    public async Task<ActionResult<User>> Create([FromBody] CreateUserRequest request)
    {
        var user = await _auth.CreateUserAsync(request);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpPost("users/{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id) =>
        await _auth.DeactivateAsync(id) ? NoContent() : NotFound();
}
