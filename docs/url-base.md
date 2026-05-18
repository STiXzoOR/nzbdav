# Hosting under a sub-path (`URL_BASE`)

Out of the box, nzbdav serves its UI, SABnzbd-compatible API, and WebDAV mount at
the **root** of the configured port (e.g. `http://nzbdav:3000/`). To host the app
under a sub-path of a reverse proxy — for example `https://example.com/nzbdav/` —
set the `URL_BASE` environment variable. The app rewrites all asset URLs, React
Router routes, API calls, and WebSocket connections to live under that prefix.

## Configuring `URL_BASE`

`URL_BASE` must be set at **both** Docker build time and runtime — they configure
two halves of the same setting:

| Stage    | What it controls                                                        |
| -------- | ----------------------------------------------------------------------- |
| Build    | React Router basename, Vite asset paths, the `__URL_BASE__` JS constant |
| Runtime  | Express middleware mount prefix, server-issued redirects                |

The two values **must match**. Mismatched values will break navigation.

### Accepted values

| Input              | Normalized to | Meaning                       |
| ------------------ | ------------- | ----------------------------- |
| (unset) / `""`     | `""`          | App at root (default)         |
| `"/"`              | `""`          | App at root                   |
| `"/nzbdav"`        | `"/nzbdav"`   | App under `/nzbdav`           |
| `"/nzbdav/"`       | `"/nzbdav"`   | Trailing slash dropped        |
| `"nzbdav"`         | `"/nzbdav"`   | Leading slash added           |

Nested paths (e.g. `/apps/nzbdav`) are supported.

### Docker Compose example

```yaml
services:
  nzbdav:
    build:
      context: .
      args:
        URL_BASE: /nzbdav
    environment:
      URL_BASE: /nzbdav
    ports:
      - "3000:3000"
```

### Plain Docker example

```sh
docker build --build-arg URL_BASE=/nzbdav -t nzbdav .
docker run --rm -p 3000:3000 -e URL_BASE=/nzbdav nzbdav
```

If you skip `--build-arg` but set the runtime env var (or vice versa), the
browser will load HTML and assets from one path while the Express server only
answers requests at the other — pages will return 404s or render with broken
links.

### Why is this a build arg, not just an env var?

React Router v7's basename and Vite's asset base must be known at build time so
they can be embedded into the emitted HTML and bundled JS. There's no supported
way to swap them at process start without rebuilding the client bundle. The
runtime env var on top is what tells the Express server which prefix to mount
middleware under so requests reach the right handlers.

## Reverse-proxy configuration

With native `URL_BASE` support, your reverse proxy just needs to forward traffic
under that prefix to nzbdav, with WebSocket upgrade headers. No response
rewriting (`sub_filter` or equivalent) is required.

### nginx

```nginx
location /nzbdav/ {
    proxy_pass http://127.0.0.1:3000/nzbdav/;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Host $host;
    proxy_set_header X-Forwarded-Proto $scheme;
    # WebSocket upgrade
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection $http_connection;
}
```

### Caddy

```caddyfile
example.com {
    handle_path /nzbdav/* {
        reverse_proxy 127.0.0.1:3000/nzbdav/*
    }
}
```

### Traefik

```yaml
http:
  routers:
    nzbdav:
      rule: "Host(`example.com`) && PathPrefix(`/nzbdav`)"
      service: nzbdav
  services:
    nzbdav:
      loadBalancer:
        servers:
          - url: "http://nzbdav:3000"
```

## Configuring downstream clients

After enabling `URL_BASE`:

- **Sonarr / Radarr (SABnzbd download client):** set the **URL Base** field
  under the SABnzbd client config to the same value (e.g. `/nzbdav`). The Host
  remains the bare hostname.
- **rclone (WebDAV):** point the remote at
  `https://example.com/nzbdav/content` (or whichever WebDAV path you mount). The
  mount paths themselves do not change — only the prefix.
