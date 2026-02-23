# Polarion MCP Servers

This repository contains Model Context Protocol (MCP) server implementations for Polarion Application Lifecycle Management (ALM) integration.

MCP Tools are available for Polarion work items, including:

**Read Tools:**
- `get_workitem`: Gets the main text content for a WorkItem, optionally at a specific revision.
- `get_workitem_details`: Gets detailed information including status, type, assignee, custom fields, and linked work items with recursive link traversal.
- `get_workitem_history`: Gets revision history for a work item with content at different revisions.
- `search_workitems`: Searches for WorkItems across the project using Polarion Lucene search.
- `search_workitems_advanced`: Advanced search with Lucene query syntax, custom field retrieval, and sorting.
- `search_in_document`: Searches for WorkItems within a document based on text criteria.
- `list_documents`: Lists documents in the project, optionally filtered by space or title.
- `list_spaces`: Lists all available space names in the project.
- `list_workitem_types`: Lists all WorkItem types available in the project.
- `list_custom_fields`: Lists all available custom fields for specific WorkItem types.
- `get_document_info`: Gets document metadata and details.
- `get_document_outline`: Gets the list of sections in a document.
- `get_document_section`: Gets the content of a specific section in a document.
- `get_document_revision_history`: Gets revision history of a document.
- `get_workitems_in_module`: Gets all work items in a document/module, with optional revision support.
- `get_workflow_actions`: Lists available workflow transitions (status changes) for a work item.
- `get_traceability_graph`: Generates a Mermaid flowchart of work item link relationships with recursive traversal.

**Write Tools** (require `polarion:write` scope on remote server):
- `create_workitem`: Creates a new work item with type, title, description, status, and custom fields.
- `update_workitem`: Updates existing work item fields (title, description, status, custom fields).
- `link_workitems`: Adds traceability links between work items with configurable role and direction.
- `unlink_workitems`: Removes traceability links between work items.
- `perform_workflow_action`: Executes a status transition on a work item with optional audit comment.
- `add_comment`: Adds a rich-text comment (HTML) to a work item.
- `bulk_update_workitems`: Batch updates multiple work items (up to 50) with per-item error handling.
- `bulk_add_comment`: Batch adds comments to multiple work items (up to 50).

## Projects

- **PolarionRemoteMcpServer**: (Streamable HTTP or SSE) based MCP server for server based installations
- **PolarionMcpServer**: Console-based MCP server for Polarion integration for local workstation installations

## Running via Docker & Linux Server (Recommended)

1. From your Linux server, create a directory for your configuration and logs:

   ```bash
   mkdir -p /opt/polarion-mcp-server
   cd /opt/polarion-mcp-server
   ```

1. Pull the Docker image:

   ```bash
   docker pull peakflames/polarion-remote-mcp-server
   ```

1. Create a tailored `/opt/polarion-mcp-server/appsettings.json` file to your Polarion configuration:

   ```json
   {
     "Logging": {
       "LogLevel": {
         "Default": "Information",
         "Microsoft.AspNetCore": "Warning"
       }
     },
     "AllowedHosts": "*",
     "ApiConsumers": {
       "Consumers": {
         "my_app": {
           "Name": "My Application",
           "ApplicationKey": "your-secure-api-key-here",
           "Active": true,
           "AllowedScopes": ["polarion:read", "polarion:write"],
           "Description": "API consumer for my application"
         }
       }
     },
     "PolarionProjects": [
          {
              "ProjectUrlAlias": "starlight", 
              "Default": true,
              "SessionConfig": { 
                  "ServerUrl": "https://polarion.int.mycompany.com/",
                  "Username": "shared_user_read_only",
                  "Password": "<YOUR_PASSWORD_HERE>", 
                  "ProjectId": "Starlight_Main", 
                  "TimeoutSeconds": 60
              },
              "PolarionWorkItemTypes": [
                {
                  "id": "requirement",
                  "fields": ["custom_field_1", "priority", "severity"]
                },
                {
                  "id": "defect",
                  "fields": ["defect_type", "found_in_build"]
                }
              ]
          },
          {
              "ProjectUrlAlias": "octopus", 
              "Default": false,
              "SessionConfig": { 
                  "ServerUrl": "https://polarion.int.mycompany.com/",
                  "Username": "some_other_user",
                  "Password": "<YOUR_PASSWORD_HERE>", 
                  "ProjectId": "octopus_gov", 
                  "TimeoutSeconds": 60
              }
          },
          {
              "ProjectUrlAlias": "grogu", 
              "Default": false,
              "SessionConfig": { 
                  "ServerUrl": "https://polarion-dev.int.mycompany.com/",
                  "Username": "vader",
                  "Password": "<YOUR_PASSWORD_HERE>", 
                  "ProjectId": "grogu_boss", 
                  "TimeoutSeconds": 60
              }
          }
      ]
   }
   ```

