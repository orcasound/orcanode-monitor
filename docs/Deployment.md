# Deployment

Orcanode Monitor is deployed to:

* https://orcanodemonitorstaging.azurewebsites.net/ on every merge to main.
* https://orcanodemonitor.azurewebsites.net/ when a matching version tag is pushed.

## Release tag format

Production deployment is triggered by pushing a tag that follows:

* `vMAJOR.MINOR.PATCH` (example: `v1.2.3`)

The release workflow tag filter is configured in `.github/workflows/release.yml`.

## Publish and release process

Both staging (`publish.yml`) and production (`release.yml`) workflows use the same high-level flow:

1. Restore, build, test, and publish the app.
2. Upload published output as the `webapp` artifact.
3. Download that artifact in the deploy job to `deployment-package`.
4. Recreate `deployment.zip` from `deployment-package\*` so app files are at the ZIP root.
5. Deploy to the Azure Web App using the publish profile secret.
6. Retry deployment up to 3 times with waits between attempts.

### Staging publish (main branch)

* Trigger: push to `main` (`.github/workflows/publish.yml`)
* Target app: `orcanodemonitorstaging`
* Publish profile secret: `OrcanodeMonitorStagingPublishProfile`

### Production release (version tags)

* Trigger: version tag push (`.github/workflows/release.yml`)
* Target app: `orcanodemonitor`
* Publish profile secret: `OrcanodeMonitorPublishProfile`

## Troubleshooting Deployment Failures

Log into the SCM site for the affected app (`https://orcanodemonitor.scm.azurewebsites.net/` for production or `https://orcanodemonitorstaging.scm.azurewebsites.net/` for staging) and go into a Debug console.

* Look under LogFiles/kudu for recent logs that may explain the issue
* Check whether one can create a file in D:\home.  If that fails, there is some disk space issue.
* Check GitHub Actions logs for the build, test, publish, and deploy steps (including retry attempt outcomes).

### Troubleshooting disk space issues

* Check free disk space with `df`
* Check whether one can create a file in D:\local
* Check the size of D:\home content

If creating a file under `home` fails but `local` succeeds:

* Log into portal.azure.com
* Select Quotas
* Select File system storage
