# Example 1-Background process using MSAL cache from a web app

This sample shows a concept of how to reuse MSAL external token cache (from a web app) on a daemon application. 

With this approach, the daemon app can acquire an access token silently and consume Microsoft Graph for example, using only delegate permissions.

We are considering that the applications are multi-tenant, but the concept also applies for single-tenant apps.

# Example 2-Background process using MSAL cache from an On-Behalf-Of flow call on a web api

The web api sample shows the same concept as the web app, however the access token been reused is from an OBO call to Graph that happens on the web api.

# Diagram

![Overview](./diagram.png)

## Required configuration

This sample is using SQL token cache leveraging the library **Microsoft.Extensions.Caching.Distributed**, so to create the necessary table, run the following command on the Package Manager Console, updating the connection string accordingly to your development environment:

```c#
dotnet tool install --global dotnet-sql-cache
dotnet sql-cache create "Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=MsalTokenCacheDatabase;Integrated Security=True;" dbo TokenCache
```

Then open a Command Prompt, navigate to the *WebApp* folder and run the Entity Framework migration scripts:

```ph
cd C:\<PathToTheProject>\BackgroundCacheRenew\WebApp
dotnet ef database update
```

Lastely, update the `appsettings.json` from the *WebApp* and *DeamonApp* with the values of your application registered in Azure AD. Also update the `TokenCacheDbConnStr` with your own connection string.

Note that **both apps must share the same clientId** for this sample to work. This applies to the web app and web api example.
