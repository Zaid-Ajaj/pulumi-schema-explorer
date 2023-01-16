# Pulumi Schema Explorer [![Nuget](https://img.shields.io/nuget/v/PulumiSchemaExplorer.svg?maxAge=0&colorB=brightgreen)](https://www.nuget.org/packages/PulumiSchemaExplorer)

A Pulumi schema explorer built as a full stack web application in F# :heart:

Building it live at Twitch https://www.twitch.tv/zaid_ajaj (Posting later to YouTube)

## Installtion & Usage
Built as dotnet CLI tool you can install:

```bash
dotnet tool install -g PulumiSchemaExplorer
```
once you have the tool installed, you can run as follows in your terminal:
```bash
pulumi-schema-explorer
```
and then navigate to `http://localhost:5000` which will show you the schema explorer

## Development

To run the project locally, you need to have the following installed:
 - Dotnet SDK v6.x
 - Nodejs v18.x or later
 - Pulumi CLI (preferably latest)

To run the project locally, you can run the following commands:
```bash
dotnet run
```

### Install the `pulumi-schema-explorer` tool locally
```bash
dotnet run LocalInstall
```
or to uninstall it
```bash
dotnet run LocalUninstall
```