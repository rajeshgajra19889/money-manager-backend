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
public class StatsController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Resolves the query window. Explicit from/to wins; otherwise the given
    /// (or current) month, honouring the user's custom month start day.
    /// </summary>
    private async Task<(DateTime Start, DateTime EndInclusive)> ResolvePeriod(
        int userId, int? month, int? year, DateTime? from, DateTime? to)
    {
        if (from is not null || to is not null)
        {
            var s = (from ?? DateTime.MinValue).Date;
            var e = (to ?? DateTime.MaxValue.Date).Date;
            if (e < s) e = s;
            return (s, e);
        }

        var y = year is > 0 ? year.Value : DateTime.Today.Year;
        var m = month is > 0 ? month.Value : DateTime.Today.Month;

        var msd = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.MonthStartDay)
            .FirstOrDefaultAsync();
        msd = Math.Clamp(msd == 0 ? (byte)1 : msd, (byte)1, (byte)28);

        var start = new DateTime(y, m, msd == 1 ? 1 : msd);
        return (start, start.AddMonths(1).AddDays(-1));
    }

    private IQueryable<Transaction> PeriodQuery(int userId, DateTime start, DateTime endInclusive, byte? type = null)
    {
        var endExclusive = endInclusive.AddDays(1);
        var query = db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsDeleted && t.Date >= start && t.Date < endExclusive);
        if (type is not null) query = query.Where(t => t.Type == type.Value);
        return query;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<PeriodSummaryDto>> Summary(
        [FromQuery] int? month, [FromQuery] int? year,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var userId = User.GetUserId();
        var (start, end) = await ResolvePeriod(userId, month, year, from, to);
        var query = PeriodQuery(userId, start, end);

        var income = await query.Where(t => t.Type == 1).SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var expense = await query.Where(t => t.Type == 0).SumAsync(t => (decimal?)t.Amount) ?? 0m;

        return new PeriodSummaryDto(start, end, income, expense, income - expense);
    }

    [HttpGet("by-category")]
    public async Task<ActionResult<IEnumerable<CategoryBreakdownDto>>> ByCategory(
        [FromQuery] int? month, [FromQuery] int? year,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] byte type = 0)
    {
        var userId = User.GetUserId();
        var (start, end) = await ResolvePeriod(userId, month, year, from, to);
        var query = PeriodQuery(userId, start, end, type);

        var rows = await query
            .Where(t => t.CategoryId != null)
            .GroupBy(t => t.CategoryId)
            .Select(g => new
            {
                CategoryId = g.Key!.Value,
                Total = g.Sum(t => t.Amount),
                Count = g.Count()
            })
            .ToListAsync();

        var names = await db.Categories.ToDictionaryAsync(c => c.Id, c => c.Name);
        var grandTotal = rows.Sum(r => r.Total);

        var result = rows
            .Select(r => new CategoryBreakdownDto(
                r.CategoryId,
                names.GetValueOrDefault(r.CategoryId, "Unknown"),
                r.Total,
                r.Count,
                grandTotal > 0 ? Math.Round((double)(r.Total / grandTotal) * 100, 1) : 0))
            .OrderByDescending(r => r.Total)
            .ToList();

        return result;
    }

    [HttpGet("trend")]
    public async Task<ActionResult<IEnumerable<TrendPointDto>>> Trend(
        [FromQuery] int? month, [FromQuery] int? year,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string granularity = "day")
    {
        var userId = User.GetUserId();
        var (start, end) = await ResolvePeriod(userId, month, year, from, to);

        var isMonthly = granularity == "month";
        var raw = isMonthly
            ? await PeriodQuery(userId, start, end)
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .Select(g => new TrendRaw(null, g.Key.Year, g.Key.Month,
                    g.Where(t => t.Type == 1).Sum(t => (decimal?)t.Amount) ?? 0m,
                    g.Where(t => t.Type == 0).Sum(t => (decimal?)t.Amount) ?? 0m))
                .ToListAsync()
            : await PeriodQuery(userId, start, end)
                .GroupBy(t => t.Date.Date)
                .Select(g => new TrendRaw((DateTime?)g.Key, 0, 0,
                    g.Where(t => t.Type == 1).Sum(t => (decimal?)t.Amount) ?? 0m,
                    g.Where(t => t.Type == 0).Sum(t => (decimal?)t.Amount) ?? 0m))
                .ToListAsync();

        var points = new List<TrendPointDto>();

        if (isMonthly)
        {
            var map = raw.Where(r => r.Day is null).ToDictionary(r => $"{r.Year}-{r.Month}", r => r);
            var cursor = new DateTime(start.Year, start.Month, 1);
            while (cursor <= end)
            {
                var row = map.GetValueOrDefault($"{cursor.Year}-{cursor.Month}");
                points.Add(new TrendPointDto(cursor.ToString("MMM yy"), row?.Income ?? 0m, row?.Expense ?? 0m));
                cursor = cursor.AddMonths(1);
            }
        }
        else
        {
            var dayMap = raw.Where(r => r.Day is not null).ToDictionary(r => r.Day!.Value, r => r);
            var days = (end - start).Days;
            if (days > 400) days = 400; // safety cap
            for (var d = 0; d <= days; d++)
            {
                var date = start.AddDays(d);
                var row = dayMap.GetValueOrDefault(date);
                points.Add(new TrendPointDto(date.ToString("d MMM"), row?.Income ?? 0m, row?.Expense ?? 0m));
            }
        }

        return points;
    }

    private sealed record TrendRaw(DateTime? Day, int Year, int Month, decimal Income, decimal Expense);

    [HttpGet("asset-trend")]
    public async Task<ActionResult<IEnumerable<AssetPointDto>>> AssetTrend([FromQuery] int months = 6)
    {
        var userId = User.GetUserId();
        months = Math.Clamp(months, 1, 24);

        var initialSum = await db.Accounts.AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsDeleted)
            .SumAsync(a => (decimal?)a.InitialBalance) ?? 0m;

        var today = DateTime.Today;
        var start = new DateTime(today.Year, today.Month, 1).AddMonths(-(months - 1));

        var nets = await db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsDeleted && t.Date < start.AddMonths(months))
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Net = g.Sum(t => t.Type == 1 ? t.Amount : t.Type == 0 ? -t.Amount : 0m) // transfers cancel out
            })
            .ToDictionaryAsync(x => (x.Year, x.Month), x => x.Net);

        var result = new List<AssetPointDto>();

        // Start from opening balances plus every flow recorded before the window
        var cumulative = initialSum
            + nets.Where(kv => kv.Key.Year < start.Year || (kv.Key.Year == start.Year && kv.Key.Month < start.Month))
                .Sum(kv => kv.Value);

        for (var i = 0; i < months; i++)
        {
            var monthStart = start.AddMonths(i);
            if (monthStart > today) break;

            cumulative += nets.GetValueOrDefault((monthStart.Year, monthStart.Month));

            result.Add(new AssetPointDto(
                monthStart.Year, monthStart.Month,
                monthStart.ToString("MMM"),
                Math.Round(cumulative, 2)));
        }

        return result;
    }
}
