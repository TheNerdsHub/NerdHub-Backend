This is the .NET 8 backend for the NerdHub application.

- It's a RESTful API for games, users, and versioning.
- It uses MongoDB for data storage (game data, user mappings).
- It integrates with the Steam Web API to get game details.
- Key files:
  - `Controllers/GamesController.cs`: Manages game-related endpoints.
  - `Services/SteamService.cs`: Interacts with the Steam API.
  - `Models/GameDetails.cs`: The data model for games.
- To run: `dotnet run`. The API is at http://localhost:5000.
- Use `dotnet test` to run tests.
