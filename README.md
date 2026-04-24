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
└── docs/       # Developer documentation and design documents
```

## Pre-requirements

* [.NET 10.0+ SDK](https://dotnet.microsoft.com/download/dotnet)
* [Node.js v18 or later](https://nodejs.org/en)
* PostgreSQL 16+ with the **pgvector** extension

### Installing pgvector

**Ubuntu / Debian (including WSL):**

```bash
sudo apt install -y postgresql-16-pgvector
```

If the package is not found, add the official PGDG repository first:

```bash
sudo sh -c 'echo "deb https://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" > /etc/apt/sources.list.d/pgdg.list'
wget -qO- https://www.postgresql.org/media/keys/ACCC4CF8.asc | sudo tee /etc/apt/trusted.gpg.d/postgresql.asc > /dev/null
sudo apt update
sudo apt install -y postgresql-16-pgvector
```

**Docker:** use the `pgvector/pgvector:pg17` image instead of `postgres:17` — pgvector is pre-installed.

**Other platforms:** see the [pgvector installation guide](https://github.com/pgvector/pgvector#installation).

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

* [Configuration Guide](./docs/configuration.md)
* [Angular Application](./host/angular/README.md)
* [ABP Framework Documentation](https://abp.io/docs/latest)
* [Application (Single Layer) Startup Template](https://abp.io/docs/latest/solution-templates/application-single-layer)
* [Configuring OpenIddict for Production](https://abp.io/docs/latest/Deployment/Configuring-OpenIddict#production-environment)
