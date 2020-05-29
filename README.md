# Background process using MSAL cache from a web app

This sample shows a concept of how to reuse MSAL external token cache (from a web app) on a daemon application. 

With this approach, the daemon app can acquire an access token silently and consume Microsoft Graph for example, using only delegate permissions.