1. Run the Docker container:

   ```bash
   docker run -d \
     --name polarion-mcp-server \
     -p 8080:8080 \
     -v appsettings.json:/app/appsettings.json \
     peakflames/polarion-remote-mcp-server
   ```

1. The server should now be running. MCP clients will connect using a URL specific to the desired project configuration alias:
   1. Streamable HTTP Transport: `http://{{your-server-ip}}:8080/{ProjectUrlAlias}`.
   2. SSE Transport: `http://{{your-server-ip}}:8080/{ProjectUrlAlias}/sse`.
2. The server also provides:
   - REST API: `http://{{your-server-ip}}:8080/polarion/rest/v1/projects/{ProjectId}/...` (uses `SessionConfig.ProjectId`)
     - **Note:** REST API endpoints require API key authentication via `X-API-Key` header
   - API Documentation: `http://{{your-server-ip}}:8080/scalar/v1` (includes authentication UI)
   - Health Check: `http://{{your-server-ip}}:8080/api/health`
3. 📢IMPORTANT - Do NOT run with replica instances of the server as the session connection will not be shared between replicas.

### Configuration Options

**Configuration Files:**
- `appsettings.json` - Base configuration for production/server deployments
- `appsettings.Development.json` - Overrides base settings for local development (gitignored, takes precedence in Development mode)
- `.env` - Optional environment variables (copy from `.env.example`), can set `POLARION_DEFAULT_PROJECT`

The server uses a `PolarionProjects` array in `appsettings.json` to define one or more Polarion instance configurations. Each object in the array represents a distinct configuration accessible via a unique URL alias.

| Top-Level Setting | Description                                                                 |
| ----------------- | --------------------------------------------------------------------------- |
| `PolarionProjects`  | (Array) Contains one or more Polarion project configuration objects.        |

**Each Project Configuration Object:**

| Setting                   | Description                                                                                                | Required | Default         |
| ------------------------- | ---------------------------------------------------------------------------------------------------------- | -------- | --------------- |
| `ProjectUrlAlias`         | A unique string used in the connection URL (`/{ProjectUrlAlias}/sse`) to identify this configuration.        | Yes      | N/A             |
| `Default`                 | (boolean) If `true`, this configuration is used if the client connects without specifying a `ProjectUrlAlias`. Only one entry can be `true`. | No       | `false`         |
| `SessionConfig`           | (Object) Contains the specific connection details for this Polarion instance.                              | Yes      | N/A             |
| `PolarionWorkItemTypes`   | (Array, Optional) Defines custom fields to retrieve for specific WorkItem types within this project. Each object in the array should have an `id` (string, WorkItem type ID) and `fields` (array of strings, custom field names). | No       | Empty List      |

**`SessionConfig` Object Details:**

| Setting        | Description                                                         | Required | Default |
| -------------- | ------------------------------------------------------------------- | -------- | ------- |
| `ServerUrl`    | URL of the Polarion server (e.g., "https://polarion.example.com/")  | Yes      | N/A     |
| `Username`     | Polarion username with appropriate permissions.                     | Yes      | N/A     |
| `Password`     | Password for the Polarion user. **(Consider secure alternatives)**    | Yes*     | N/A     |
| `ProjectId`    | The *actual* ID of the Polarion project to interact with.           | Yes      | N/A     |
| `TimeoutSeconds` | Connection timeout in seconds.                                      | No       | `60`    |

\* Either `Password` or `PersonalAccessToken` (at the project level) must be provided. PAT takes priority when both are set.

**Additional Project-Level Settings:**

| Setting                   | Description                                                                 | Required | Default |
| ------------------------- | --------------------------------------------------------------------------- | -------- | ------- |
| `PersonalAccessToken`     | Polarion Personal Access Token (PAT). When set, used instead of Username/Password. | No       | N/A     |
| `EnforceMcpScopes`        | Whether MCP scope enforcement is active for this project.                   | No       | `true`  |

### Credential Priority Chain

