# Money Manager Backend

![CI](https://github.com/rajeshgajra19889/money-manager-backend/actions/workflows/ci.yml/badge.svg)
![.NET](https://img.shields.io/badge/.NET-10.0-blue)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-4169E1?logo=postgresql&logoColor=white)
![Entity Framework](https://img.shields.io/badge/Entity%20Framework%20Core-8B5CF6)
![JWT](https://img.shields.io/badge/JWT-Authentication-black?logo=jsonwebtokens&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-green)

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

## Deployment

- **Backend** — deployed on [Render](https://render.com) via the included `Dockerfile`. Config comes from env vars (`ConnectionStrings__Default`, `Jwt__*`).
- **Database** — PostgreSQL on [Neon](https://neon.tech).
- **Frontend** — Angular client deployed as a static site on Render. Live at [money-manager-client-492h.onrender.com](https://money-manager-client-492h.onrender.com).

## License

MIT
