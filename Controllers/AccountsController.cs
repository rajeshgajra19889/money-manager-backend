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
public class AccountsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AccountDto>>> GetAll()
    {
        var userId = User.GetUserId();

        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsDeleted)
            .OrderBy(a => a.Id)
            .ToListAsync();

        var flows = await db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsDeleted
                && (t.AccountId != null || t.TransferAccountId != null))
            .Select(t => new { t.AccountId, t.TransferAccountId, t.Type, t.Amount })
            .ToListAsync();

        return accounts.Select(a =>
        {
            decimal income = 0, expense = 0, transferOut = 0, transferIn = 0;
            foreach (var f in flows)
            {
                if (f.Type == 1 && f.AccountId == a.Id) income += f.Amount;
                else if (f.Type == 0 && f.AccountId == a.Id) expense += f.Amount;
                else if (f.Type == 2)
                {
                    if (f.AccountId == a.Id) transferOut += f.Amount;
                    if (f.TransferAccountId == a.Id) transferIn += f.Amount;
                }
            }
            var balance = a.InitialBalance + income - expense - transferOut + transferIn;
            return new AccountDto(a.Id, a.Name, a.Type, a.InitialBalance, balance, a.Kind, a.IncludeInTotal);
        }).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<AccountDto>> Create(SaveAccountRequest req)
    {
        var userId = User.GetUserId();
        var account = new Account { UserId = userId, Name = req.Name.Trim(), Type = req.Type, Kind = req.Kind, InitialBalance = req.InitialBalance, IncludeInTotal = req.IncludeInTotal };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return Ok(new AccountDto(account.Id, account.Name, account.Type, account.InitialBalance, account.InitialBalance, account.Kind, account.IncludeInTotal));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveAccountRequest req)
    {
        var userId = User.GetUserId();
        var account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId && !a.IsDeleted);
        if (account is null) return NotFound();

        account.Name = req.Name.Trim();
        account.Type = req.Type;
        account.Kind = req.Kind;
        account.InitialBalance = req.InitialBalance;
        account.IncludeInTotal = req.IncludeInTotal;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Renames an account group (or merges it into another when the target
    /// already exists) by moving every account of that user in one update.
    /// </summary>
    [HttpPut("group")]
    public async Task<IActionResult> UpdateGroup(UpdateAccountsGroupRequest req)
    {
        var userId = User.GetUserId();
        var from = (req.FromType ?? string.Empty).Trim();
        var to = (req.ToType ?? string.Empty).Trim();

        if (from.Length == 0 || to.Length == 0)
            return BadRequest(new { message = "A group name is required." });
        if (to.Length > 50)
            return BadRequest(new { message = "Group name is too long (max 50 characters)." });
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Pick a different group name." });

        var updated = await db.Accounts
            .Where(a => a.UserId == userId && !a.IsDeleted && a.Type == from)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Type, to));

        return Ok(new { updated });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = User.GetUserId();
        var account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId && !a.IsDeleted);
        if (account is null) return NotFound();

        // Soft delete: the account disappears from lists, pickers and worth
        // totals, while its past transactions keep resolving the name.
        account.IsDeleted = true;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
