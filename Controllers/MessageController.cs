using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using NerdHub.Models;

namespace NerdHub.Controllers {

    [ApiController]
    [Route("[controller]")]
    public class MessageController : ControllerBase
    {
        private readonly IMongoCollection<Message> _messages;

        public MessageController(IMongoClient client)
        {
            var database = client.GetDatabase("NerdHub-Discord");
            _messages = database.GetCollection<Message>("quotes");
        }

        [HttpPost]
        public IActionResult Post([FromBody] Message message)
        {
            _messages.InsertOne(message);
            return Ok();
        }
    }
}