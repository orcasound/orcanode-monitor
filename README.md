# orcanode-monitor

Orcanode Monitor is a web service for monitoring liveness of [orcanode](https://github.com/orcasound/orcanode) audio streaming.

The troubleshooting guide at
[Administration of network nodes](https://github.com/orcasound/orcanode/wiki/Administration-of-network-nodes#general-trouble-shooting-strategies-for-orcasound-nodes)
explains how to manually check orcanode liveness.  This repository holds source code for an Azure web service
that monitors the availability of orcanodes, provides a dashboard of their state (and, in the future, uptime
history), and allows real-time notifications via many mechanisms when a problem is detected.

For more details, see the [Design note](docs/Design.md).

## Contributing

Please read [CONTRIBUTING.md](docs/CONTRIBUTING.md) for details on our code of conduct, and the process for
submitting pull requests.
