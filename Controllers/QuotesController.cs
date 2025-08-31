using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using NerdHub.Models;

namespace NerdHub.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QuotesController : ControllerBase
    {
        private readonly IMongoCollection<Quote> _quotes;
        private readonly ILogger<QuotesController> _logger;

        public QuotesController(IMongoClient client, ILogger<QuotesController> logger)
        {
            _logger = logger;
            var database = client.GetDatabase("NH-Quotes");
            _quotes = database.GetCollection<Quote>("quotes");
        }

        [HttpGet]
        [ProducesResponseType(200)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetAllQuotes()
        {
            try
            {
                var quotes = await _quotes.Find(_ => true)
                    .SortByDescending(q => q.Timestamp)
                    .ToListAsync();
                return Ok(quotes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving quotes");
                return StatusCode(500, "An error occurred while retrieving quotes");
            }
        }

        [HttpGet("random")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetRandomQuote()
        {
            try
            {
                var count = await _quotes.CountDocumentsAsync(_ => true);
                if (count == 0)
                {
                    return NotFound("No quotes found");
                }

                var random = new Random();
                var skip = random.Next((int)count);
                var quote = await _quotes.Find(_ => true).Skip(skip).Limit(1).FirstOrDefaultAsync();
                
                return Ok(quote);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving random quote");
                return StatusCode(500, "An error occurred while retrieving random quote");
            }
        }

        [HttpGet("daily")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetQuoteOfTheDay()
        {
            try
            {
                var today = DateTime.UtcNow.Date;
                var seed = today.GetHashCode();
                var random = new Random(seed);

                var count = await _quotes.CountDocumentsAsync(_ => true);
                if (count == 0)
                {
                    return NotFound("No quotes found");
                }

                var skip = random.Next((int)count);
                var quote = await _quotes.Find(_ => true).Skip(skip).Limit(1).FirstOrDefaultAsync();
                
                return Ok(quote);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving quote of the day");
                return StatusCode(500, "An error occurred while retrieving quote of the day");
            }
        }

        [HttpPost]
        [ProducesResponseType(201)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> CreateQuote([FromBody] Quote quote)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(quote.QuoteText))
                {
                    return BadRequest("Quote text cannot be empty");
                }

                quote.Timestamp = DateTime.UtcNow;
                await _quotes.InsertOneAsync(quote);
                
                return CreatedAtAction(nameof(GetAllQuotes), new { id = quote.Id }, quote);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating quote");
                return StatusCode(500, "An error occurred while creating the quote");
            }
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> DeleteQuote(string id)
        {
            try
            {
                var result = await _quotes.DeleteOneAsync(q => q.Id == id);
                if (result.DeletedCount == 0)
                {
                    return NotFound("Quote not found");
                }
                
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting quote with ID: {Id}", id);
                return StatusCode(500, "An error occurred while deleting the quote");
            }
        }
    }
}
