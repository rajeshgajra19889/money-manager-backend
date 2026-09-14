# Money Manager Backend

A RESTful API for managing personal finances built with ASP.NET Core and PostgreSQL.

## Features

- **Authentication** - User registration & login with JWT tokens
- **Transactions** - Track income and expenses
- **Accounts** - Manage multiple accounts
- **Categories** - Organize transactions by categories
- **Budgets** - Set and monitor spending limits
- **Recurring Transactions** - Automate repeating entries
- **Statistics** - View spending insights and summaries

## Tech Stack

- .NET 10
- PostgreSQL
- Entity Framework Core
- JWT Authentication

## Getting Started

1. Install [.NET 10 SDK](https://dotnet.microsoft.com/download)
2. Install PostgreSQL
3. Update `appsettings.json` with your database credentials and JWT secret
4. Run the application:
   ```bash
   dotnet run
   ```

The API will start on `http://localhost:5000`.

## Configuration

Add your secrets to `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=ExpenseTracker;Username=postgres;Password=YOUR_PASSWORD"
  },
  "Jwt": {
    "Issuer": "ExpenseTracker.Api",
    "Audience": "ExpenseTracker.Client",
    "Key": "YOUR_JWT_SECRET_KEY",
    "AccessTokenMinutes": 720
  }
}
```

## License

MIT
