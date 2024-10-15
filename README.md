# orcanode-monitor

[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/orcasound/orcanode-monitor/badge)](https://scorecard.dev/viewer/?uri=github.com/orcasound/orcanode-monitor)

[Orcanode Monitor](https://orcanodemonitor2.azurewebsites.net/) is a web service for monitoring liveness of [orcanode](https://github.com/orcasound/orcanode) audio streaming.

The troubleshooting guide at
[Administration of network nodes](https://github.com/orcasound/orcanode/wiki/Administration-of-network-nodes#general-trouble-shooting-strategies-for-orcasound-nodes)
explains how to manually check orcanode liveness.  This repository holds source code for the Azure web service
that monitors the availability of orcanodes, provides a dashboard of their state and uptime
history, and allows real-time notifications via many mechanisms (using [IFTTT](https://ifttt.com)) when a problem is detected.

For more details, see the [Design note](docs/Design.md).

For troubleshooting info, see the [Debugging note](docs/Debugging.md).

## Contributing

Please read [CONTRIBUTING.md](CONTRIBUTING.md) for details on our code of conduct, and the process for
submitting pull requests.