Credentials can be provided via appsettings or environment variables. The resolution order (highest priority first):

1. `POLARION_{ALIAS}_PAT` — Per-project PAT env var (alias normalized to uppercase alphanumeric + underscore)
2. `POLARION_PAT` — Global PAT env var (applies to the project marked `Default`)
3. `POLARION_{ALIAS}_PASSWORD` — Per-project password env var
4. `POLARION_PASSWORD` — Global password env var (applies to the project marked `Default`)
5. `PersonalAccessToken` in appsettings.json
6. `Password` in appsettings.json

Example:
```bash
export POLARION_STARLIGHT_PAT="eyJhbGci..."     # For project with alias "starlight"
export POLARION_PAT="eyJhbGci..."                  # Fallback for default project
```

*Note: It is strongly recommended to use environment variables or other secure methods (User Secrets, Azure Key Vault, etc.) rather than placing plain text passwords in `appsettings.json`.*

### API Key Authentication (REST API Only)

REST API endpoints require authentication via API key. Configure API consumers in the `ApiConsumers` section of `appsettings.json`:

| Setting | Description | Required |
| ------- | ----------- | -------- |
| `ApiConsumers.Consumers` | Dictionary of consumer configurations keyed by consumer ID | Yes |
| `Name` | Display name for the API consumer | Yes |
| `ApplicationKey` | The API key used for authentication | Yes |
| `Active` | Whether the consumer is allowed to authenticate | Yes |
| `AllowedScopes` | List of scopes (e.g., `["polarion:read", "polarion:write"]`) | Yes |
| `Description` | Optional description of the consumer | No |

**Available Scopes:**
- `polarion:read` — Read access to all REST API and read-only MCP tools
- `polarion:write` — Write access to mutation MCP tools (create, update, link, comment, workflow)
- `polarion:admin` — Administrative operations (reserved for future use)

**Usage:**
```bash
curl -H "X-API-Key: your-api-key" http://localhost:8080/polarion/rest/v1/projects/{projectId}/spaces
```

**Note:** MCP endpoints, health checks (`/api/health`, `/api/version`), and API documentation (`/scalar/v1`) do not require authentication.

## Configuring MCP Clients

**To configure Cline:**

1. Open Cline's MCP settings UI
1. Click the "Remote Servers" tab
1. For each `ProjectUrlAlias` in your `appsettings.json` that the user wants to connect to:

  ```json
  {
    "mcpServers": {
      ...
      ...

      "Polarion Starling": {
        "autoApprove": [],
        "disabled": true,
        "timeout": 60,
        "url": "http://{{your-server-ip}}:8080/starlight/sse",
        "transportType": "sse"
      },
      "Polarion Octopus": {
        "autoApprove": [],
        "disabled": true,
        "timeout": 60,
        "url": "http://{{your-server-ip}}:8080/octopus/sse",
      "transportType": "sse"
      }
    ...
    ...
  }
   ```

1. Repeat for each `ProjectUrlAlias` you want to connect to.

**To configure Visual Studio Code:**

Add the following configuration to your settings.json file:

```json
"servers": {
    "polarion-starlight": { // Use a descriptive key
        "type": "sse",
        "url": "http://{{your-server-ip}}:8080/starlight/sse", // Replace with your alias
        "env": {}
    },
    "polarion-octopus": { 
        "type": "sse",
        "url": "http://{{your-server-ip}}:8080/octopus/sse", // Replace with your alias
        "env": {}
    }
    // Add entries for each ProjectUrlAlias
}
```

**To Claude Desktop:**

Claude Desktop currently doesn’t support SSE, but you can use a proxy with the following addition to the claude_desktop_config.json file:

```json
{
  "mcpServers": {
    "polarion-remote": {
      "command": "npx",
      "args": [
        "mcp-remote",
        "http://{{your-server-ip}}:8080/{ProjectUrlAlias}/sse" // Replace {ProjectUrlAlias}
      ]
    }
    // Add entries for each ProjectUrlAlias, potentially using different keys like "polarion-starlight"
  }
}
```

## Running Locally (stdio)

For local development or workstation use, you can run the stdio-based MCP server:

1. Download the appropriate executable for your platform from the [releases page](https://github.com/peakflames/PolarionMcpServers/releases)
2. Configure your MCP client to use the stdio transport with the executable path

## Contributing

For developers who want to contribute or build from source, see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

See [LICENSE](LICENSE) for details.
