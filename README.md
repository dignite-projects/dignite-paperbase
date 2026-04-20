# Dignite Paperbase

A modular, extensible ABP-based application for paperless workflows.

## Solution Structure

```
dignite-paperbase/
├── core/       # Core ABP module — domain models, repositories, application services, HTTP API
├── modules/    # Reusable business modules
├── host/       # Host application for development and deployment
│   ├── src/    # ASP.NET Core API backend
│   └── angular/# Angular SPA frontend
└── docs/       # Project documentation
```

## Pre-requirements

* [.NET 10.0+ SDK](https://dotnet.microsoft.com/download/dotnet)
* [Node.js v18 or later](https://nodejs.org/en)

## Getting Started (Local Development)

### 1. Configure the database

Create `host/src/appsettings.Development.json` with your local database connection:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug"
    }
  },
  "ConnectionStrings": {
    "Default": "Server=YOUR_DB_SERVER;Database=Paperbase-Dev;User ID=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=true"
  },
  "StringEncryption": {
    "DefaultPassPhrase": "YOUR_ENCRYPTION_KEY"
  }
}
```

> This file is git-ignored. In Development mode, the application automatically generates temporary OpenIddict certificates — no `.pfx` file is needed.

### 2. Install client-side libraries

The host project includes a login UI that requires client-side libraries. Run once after cloning or when dependencies change:

```bash
cd host/src
abp install-libs
```

### 3. Run the backend

```bash
cd host/src
dotnet run
```

### 4. Install frontend dependencies and run Angular

```bash
cd host/angular
npm install
npm start
```

## Deploying to Production

### Generating a Signing Certificate

In the production environment, you need a signing certificate. Generate one with:

```bash
dotnet dev-certs https -v -ep openiddict.pfx -p <your-certificate-passphrase>
```

> Replace `<your-certificate-passphrase>` with a strong random password. Save it — you will need it in `appsettings.Production.json`.

For more information, refer to: [OpenIddict Certificate Configuration](https://documentation.openiddict.com/configuration/encryption-and-signing-credentials.html#registering-a-certificate-recommended-for-production-ready-scenarios)

### Production Configuration

The `appsettings.Production.json` file is git-ignored. Create it manually in the deployment directory alongside `openiddict.pfx`:

```json
{
  "ConnectionStrings": {
    "Default": "<your production database connection string>"
  },
  "AuthServer": {
    "CertificatePassPhrase": "<your-certificate-passphrase>"
  },
  "StringEncryption": {
    "DefaultPassPhrase": "<your encryption key — never change this after data has been written>"
  }
}
```

### Deploy with Docker

Build images locally:

```bash
cd host/etc/build
./build-images-locally.ps1
```

Start with Docker Compose:

```bash
cd host/etc/docker
./run-docker.ps1
```

Stop containers:

```bash
cd host/etc/docker
./stop-docker.ps1
```

## Resources

* [Project Documentation](./docs/)
* [Angular Application](./host/angular/README.md)
* [ABP Framework Documentation](https://abp.io/docs/latest)
* [Application (Single Layer) Startup Template](https://abp.io/docs/latest/solution-templates/application-single-layer)
* [Configuring OpenIddict for Production](https://abp.io/docs/latest/Deployment/Configuring-OpenIddict#production-environment)
