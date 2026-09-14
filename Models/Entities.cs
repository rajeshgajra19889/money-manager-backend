using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExpenseTracker.Api.Models;

[Table("Users")]
public class User
{
    public int Id { get; set; }

    [MaxLength(450)]
    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = "User";

    [MaxLength(-1)]
    public string? RefreshToken { get; set; }

    public DateTime? RefreshTokenExpiry { get; set; }

    /// <summary>Day of month (1-28) the user's financial month starts on.</summary>
    public byte MonthStartDay { get; set; } = 1;

    [MaxLength(9)]
    public string? IncomeColor { get; set; }

    [MaxLength(9)]
    public string? ExpenseColor { get; set; }
}

[Table("Categories")]
public class Category
{
    public int Id { get; set; }
    public int UserId { get; set; } // owner — every user has a private category list
    public string Name { get; set; } = string.Empty;
    public byte Type { get; set; } // 0 = Expense, 1 = Income
    public int? ParentId { get; set; } // set => subcategory (one level deep)
    public bool IsDeleted { get; set; } // soft delete: hidden from pickers, kept for history
}

[Table("Expenses")]
public class Transaction
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    [MaxLength(-1)]
    public string? Description { get; set; }
    public int? CategoryId { get; set; } // null for transfers
    public int UserId { get; set; }
    public bool IsDeleted { get; set; }
    [MaxLength(-1)]
    public string? ReceiptUrl { get; set; }
    public byte Type { get; set; } // 0 = Expense, 1 = Income, 2 = Transfer
    public int? AccountId { get; set; } // transfer source when Type == 2
    public int? TransferAccountId { get; set; } // transfer destination when Type == 2
    public bool IsBookmarked { get; set; }
}

[Table("Budgets")]
public class Budget
{
    public int Id { get; set; }
    public decimal MonthlyLimit { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public int CategoryId { get; set; }
    public int UserId { get; set; }
    public bool IsDeleted { get; set; } // soft delete: hidden from the budgets page, resurrectable
}

[Table("Accounts")]
public class Account
{
    public int Id { get; set; }
    public int UserId { get; set; }
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(50)]
    public string Type { get; set; } = "Cash";
    /// <summary>0 = Debit/asset, 1 = Credit/liability (credit card, loan).</summary>
    public byte Kind { get; set; }
    public decimal InitialBalance { get; set; }
    public bool IsArchived { get; set; }
    /// <summary>When false the account stays listed but is excluded from asset/liability worth totals.</summary>
    public bool IncludeInTotal { get; set; } = true;
    public bool IsDeleted { get; set; } // soft delete: hidden from lists/pickers, kept for history
}

[Table("RecurringTransactions")]
public class Recurring
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public byte Type { get; set; } // 0 = Expense, 1 = Income
    public decimal Amount { get; set; }
    public int? CategoryId { get; set; }
    public int? AccountId { get; set; }
    [MaxLength(200)]
    public string? Note { get; set; }

    /// <summary>0 = Daily, 1 = Weekly, 2 = Monthly, 3 = Yearly.</summary>
    public byte Frequency { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly NextDueDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } // soft delete: hidden from the recurring list, never fires
}
