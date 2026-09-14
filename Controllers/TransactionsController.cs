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
public class TransactionsController(AppDbContext db) : ControllerBase
{
    private const int MaxReceiptLength = 700_000; // ~500KB image as base64 data URL

    [HttpGet]
    public async Task<ActionResult<PagedResult<TransactionDto>>> GetAll(
        [FromQuery] int? month, [FromQuery] int? year,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] byte? type,
        [FromQuery] int? accountId, [FromQuery] int? categoryId,
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var userId = User.GetUserId();
        var query = db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsDeleted);

        if (from is not null || to is not null)
        {
            // Custom period overrides month/year
            var rangeStart = (from ?? DateTime.MinValue).Date;
            var rangeEnd = (to ?? DateTime.MaxValue.Date).Date.AddDays(1);
            query = query.Where(t => t.Date >= rangeStart && t.Date < rangeEnd);
        }
        else if (month is > 0 && year is > 0)
        {
            var start = new DateTime(year.Value, month.Value, 1);
            var end = start.AddMonths(1);
            query = query.Where(t => t.Date >= start && t.Date < end);
        }
        if (type is not null) query = query.Where(t => t.Type == type);
        if (accountId is not null) query = query.Where(t => t.AccountId == accountId);
        if (categoryId is not null) query = query.Where(t => t.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(t => t.Description!.ToLower().Contains(term)
                || db.Categories.Any(c => c.Id == t.CategoryId && c.Name.ToLower().Contains(term)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionDto(
                t.Id, t.Amount, t.Date, t.Description,
                t.CategoryId,
                db.Categories.Where(c => c.Id == t.CategoryId).Select(c => c.Name).FirstOrDefault(),
                t.Type, t.AccountId,
                db.Accounts.Where(a => a.Id == t.AccountId).Select(a => (string?)a.Name).FirstOrDefault(),
                t.TransferAccountId,
                db.Accounts.Where(a => a.Id == t.TransferAccountId).Select(a => (string?)a.Name).FirstOrDefault(),
                t.ReceiptUrl != null, null, t.IsBookmarked))
            .ToListAsync();

        return new PagedResult<TransactionDto>(items, page, pageSize, total);
    }

    /// <summary>Every bookmarked transaction across all months, newest first.</summary>
    [HttpGet("bookmarks")]
    public async Task<ActionResult<PagedResult<TransactionDto>>> Bookmarks(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 200)
    {
        var userId = User.GetUserId();
        pageSize = Math.Clamp(pageSize, 1, 500);
        var query = db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsDeleted && t.IsBookmarked);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionDto(
                t.Id, t.Amount, t.Date, t.Description,
                t.CategoryId,
                db.Categories.Where(c => c.Id == t.CategoryId).Select(c => c.Name).FirstOrDefault(),
                t.Type, t.AccountId,
                db.Accounts.Where(a => a.Id == t.AccountId).Select(a => (string?)a.Name).FirstOrDefault(),
                t.TransferAccountId,
                db.Accounts.Where(a => a.Id == t.TransferAccountId).Select(a => (string?)a.Name).FirstOrDefault(),
                t.ReceiptUrl != null, null, true))
            .ToListAsync();

        return new PagedResult<TransactionDto>(items, page, pageSize, total);
    }

    /// <summary>Toggles the bookmark flag on a transaction.</summary>
    [HttpPost("{id:int}/bookmark")]
    public async Task<IActionResult> ToggleBookmark(int id)
    {
        var userId = User.GetUserId();
        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId && !t.IsDeleted);
        if (tx is null) return NotFound();

        tx.IsBookmarked = !tx.IsBookmarked;
        await db.SaveChangesAsync();
        return Ok(new { isBookmarked = tx.IsBookmarked });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TransactionDto>> GetOne(int id)
    {
        var userId = User.GetUserId();
        var tx = await db.Transactions.AsNoTracking()
            .Where(t => t.Id == id && t.UserId == userId && !t.IsDeleted)
            .Select(t => new TransactionDto(
                t.Id, t.Amount, t.Date, t.Description,
                t.CategoryId,
                db.Categories.Where(c => c.Id == t.CategoryId).Select(c => c.Name).FirstOrDefault(),
                t.Type, t.AccountId,
                db.Accounts.Where(a => a.Id == t.AccountId).Select(a => (string?)a.Name).FirstOrDefault(),
                t.TransferAccountId,
                db.Accounts.Where(a => a.Id == t.TransferAccountId).Select(a => (string?)a.Name).FirstOrDefault(),
                t.ReceiptUrl != null, t.ReceiptUrl, t.IsBookmarked))
            .FirstOrDefaultAsync();
        return tx is null ? NotFound() : Ok(tx);
    }

    [HttpPost]
    public async Task<ActionResult<TransactionDto>> Create(SaveTransactionRequest req)
    {
        var userId = User.GetUserId();
        var validation = Validate(req);
        if (validation is not null) return BadRequest(new { message = validation });

        var tx = new Transaction
        {
            Amount = req.Amount,
            Date = req.Date,
            Description = req.Description?.Trim(),
            CategoryId = req.CategoryId,
            Type = req.Type,
            AccountId = req.AccountId,
            TransferAccountId = req.Type == 2 ? req.TransferAccountId : null,
            ReceiptUrl = string.IsNullOrWhiteSpace(req.ReceiptUrl) ? null : TruncateReceipt(req.ReceiptUrl),
            UserId = userId,
            IsDeleted = false
        };
        db.Transactions.Add(tx);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetOne), new { id = tx.Id }, tx.Id);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveTransactionRequest req)
    {
        var validation = Validate(req);
        if (validation is not null) return BadRequest(new { message = validation });
        var userId = User.GetUserId();

        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId && !t.IsDeleted);
        if (tx is null) return NotFound();

        tx.Amount = req.Amount;
        tx.Date = req.Date;
        tx.Description = req.Description?.Trim();
        tx.CategoryId = req.Type == 2 ? null : req.CategoryId;
        tx.Type = req.Type;
        tx.AccountId = req.AccountId;
        tx.TransferAccountId = req.Type == 2 ? req.TransferAccountId : null;
        if (!string.IsNullOrWhiteSpace(req.ReceiptUrl)) tx.ReceiptUrl = TruncateReceipt(req.ReceiptUrl);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static string? Validate(SaveTransactionRequest req)
    {
        if (req.Amount <= 0) return "Amount must be greater than zero.";
        if (req.Type > 2) return "Invalid transaction type.";
        if (req.Type == 2)
        {
            if (req.AccountId is null || req.TransferAccountId is null)
                return "A transfer needs both a source and a destination account.";
            if (req.AccountId == req.TransferAccountId)
                return "Source and destination accounts must be different.";
        }
        else if (req.CategoryId is null)
        {
            return "Please pick a category.";
        }
        return null;
    }

    /// <summary>
    /// Splits a total into equal monthly installments, each stored as a real
    /// transaction dated one month apart (the last one absorbs rounding).
    /// </summary>
    [HttpPost("installment")]
    public async Task<IActionResult> CreateInstallment(InstallmentRequest req)
    {
        var userId = User.GetUserId();
        if (req.Amount <= 0) return BadRequest(new { message = "Amount must be greater than zero." });
        if (req.Months < 1 || req.Months > 60) return BadRequest(new { message = "Installments must be between 1 and 60 months." });
        if (req.Type > 1) return BadRequest(new { message = "Installments support expense and income only." });
        if (req.CategoryId is null) return BadRequest(new { message = "Please pick a category." });

        var per = Math.Round(req.Amount / req.Months, 2, MidpointRounding.AwayFromZero);
        var lastAmount = req.Amount - per * (req.Months - 1);
        var baseNote = string.IsNullOrWhiteSpace(req.Description) ? "Installment" : req.Description!.Trim();

        for (var i = 0; i < req.Months; i++)
        {
            db.Transactions.Add(new Transaction
            {
                UserId = userId,
                Amount = i == req.Months - 1 ? lastAmount : per,
                Type = req.Type,
                CategoryId = req.CategoryId,
                AccountId = req.AccountId,
                Description = $"{baseNote} ({i + 1}/{req.Months})",
                Date = req.Date.AddMonths(i),
                IsDeleted = false
            });
        }
        await db.SaveChangesAsync();
        return Ok(new { created = req.Months });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = User.GetUserId();
        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId && !t.IsDeleted);
        if (tx is null) return NotFound();

        tx.IsDeleted = true;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static string TruncateReceipt(string url) =>
        url.Length <= MaxReceiptLength ? url : url[..MaxReceiptLength];
}
