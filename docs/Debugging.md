# Orcanode Monitor Debugging

## Debugging Startup Issues

### Method 1

1. Go to the app service in [Azure portal](https://portal.azure.com/).
2. Under "Settings" on the left, select "Environment variables".
3. Set the environment variable ASPNETCORE_ENVIRONMENT to "Development".
4. Save the environment variables which will restart the server if it is running.
5. Go back to "Overview" on the left, and click on the "Logs" tab below the Essentials section.
5. At the top, click "Start" if the service is currently Stopped.
6. Click "Configure logs"
7. Turn "Application logging (Filesystem)" on and click "Save".
8. Go back to the Logs tab and click "View log stream".  It should say "Connected!"
9. Navigate to the site

### Method 2

1. Go to the app service in [Azure portal](https://portal.azure.com/).
2. On the left under "Development Tools", click on "Console".  Note: steps 1-2 here seem to be equivalent to steps 1-3 in method 3.
3. Type in: OrcanodeMonitor.exe

### Method 3

1. Go to https://orcanodemonitor2.scm.azurewebsites.net.
2. Under the "Debug console" drop down menu, select "CMD".
3. Cd to C:\home\site\wwwroot.  Note: steps 1-3 here seem to be equivalent to steps 1-2 in method 2.
4. Type in: dotnet OrcanodeMonitor.dll

## Viewing Data

1. Launch Microsoft SQL Server Management Studio
2. Connect to Server
   * Server name: tcp:orcasound-server.database.windows.net,1433
   * Authentication: Microsoft Entra Default
   * User name: dthaler_messengeruser.com#EXT#@dthalermessengeruser.onmicrosoft.com

or to connect to the local database:

2. Connect to Server
   * Server name: (localdb)\mssqllocaldb
   * Authentication: Windows Authentication

## Links

* [Production site](https://orcanodemonitor2.azurewebsites.net/)
* [Staging site](https://orcanodemonitorstaging2.azurewebsites.net/)
* [Azure portal](https://portal.azure.com/)
* [Blog on how to troubleshoot error 500.30](https://zimmergren.net/solving-asp-net-core-3-on-azure-app-service-causing-500-30-in-process-startup-failure/)
