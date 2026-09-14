namespace ExpenseTracker.Api.Dtos;

public record LoginRequest(string UsernameOrEmail, string Password);
public record RegisterRequest(string Username, string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc, UserDto User);

public record UserDto(int Id, string Username, string Email, string Role,
    byte MonthStartDay = 1, string? IncomeColor = null, string? ExpenseColor = null);

public record SavePreferencesRequest(byte MonthStartDay, string? IncomeColor, string? ExpenseColor);

public record TransactionDto(int Id, decimal Amount, DateTime Date, string? Description,
    int? CategoryId, string? CategoryName, byte Type, int? AccountId, string? AccountName,
    int? TransferAccountId, string? TransferAccountName,
    bool HasReceipt, string? ReceiptUrl, bool IsBookmarked = false);

public record SaveTransactionRequest(decimal Amount, DateTime Date, string? Description,
    int? CategoryId, byte Type, int? AccountId, int? TransferAccountId, string? ReceiptUrl);

public record InstallmentRequest(decimal Amount, DateTime Date, int Months, string? Description,
    int? CategoryId, byte Type, int? AccountId);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public record AccountDto(int Id, string Name, string Type, decimal InitialBalance, decimal Balance, byte Kind = 0, bool IncludeInTotal = true);
public record SaveAccountRequest(string Name, string Type, decimal InitialBalance, byte Kind = 0, bool IncludeInTotal = true);

public record UpdateAccountsGroupRequest(string FromType, string ToType);

public record CategoryDto(int Id, string Name, byte Type, int? ParentId = null, string? ParentName = null);
public record CreateCategoryRequest(string Name, byte Type, int? ParentId = null);
public record UpdateCategoryRequest(string Name, int? ParentId = null);

public record BudgetDto(int Id, decimal MonthlyLimit, int Month, int Year, int CategoryId, string CategoryName, decimal Spent);
public record SaveBudgetRequest(decimal MonthlyLimit, int Month, int Year, int CategoryId);

/// <summary>Summary for an arbitrary period (month, year or custom range).</summary>
public record PeriodSummaryDto(DateTime From, DateTime To, decimal TotalIncome, decimal TotalExpense, decimal NetBalance);
public record MonthSummaryDto(int Month, int Year, decimal TotalIncome, decimal TotalExpense, decimal NetBalance);
public record CategoryBreakdownDto(int CategoryId, string CategoryName, decimal Total, int Count, double Percent);
public record DailyTrendDto(DateTime Date, decimal Income, decimal Expense);
public record TrendPointDto(string Label, decimal Income, decimal Expense);
public record AssetPointDto(int Year, int Month, string Label, decimal Total);

public record RecurringDto(int Id, byte Type, decimal Amount, int? CategoryId, string? CategoryName,
    int? AccountId, string? AccountName, string? Note, byte Frequency,
    DateOnly StartDate, DateOnly NextDueDate, bool IsActive);

public record SaveRecurringRequest(decimal Amount, byte Type, int? CategoryId, int? AccountId,
    string? Note, byte Frequency, DateOnly StartDate, bool IsActive = true);

public record RecurringRunResult(int Created, IReadOnlyList<int> RecurringIds);
