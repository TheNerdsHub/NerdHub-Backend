using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using NerdHub_Backend.Models;

namespace NerdHub_Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QuoteCategoriesController : ControllerBase
    {
        private readonly IMongoCollection<QuoteCategory> _quoteCategories;
        private readonly ILogger<QuoteCategoriesController> _logger;

        public QuoteCategoriesController(IMongoClient client, ILogger<QuoteCategoriesController> logger)
        {
            _logger = logger;
            var database = client.GetDatabase("NH-Quotes");
            _quoteCategories = database.GetCollection<QuoteCategory>("quote-categories");
        }

        [HttpGet]
        [ProducesResponseType(200)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetQuoteCategories()
        {
            try
            {
                var categories = await _quoteCategories.Find(_ => true)
                    .SortByDescending(c => c.UpdatedAt)
                    .ToListAsync();
                return Ok(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving quote categories");
                return StatusCode(500, "An error occurred while retrieving quote categories");
            }
        }

        [HttpPost]
        [ProducesResponseType(201)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> CreateQuoteCategory([FromBody] QuoteCategory category)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(category.CategoryId))
                {
                    return BadRequest("Category ID cannot be empty");
                }

                // Check if category already exists for this guild
                var existing = await _quoteCategories.Find(c => 
                    c.GuildId == category.GuildId && c.CategoryId == category.CategoryId)
                    .FirstOrDefaultAsync();
                
                if (existing != null)
                {
                    // Update existing category
                    existing.CategoryName = category.CategoryName;
                    existing.UpdatedAt = DateTime.UtcNow;
                    
                    await _quoteCategories.ReplaceOneAsync(c => c.Id == existing.Id, existing);
                    return Ok(existing);
                }

                category.CreatedAt = DateTime.UtcNow;
                category.UpdatedAt = DateTime.UtcNow;
                await _quoteCategories.InsertOneAsync(category);
                
                return CreatedAtAction(nameof(GetQuoteCategories), new { id = category.Id }, category);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating quote category");
                return StatusCode(500, "An error occurred while creating the quote category");
            }
        }

        [HttpGet("guild/{guildId}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetQuoteCategoryByGuild(string guildId)
        {
            try
            {
                var category = await _quoteCategories.Find(c => c.GuildId == guildId)
                    .FirstOrDefaultAsync();
                
                if (category == null)
                {
                    return NotFound("No quote category found for this guild");
                }
                
                return Ok(category);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving quote category for guild: {GuildId}", guildId);
                return StatusCode(500, "An error occurred while retrieving quote category");
            }
        }

        [HttpDelete("guild/{guildId}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> DeleteQuoteCategoryByGuild(string guildId)
        {
            try
            {
                var result = await _quoteCategories.DeleteOneAsync(c => c.GuildId == guildId);
                if (result.DeletedCount == 0)
                {
                    return NotFound("Quote category not found for this guild");
                }
                
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting quote category for guild: {GuildId}", guildId);
                return StatusCode(500, "An error occurred while deleting the quote category");
            }
        }
    }
}
