using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Extensions;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(AppDbContext db, JwtService jwt) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req)
    {
        var uname = req.Username.Trim();
        if (await db.Users.AnyAsync(u => u.Username.ToLower() == uname.ToLower() || u.Email.ToLower() == req.Email.Trim().ToLower()))
            return BadRequest(new { message = "Username or email already exists." });

        var user = new User
        {
            Username = uname,
            Email = req.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = "User"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Give every new user starter accounts (Money Manager style)
        db.Accounts.AddRange(
            new Account { UserId = user.Id, Name = "Cash", Type = "Cash", InitialBalance = 0 },
            new Account { UserId = user.Id, Name = "Bank Account", Type = "Bank", InitialBalance = 0 });

        // ...and their own private starter categories (names match the frontend icon map)
        string[] expenseCats =
        [
            "Food & Dining", "Groceries", "Transport", "Fuel", "Rent", "Utilities",
            "Shopping", "Entertainment", "Health", "Education", "Travel",
            "Personal Care", "Gifts & Donations", "Other Expense"
        ];
        string[] incomeCats = ["Salary", "Bonus", "Interest", "Investment Returns", "Other Income"];
        db.Categories.AddRange(expenseCats.Select(n => new Category { UserId = user.Id, Name = n, Type = 0 }));
        db.Categories.AddRange(incomeCats.Select(n => new Category { UserId = user.Id, Name = n, Type = 1 }));

        await db.SaveChangesAsync();

        return await IssueTokens(user);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        var key = req.UsernameOrEmail.Trim().ToLower();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == key || u.Email.ToLower() == key);
        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid username or password." });

        return await IssueTokens(user);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me()
    {
        var userId = User.GetUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return NotFound();
        return new UserDto(user.Id, user.Username, user.Email, user.Role,
            user.MonthStartDay, user.IncomeColor, user.ExpenseColor);
    }

    [HttpPut("preferences")]
    [Authorize]
    public async Task<IActionResult> SavePreferences(SavePreferencesRequest req)
    {
        if (req.MonthStartDay is < 1 or > 28)
            return BadRequest(new { message = "Month start day must be between 1 and 28." });

        var userId = User.GetUserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return NotFound();

        user.MonthStartDay = req.MonthStartDay;
        user.IncomeColor = string.IsNullOrWhiteSpace(req.IncomeColor) ? null : req.IncomeColor.Trim();
        user.ExpenseColor = string.IsNullOrWhiteSpace(req.ExpenseColor) ? null : req.ExpenseColor.Trim();
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest req)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.RefreshToken == req.RefreshToken);
        if (user is null || user.RefreshTokenExpiry < DateTime.UtcNow)
            return Unauthorized(new { message = "Session expired. Please sign in again." });

        return await IssueTokens(user);
    }

    private async Task<AuthResponse> IssueTokens(User user)
    {
        var (access, expires) = jwt.CreateAccessToken(user);
        var (refresh, refreshExpiry) = JwtService.CreateRefreshToken();
        user.RefreshToken = refresh;
        user.RefreshTokenExpiry = refreshExpiry;
        await db.SaveChangesAsync();

        return new AuthResponse(access, refresh, expires,
            new UserDto(user.Id, user.Username, user.Email, user.Role));
    }
}
