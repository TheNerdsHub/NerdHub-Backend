using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using NerdHub.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NerdHub.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MessageController : ControllerBase
    {
        private readonly IMongoCollection<Message> _messages;

        public MessageController(IMongoClient client)
        {
            var database = client.GetDatabase("NerdHub-Discord");
            _messages = database.GetCollection<Message>("quotes");
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] Message message)
        {
            if (message == null)
            {
                return BadRequest("Message cannot be null.");
            }

            await _messages.InsertOneAsync(message);
            return Ok(message);
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var messages = await _messages.Find(_ => true).ToListAsync();
            return Ok(messages);
        }

        [HttpGet("{id:length(24)}", Name = "GetMessage")]
        public async Task<IActionResult> Get(string id)
        {
            var message = await _messages.Find(m => m.Id.ToString() == id).FirstOrDefaultAsync();

            if (message == null)
            {
                return NotFound();
            }

            return Ok(message);
        }

        [HttpPut("{id:length(24)}")]
        public async Task<IActionResult> Put(string id, [FromBody] Message updatedMessage)
        {
            if (updatedMessage == null || updatedMessage.Id.ToString() != id)
            {
                return BadRequest("Invalid message data.");
            }

            var result = await _messages.ReplaceOneAsync(m => m.Id.ToString() == id, updatedMessage);

            if (result.MatchedCount == 0)
            {
                return NotFound();
            }

            return Ok(updatedMessage);
        }

        [HttpDelete("{id:length(24)}")]
        public async Task<IActionResult> Delete(string id)
        {
            var result = await _messages.DeleteOneAsync(m => m.Id.ToString() == id);

            if (result.DeletedCount == 0)
            {
                return NotFound();
            }

            return Ok();
        }
    }
}