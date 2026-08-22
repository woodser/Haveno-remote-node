## Remote Haveno node for the mobile app
This application requires <a href="https://adoptium.net/temurin/releases/?version=21" target="_blank">Java 21</a>

## About
Installs the Haveno daemon and starts a reverse proxy to translate grpc-web from the Haveno app. The daemon publishes the proxy as a hidden service on its own tor instance, so no port forwarding is required.

Currently works on windows, linux and macos

> [!note]
> This repository is configured for the public test network/stagenet. You will need to use a third party mainnet network to make real trades. The developers of this repository do not endorse any networks at this time.

## Network operators
The application can be built using GitHub actions and is set up to trigger a build whenever a new tag is pushed.

1. Set up your Haveno repo to build the daemon
	
	Haveno builds the daemon jars for every platform on release. macos clients need daemon-macos-x86_64.jar and daemon-macos-aarch64.jar, added by https://github.com/haveno-dex/haveno/commit/20970b976a and included in releases after v1.8.0. The daemon must also support --apiHiddenService, which publishes the reverse proxy as a hidden service.

2. Open Manta.Remote.csproj in a text editor

    | Property              | Value                                 
    |-----------------------|---------------------------------------
    | DaemonUrl             | Url of where the daemon.jar files are hosted. If you're using GitHub actions, use the url of the release that was created
    | Network               | Change this to XMR_MAINNET
	| HavenoAppName         | Change this to haveno-[SOMETHING]-node, this works the same as the app data directory for the desktop app

## Install
1. Download one of the releases or build from source
2. Unzip
3. chmod +x haveno-remote-node-[platform]
4. Install Java 21
5. Run haveno-remote-node-[platform]

### macos
Use the osx-arm64 release on Apple Silicon and osx-x64 on Intel. The releases are not notarized, so clear the quarantine flag after unzipping:

```
xattr -dr com.apple.quarantine haveno-remote-node-osx-arm64
```

Apple Silicon also needs Rosetta 2, since the daemon ships x86_64 monero binaries:

```
softwareupdate --install-rosetta --agree-to-license
```