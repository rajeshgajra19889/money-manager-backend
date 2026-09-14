using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Extensions;
using ExpenseTracker.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BudgetsController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Returns every budget the user has defined (one per category, carried
    /// forward to all months), with 'spent' computed for the requested month.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<BudgetDto>>> GetAll(
        [FromQuery] int? month, [FromQuery] int? year)
    {
        var userId = User.GetUserId();

        var today = DateTime.Today;
        var y = year is > 0 ? year.Value : today.Year;
        var m = month is > 0 ? month.Value : today.Month;

        // One definition per category: keep the most recently edited row.
        var all = await db.Budgets.AsNoTracking()
            .Where(b => b.UserId == userId && !b.IsDeleted).ToListAsync();
        var budgets = all
            .GroupBy(b => b.CategoryId)
            .Select(g => g.OrderByDescending(b => b.Id).First())
            .OrderBy(b => b.CategoryId)
            .ToList();

        var names = await db.Categories.ToDictionaryAsync(c => c.Id, c => c.Name);

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var msd = Math.Clamp(user?.MonthStartDay == 0 ? (byte)1 : user!.MonthStartDay, (byte)1, (byte)28);

        var start = new DateTime(y, m, msd);
        var endExclusive = start.AddMonths(1);

        var result = new List<BudgetDto>(budgets.Count);
        foreach (var b in budgets)
        {
            var spent = await db.Transactions
                .Where(t => t.UserId == userId && !t.IsDeleted && t.Type == 0
                    && t.CategoryId == b.CategoryId
                    && t.Date >= start && t.Date < endExclusive)
                .SumAsync(t => (decimal?)t.Amount) ?? 0m;
            result.Add(new BudgetDto(b.Id, b.MonthlyLimit, m, y,
                b.CategoryId, names.GetValueOrDefault(b.CategoryId, "Unknown"), spent));
        }
        return result;
    }

    /// <summary>
    /// Creates or updates the user's standing limit for a category. The limit
    /// applies to every month from now on (month/year are only stamped on new
    /// rows for schema compatibility).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Save(SaveBudgetRequest req)
    {
        if (req.MonthlyLimit <= 0) return BadRequest(new { message = "Limit must be greater than zero." });
        var userId = User.GetUserId();

        var existing = await db.Budgets
            .Where(b => b.UserId == userId && b.CategoryId == req.CategoryId)
            .OrderByDescending(b => b.Id).FirstOrDefaultAsync();

        if (existing is null)
        {
            db.Budgets.Add(new Budget
            {
                UserId = userId,
                MonthlyLimit = req.MonthlyLimit,
                Month = req.Month,
                Year = req.Year,
                CategoryId = req.CategoryId,
                IsDeleted = false
            });
        }
        else
        {
            existing.MonthlyLimit = req.MonthlyLimit;
            existing.IsDeleted = false; // re-saving a deleted budget restores it
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = User.GetUserId();
        var budget = await db.Budgets.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId && !b.IsDeleted);
        if (budget is null) return NotFound();

        budget.IsDeleted = true;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
