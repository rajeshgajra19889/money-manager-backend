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
public class RecurringsController(AppDbContext db) : ControllerBase
{
    private const int MaxCatchUpPerRule = 60;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RecurringDto>>> GetAll()
    {
        var userId = User.GetUserId();

        return await db.Recurrings.AsNoTracking()
            .Where(r => r.UserId == userId && !r.IsDeleted)
            .OrderBy(r => r.NextDueDate)
            .Select(r => new RecurringDto(
                r.Id, r.Type, r.Amount, r.CategoryId,
                db.Categories.Where(c => c.Id == r.CategoryId).Select(c => c.Name).FirstOrDefault(),
                r.AccountId,
                db.Accounts.Where(a => a.Id == r.AccountId).Select(a => (string?)a.Name).FirstOrDefault(),
                r.Note, r.Frequency, r.StartDate, r.NextDueDate, r.IsActive))
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<RecurringDto>> Create(SaveRecurringRequest req)
    {
        if (req.Amount <= 0) return BadRequest(new { message = "Amount must be greater than zero." });
        if (req.Frequency > 4) return BadRequest(new { message = "Invalid frequency." });

        var userId = User.GetUserId();
        var recurring = new Recurring
        {
            UserId = userId,
            Type = req.Type,
            Amount = req.Amount,
            CategoryId = req.CategoryId,
            AccountId = req.AccountId,
            Note = req.Note?.Trim(),
            Frequency = req.Frequency,
            StartDate = req.StartDate,
            NextDueDate = req.StartDate,
            IsActive = req.IsActive
        };
        db.Recurrings.Add(recurring);
        await db.SaveChangesAsync();
        return Ok(recurring.Id);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveRecurringRequest req)
    {
        if (req.Amount <= 0) return BadRequest(new { message = "Amount must be greater than zero." });
        if (req.Frequency > 4) return BadRequest(new { message = "Invalid frequency." });

        var userId = User.GetUserId();
        var recurring = await db.Recurrings
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId && !r.IsDeleted);
        if (recurring is null) return NotFound();

        // If schedule moved forward, reset the due date to the new start
        recurring.Type = req.Type;
        recurring.Amount = req.Amount;
        recurring.CategoryId = req.CategoryId;
        recurring.AccountId = req.AccountId;
        recurring.Note = req.Note?.Trim();
        recurring.Frequency = req.Frequency;
        recurring.IsActive = req.IsActive;
        if (req.StartDate > recurring.NextDueDate || !req.IsActive)
            recurring.NextDueDate = req.StartDate;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Pause or resume without touching the rest of the rule.</summary>
    [HttpPatch("{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, [FromBody] bool isActive)
    {
        var userId = User.GetUserId();
        var recurring = await db.Recurrings
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId && !r.IsDeleted);
        if (recurring is null) return NotFound();

        recurring.IsActive = isActive;
        if (!isActive) recurring.NextDueDate = DateOnly.MaxValue; // never fires while paused
        else if (recurring.NextDueDate == DateOnly.MaxValue)
            recurring.NextDueDate = DateOnly.FromDateTime(DateTime.Today);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = User.GetUserId();
        var recurring = await db.Recurrings
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId && !r.IsDeleted);
        if (recurring is null) return NotFound();

        recurring.IsDeleted = true;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Generates transactions for every active rule whose next due date has
    /// arrived (up to today), advancing each rule forward. Safe to call often.
    /// </summary>
    [HttpPost("run")]
    public async Task<ActionResult<RecurringRunResult>> Run()
    {
        var userId = User.GetUserId();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var dueRules = await db.Recurrings
            .Where(r => r.UserId == userId && r.IsActive && !r.IsDeleted && r.NextDueDate <= today)
            .OrderBy(r => r.NextDueDate)
            .ToListAsync();

        int created = 0;
        foreach (var rule in dueRules)
        {
            if (rule.CategoryId is null) continue; // cannot file a transaction without a category

            var guard = 0;
            while (rule.NextDueDate <= today && guard++ < MaxCatchUpPerRule)
            {
                db.Transactions.Add(new Transaction
                {
                    UserId = userId,
                    Amount = rule.Amount,
                    Type = rule.Type,
                    CategoryId = rule.CategoryId!.Value,
                    AccountId = rule.AccountId,
                    Description = string.IsNullOrWhiteSpace(rule.Note)
                        ? $"Recurring ({FrequencyLabel(rule.Frequency)})" : rule.Note,
                    Date = rule.NextDueDate.ToDateTime(TimeOnly.MinValue),
                    IsDeleted = false
                });
                created++;
                rule.NextDueDate = Advance(rule.NextDueDate, rule.Frequency);
            }
        }

        await db.SaveChangesAsync();
        return Ok(new RecurringRunResult(created, dueRules.Select(r => r.Id).ToList()));
    }

    private static DateOnly Advance(DateOnly date, byte frequency) => frequency switch
    {
        0 => date.AddDays(1),
        1 => date.AddDays(7),
        2 => date.AddMonths(1),
        4 => date.AddDays(14),
        _ => date.AddYears(1)
    };

    private static string FrequencyLabel(byte frequency) => frequency switch
    {
        0 => "Daily", 1 => "Weekly", 2 => "Monthly", 4 => "Every 2 weeks", _ => "Yearly"
    };
}
