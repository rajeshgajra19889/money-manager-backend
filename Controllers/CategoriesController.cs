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
public class CategoriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> GetAll([FromQuery] byte? type)
    {
        var userId = User.GetUserId();
        var query = db.Categories.AsNoTracking()
            .Where(c => c.UserId == userId && !c.IsDeleted)
            .OrderBy(c => c.Name).AsQueryable();
        if (type is not null) query = query.Where(c => c.Type == type);

        return await query.Select(c => new CategoryDto(
            c.Id, c.Name, c.Type, c.ParentId,
            db.Categories.Where(p => p.Id == c.ParentId && p.UserId == userId).Select(p => p.Name).FirstOrDefault()))
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(CreateCategoryRequest req)
    {
        var userId = User.GetUserId();
        var name = req.Name.Trim();
        if (name.Length == 0) return BadRequest(new { message = "Name is required." });

        int? parentId = req.ParentId;
        if (parentId is not null)
        {
            var parent = await db.Categories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == parentId && c.UserId == userId && !c.IsDeleted);
            if (parent is null) return BadRequest(new { message = "Parent category not found." });
            if (parent.ParentId is not null)
                return BadRequest(new { message = "Subcategories can only be one level deep." });
            if (parent.Type != req.Type)
                return BadRequest(new { message = "Subcategory must match the parent's category type." });
        }

        // Deleted rows don't count as duplicates, so a name can be reused.
        if (await db.Categories.AnyAsync(c =>
                c.UserId == userId && !c.IsDeleted && c.Name.ToLower() == name.ToLower()
                && c.Type == req.Type && c.ParentId == parentId))
            return BadRequest(new { message = "Category already exists." });

        var category = new Category { UserId = userId, Name = name, Type = req.Type, ParentId = parentId };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return Ok(new CategoryDto(category.Id, category.Name, category.Type, category.ParentId, null));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateCategoryRequest req)
    {
        var userId = User.GetUserId();
        // Scoped by owner: another user's category simply does not exist here.
        var category = await db.Categories
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId && !c.IsDeleted);
        if (category is null) return NotFound();

        var name = req.Name.Trim();
        if (name.Length == 0) return BadRequest(new { message = "Name is required." });
        if (req.ParentId == id)
            return BadRequest(new { message = "A category cannot be its own parent." });

        if (req.ParentId is not null)
        {
            var parent = await db.Categories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == req.ParentId && c.UserId == userId && !c.IsDeleted);
            if (parent is null) return BadRequest(new { message = "Parent category not found." });
            if (parent.ParentId is not null)
                return BadRequest(new { message = "Subcategories can only be one level deep." });
            if (parent.Type != category.Type)
                return BadRequest(new { message = "Subcategory must match the parent's category type." });
        }

        if (await db.Categories.AnyAsync(c =>
                c.Id != id && c.UserId == userId && !c.IsDeleted && c.Name.ToLower() == name.ToLower()
                && c.Type == category.Type && c.ParentId == req.ParentId))
            return BadRequest(new { message = "Category already exists." });

        category.Name = name;
        category.ParentId = req.ParentId;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = User.GetUserId();
        var category = await db.Categories
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId && !c.IsDeleted);
        if (category is null) return NotFound();

        // Soft delete: history (transactions/budgets/recurrings) keeps working,
        // the category just disappears from pickers and lists.
        var hasChildren = await db.Categories.AnyAsync(c => c.ParentId == id && !c.IsDeleted);
        if (hasChildren)
            return BadRequest(new { message = "Delete or move the subcategories first." });

        category.IsDeleted = true;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
