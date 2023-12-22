using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

public class GameController : ControllerBase
{
    private readonly IMongoDatabase _database;

    public GameController(IMongoClient client)
    {
        _database = client.GetDatabase("GameDatabase");
    }

    // Your action methods go here
}