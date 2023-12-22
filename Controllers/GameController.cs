using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using NerdHub.Models;

namespace NerdHub.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class GamesController : ControllerBase
    {
        [HttpGet]
        public IEnumerable<Game> Get()
        {
            var games = new List<Game>
            {
                new Game { Id = "1", Name = "Game 1", Description = "Description 1" },
                new Game { Id = "2", Name = "Game 2", Description = "Description 2" }
            };

            return games;
        }
    }
}