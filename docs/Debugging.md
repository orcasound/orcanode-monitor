# Orcanode Monitor Debugging

## Debugging Startup Issues

### Method 1

1. Set the environment variable ASPNETCORE_ENVIRONMENT to "Development"
2. Save the environment variables which will restart the server
3. Navigate to the site

### Method 2

1. Go to the app service in [Azure portal](https://portal.azure.com/)
2. Click on "Console" on the left
3. Type in the exe name: OrcanodeMonitor.exe

### Method 3

1. Go to https://orcanodemonitor.scm.azurewebsites.net
2. Under the "Debug console" drop down menu, select "CMD"
3. Cd to C:\home\site\wwwroot
4. Type in: dotnet OrcanodeMonitor.dll

## Links

* [Production site](https://orcanodemonitor.azurewebsites.net/)
* [Staging site](https://orcanodemonitorstaging.azurewebsites.net/)
* [Azure portal](https://portal.azure.com/)
* [Blog on how to troubleshoot error 500.30](https://zimmergren.net/solving-asp-net-core-3-on-azure-app-service-causing-500-30-in-process-startup-failure/)
