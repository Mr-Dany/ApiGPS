# LocationIngestor — Render (Docker)

## Deploy (Render)
1. Sube este repo a GitHub.
2. En Render crea **Web Service** con entorno **Docker** (clona tu repo).
3. En Settings:
   - **Dockerfile path**: `/Dockerfile`
   - **Docker Build Context**: `/`
   - **Health Check Path**: `/health`
   - (Opcional) **Add Disk** y setear env `Storage:BaseDir` al punto de montaje (p. ej. `/var/data`) para persistir logs entre deploys/restarts.
4. Deploy.

## Test
```
curl -s https://<tu-servicio>.onrender.com/health
curl -s -X POST https://<tu-servicio>.onrender.com/api/locations \
  -H "Content-Type: application/json" \
  -d '{"locations":[{"lm_device_id":"","lm_latitude":"-2.16144861","lm_longitude":"-79.91768754","lm_device_alias":"a","lm_datetime":"2025-10-01 19:34:13.247"}]}'
```

## Local
```
dotnet restore
dotnet run
# http://localhost:5088/swagger
```
