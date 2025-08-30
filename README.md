# NerdHub-Backend

This repository contains the .NET 8 backend for the NerdHub application. It serves as a RESTful API for managing game data, user information, and application versioning.

## Features

- **Game Management**: Endpoints for fetching and managing game details.
- **Steam Integration**: Integrates with the Steam Web API to retrieve comprehensive game information.
- **Data Storage**: Uses MongoDB to store game data and user mappings.
- **Versioning**: An endpoint to check the current version of the API.

## Key Files

- `Controllers/GamesController.cs`: Manages all game-related API endpoints.
- `Services/SteamService.cs`: Handles all interactions with the Steam Web API.
- `Models/GameDetails.cs`: Defines the data model for game details.
- `Program.cs`: The main entry point and configuration for the application.

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A running MongoDB instance
- A Steam Web API Key

### Configuration

1.  Create a `.env` file in the root of the project.
2.  Add the following environment variables to the `.env` file:
    ```
    MONGODB_CONNECTION_STRING="your-mongo-connection-string"
    STEAM_API_KEY="your-steam-api-key"
    VERSION="dev-prerelease"
    ```

### Running Locally

To run the backend service locally, execute the following command from the root of the repository:

```sh
dotnet run
```

The API will be available at `https://localhost:5172`.