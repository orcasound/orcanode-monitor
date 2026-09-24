# Deployment

Orcanode Monitor is deployed to:

* https://orcanodemonitorstaging.azurewebsites.net/ on every merge to main.
* https://orcanodemonitor.azurewebsites.net/ when a version tag is pushed to main.

## Troubleshooting Deployment Failures

Log into https://orcanodemonitor.scm.azurewebsites.net/ and go into a Debug console.

* Look under LogFiles/kudu for recent logs that may explain the issue
* Check whether one can create a file in D:\home.  If that fails, there is some disk space issue.

### Troubleshooting disk space issues

* Check free disk space with `df`
* Check whether one can create a file in D:\local
* Check the size of D:\home content

If creating a file under `home` fails but `local` succeeds:

* Log into portal.azure.com
* Select Quotas
* Select File system storage
